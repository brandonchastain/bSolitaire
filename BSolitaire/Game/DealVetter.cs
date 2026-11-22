using System.Diagnostics;

namespace BSolitaire.Game;

/// <summary>
/// Chooses the deals the player is given, so nobody is handed one that was never winnable.
/// Being told a deal is lost is a poor consolation for having played it; not dealing it is
/// the fix, and it is what other solitaire games do.
///
/// This consumes exactly one half of the <see cref="Solver"/>'s verdict: a deal is kept only
/// when a win has been <em>found</em>, and discarded on anything else. That asymmetry is the
/// whole reason this is affordable. Finding one winning line is cheap; proving that none
/// exists means exhausting the tree. Measured over sixty fresh openings at
/// <see cref="SolverBudget.Default"/>: 63% proved winnable in a median of one millisecond,
/// none at all were proved unwinnable, and the rest ran out of budget at a 90th percentile of
/// 56ms. The same openings at <see cref="SolverBudget.Thorough"/> raised winnable only to
/// 68% while the 90th percentile went to 1.8 seconds — thirty times the cost for five points
/// — so the smaller budget is the right one, and a candidate that does not prove itself
/// inside it is thrown away rather than investigated. Discarding a good deal costs a reshuffle;
/// investigating a bad one costs the frame.
///
/// At 63% kept, a deal takes about one and a half candidates, so the search runs in the idle
/// frames of the game already in progress and the next deal is normally ready long before it
/// is asked for.
///
/// What the player is promised is "a win exists from here for someone who can see the
/// face-down cards" — the only question the search can answer. It is not a promise that they
/// will find it, and a deal can still be lost by playing it badly. That is the game.
///
/// Under a draw of three the promise is weaker, and knowingly so. The <see cref="Solver"/>
/// models the stock as a set rather than a sequence, so it may win using a card the real
/// deal would never put on top of the waste. The verdict in the stats overlay is marked for
/// that; the deal still carries a proof, only one made in a game slightly kinder than the
/// one being played.
///
/// How much weaker is not measured, and the reason is worth recording rather than leaving
/// as an omission. Measuring it means proving kept deals <em>dead</em> under real draw-three
/// grouping, which needs a draw-aware search this codebase does not have — and deadness is
/// the expensive half of the asymmetry above: of those sixty openings, not one was proved
/// unwinnable. So the instrument would cost about what fixing it would, and would mostly
/// answer "ran out of budget" once built. The honest position is the marked verdict, until
/// somebody wants the number enough to pay for a draw-aware search and the hours to run it.
/// </summary>
internal sealed class DealVetter
{
    /// <summary>Positions per frame, matching <see cref="Analyzer"/>: a machine fast enough
    /// to burn the budget inside four milliseconds still spreads the work out.</summary>
    private const int SearchSlice = 8000;

    /// <summary>
    /// How many candidates a deal nobody has vetted yet is worth. At 37% discarded, eight
    /// attempts leave about a four-in-ten-thousand chance of running out, and the time
    /// budget below stops it long before the count does on a slow machine.
    /// </summary>
    private const int ImmediateAttempts = 8;

    /// <summary>The slice of a frame the search may have. Four milliseconds for the same
    /// reason <see cref="Analyzer"/> takes four: everything here shares a thread.</summary>
    private static readonly TimeSpan SearchBudget = TimeSpan.FromMilliseconds(4);

    /// <summary>
    /// How long the player is kept waiting when no deal has been vetted yet — the first deal
    /// of a session, or dealing again faster than the search keeps up. A quarter of a second
    /// is the ceiling, not the expectation: the measured median is a single millisecond, and
    /// the alternative to waiting is handing out a deal nobody has looked at.
    /// </summary>
    private static readonly TimeSpan ImmediateBudget = TimeSpan.FromMilliseconds(250);

    private readonly Dealer dealer = new();

    /// <summary>
    /// Somewhere to deal a candidate so a search can be pointed at it. Only ever a way to
    /// hand a dealt position to a <see cref="Solver"/>, which packs it into its own
    /// representation in its constructor and never reads it again — so one position serves
    /// every candidate, and taking it apart cannot disturb a search already running.
    /// </summary>
    private readonly Position scratch = new();

    private (Suit Suit, Rank Rank)[]? candidate;
    private Solver? search;
    private (Suit Suit, Rank Rank)[]? vetted;

    /// <summary>Whether a deal is ready to be handed over without anyone waiting for it.</summary>
    public bool HasDeal => vetted is not null;

    /// <summary>Candidates thrown away. Nothing depends on it; it is how the tests and the
    /// stats overlay can see the search working.</summary>
    public int Discarded { get; private set; }

    /// <summary>
    /// Deals handed out that nothing had proved anything about, because the wait ran out
    /// before a candidate proved itself.
    ///
    /// Rare by construction and silent until now, which is the problem with it: if this
    /// ever moves in real play then the attempt count or the quarter-second ceiling is
    /// wrong for that machine, and that is worth knowing rather than guessing at. It is an
    /// instrument, not a score — the player is told nothing, because the deal they were
    /// given carries no promise to tell them about.
    /// </summary>
    public int Unvetted { get; private set; }

    /// <summary>
    /// Gives the search its slice of one frame.
    /// </summary>
    /// <param name="paused">
    /// True when the frame has something better to do. The game in progress has first call on
    /// the time — its own verdict is what raises the banner, and this is work for a deal
    /// nobody has asked for yet.
    /// </param>
    public void Update(bool paused)
    {
        if (paused || vetted is not null)
        {
            return;
        }

        if (search is null)
        {
            candidate = Dealer.Shuffle();
            search = StartSearch(candidate);
        }

        if (!search.Step(SearchSlice, SearchBudget))
        {
            return;
        }

        if (search.Result == SolveResult.Winnable)
        {
            vetted = candidate;
        }
        else
        {
            Discarded++;
        }

        candidate = null;
        search = null;
    }

    /// <summary>
    /// The deal to play next, or null if even waiting could not find one — in which case the
    /// caller deals a shuffle as it always did. Never blocks for longer than
    /// <see cref="ImmediateBudget"/>: a deal in hand now beats a better deal later, and the
    /// unvetted deal is the behaviour this replaces rather than a regression from it.
    /// </summary>
    public IReadOnlyList<(Suit Suit, Rank Rank)>? TakeDeal()
    {
        if (vetted is { } ready)
        {
            vetted = null;
            return ready;
        }

        long startedAt = Stopwatch.GetTimestamp();

        for (int attempt = 0; attempt < ImmediateAttempts; attempt++)
        {
            var remaining = ImmediateBudget - Stopwatch.GetElapsedTime(startedAt);

            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            var order = Dealer.Shuffle();
            var immediate = StartSearch(order);

            // One call rather than a loop: Step runs until it has an answer or runs out of
            // the time it was given, and out of time is a discard here like any other.
            if (immediate.Step(int.MaxValue, remaining) && immediate.Result == SolveResult.Winnable)
            {
                return order;
            }

            Discarded++;
        }

        Unvetted++;
        return null;
    }

    private Solver StartSearch(IReadOnlyList<(Suit Suit, Rank Rank)> order)
    {
        scratch.Strip();
        dealer.Deal(scratch, order);
        return new Solver(scratch);
    }
}
