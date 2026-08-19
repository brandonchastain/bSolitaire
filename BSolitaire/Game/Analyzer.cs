namespace BSolitaire.Game;

/// <summary>
/// Keeps a <see cref="Solver"/> pointed at the position on the board and feeds it a slice of
/// each frame. This is scheduling, not solitaire: the game does not care how the verdict was
/// reached, only that one eventually arrives, and the search does not care which frame it is
/// running in. Splitting the two out means neither has to hold the other's bookkeeping.
/// </summary>
public sealed class Analyzer
{
    /// <summary>
    /// How long the search may run inside one frame. This is the number that decides whether
    /// the board feels responsive: everything here shares a thread, so whatever the search
    /// spends is added directly to the frame it runs in. Four milliseconds leaves the rest of
    /// a sixty-hertz frame free, and unlike a position count it means the same thing on a
    /// phone as on a desktop — slow hardware takes more frames rather than dropping them.
    /// </summary>
    private static readonly TimeSpan SearchBudget = TimeSpan.FromMilliseconds(4);

    /// <summary>
    /// A ceiling on positions per frame as well, so a machine fast enough to burn the whole
    /// node budget inside four milliseconds still spreads the work out rather than doing it
    /// all in one frame.
    /// </summary>
    private const int SearchSlice = 8000;

    private readonly Board board;

    private Solver? solver;
    private int analysedVersion = -1;

    public Analyzer(Board board) => this.board = board;

    /// <summary>How the search on the current position is going. Null before it starts.</summary>
    public SolveResult Result => solver?.Result ?? SolveResult.Searching;

    /// <summary>Positions the search has examined on the current board.</summary>
    public int Nodes => solver?.Nodes ?? 0;

    /// <summary>Distinct positions the search is holding on to.</summary>
    public int States => solver?.States ?? 0;

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
        // A move invalidates whatever the search was working on; start again on the new
        // position. Restarting rather than repairing is the honest thing: a move can turn a
        // lost position into a won one and vice versa.
        if (analysedVersion != board.Version)
        {
            analysedVersion = board.Version;
            solver = board.State == GameState.Playing ? new Solver(board) : null;
        }

        if (paused || solver == null || solver.Done)
        {
            return false;
        }

        if (solver.Step(SearchSlice, SearchBudget) && solver.Result == SolveResult.Unwinnable)
        {
            board.MarkUnwinnable();
            return true;
        }

        return false;
    }
}
