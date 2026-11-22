namespace BSolitaire.Game;

/// <summary>
/// Keeps a <see cref="Solver"/> pointed at the position on the board and feeds it a slice of
/// each frame. This is scheduling, not solitaire: the game does not care how the verdict was
/// reached, only that one eventually arrives, and the search does not care which frame it is
/// running in. Splitting the two out means neither has to hold the other's bookkeeping.
/// </summary>
internal sealed class Analyzer
{
    /// <summary>
    /// A ceiling on positions per frame as well, so a machine fast enough to burn the whole
    /// node budget inside four milliseconds still spreads the work out rather than doing it
    /// all in one frame.
    /// </summary>
    private const int SearchSlice = 8000;

    /// <summary>
    /// How long the search may run inside one frame. This is the number that decides whether
    /// the board feels responsive: everything here shares a thread, so whatever the search
    /// spends is added directly to the frame it runs in. Four milliseconds leaves the rest of
    /// a sixty-hertz frame free, and unlike a position count it means the same thing on a
    /// phone as on a desktop — slow hardware takes more frames rather than dropping them.
    /// </summary>
    private static readonly TimeSpan SearchBudget = TimeSpan.FromMilliseconds(4);

    private readonly Board board;

    private Solver? solver;
    private int analysedVersion = -1;
    private int analysedDeal = -1;

    public Analyzer(Board board) => this.board = board;

    /// <summary>How the search on the current position is going. Null before it starts.</summary>
    public SolveResult Result => solver?.Result ?? SolveResult.Searching;

    /// <summary>Positions the search has examined on the current board.</summary>
    public int Nodes => solver?.Nodes ?? 0;

    /// <summary>
    /// Whether this has finished with the position in front of it, so the frame's slice is
    /// going spare. True on a board that is over, where there is nothing to ask.
    /// </summary>
    public bool Done => solver is null || solver.Done;

    /// <summary>
    /// How far back the last position this proved winnable is, as an undo depth, or -1 if
    /// there is no such position in this deal.
    ///
    /// A depth rather than a <see cref="Board.Version"/>, because undo bumps the version
    /// like any other change — there is no version to go back to. Undo is a pure stack, so
    /// a depth is an identity: unwind to it and the position is the one that was proved.
    ///
    /// Only positions the search actually <em>proved</em> winnable are anchored on. One it
    /// merely ran out of budget on is not somewhere to send the player back to and call it
    /// safe, and the solver's standard of proof is the whole reason its answers are worth
    /// anything.
    /// </summary>
    public int WinnableDepth { get; private set; } = -1;

    /// <summary>
    /// Gives the search its slice of one frame, and reports whether the board changed as a
    /// result — the only way it can is a position being proved dead.
    /// </summary>
    /// <param name="paused">
    /// True when the frame has something better to do with the time. A held stack is the one
    /// time the board animates, and the search is the one thing big enough to be felt inside a
    /// frame. Everything is on one thread here, so the slice and the draw are strictly in
    /// series: spending it mid-drag buys an answer nobody is waiting for at the cost of the
    /// only motion the player can see. The search resumes the moment the board is still again.
    /// </param>
    public bool Update(bool paused)
    {
        if (analysedDeal != board.DealId)
        {
            // A new deal. Whatever was proved about the last one is about cards that are no
            // longer on the table.
            analysedDeal = board.DealId;
            WinnableDepth = -1;
        }

        if (board.UndoDepth < WinnableDepth)
        {
            // The player has gone back past it by hand. Anything they do from here is a
            // different line, and the old anchor is a position on a line nobody is on.
            WinnableDepth = -1;
        }

        // A move invalidates whatever the search was working on; start again on the new
        // position. Restarting rather than repairing is the honest thing: a move can turn a
        // lost position into a won one and vice versa.
        if (analysedVersion != board.Version)
        {
            analysedVersion = board.Version;
            solver = board.State == GameState.Playing ? new Solver(board.Position) : null;
        }

        if (paused || solver == null || solver.Done)
        {
            return false;
        }

        if (!solver.Step(SearchSlice, SearchBudget))
        {
            return false;
        }

        if (solver.Result == SolveResult.Winnable)
        {
            // Somewhere to come back to, if the player later plays themselves out of it.
            WinnableDepth = board.UndoDepth;
        }

        // A verdict worth drawing: the board itself has not changed, but what the game has
        // to say about it has.
        return solver.Result == SolveResult.Unwinnable;
    }
}
