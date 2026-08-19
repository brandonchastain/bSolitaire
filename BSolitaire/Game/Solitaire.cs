namespace BSolitaire.Game;

/// <summary>
/// One game session, and the only object the host component and the drawer talk to.
/// It owns the pieces and wires them together — the rules live in <see cref="Rules"/>,
/// moves in <see cref="Board"/>, geometry in <see cref="BoardLayout"/>, and gestures in
/// <see cref="PointerInput"/>. What it adds is what the host needs and none of them
/// should know: whether the picture needs redrawing, and whether anything blew up.
/// It knows nothing about Blazor, canvas, or JS, so it stays unit-testable.
/// </summary>
public class Solitaire
{
    private readonly PointerInput input;

    private Solver? solver;
    private int analysedVersion = -1;

    private bool fastForwarding;
    private bool recordedThisGame;
    private bool pressConsumed;
    private int framesUntilNextCard;
    private int stepsWithoutProgress;

    public Solitaire()
    {
        Board = new Board();
        // Placeholder size; the host calls Resize with the real viewport on first render.
        Layout = new BoardLayout(800, 600);
        input = new PointerInput(Board, Layout);
    }

    public Board Board { get; }

    /// <summary>The local player's running record. The host loads it before the first frame
    /// and saves it whenever <see cref="ScoreChanged"/> fires — the game itself only counts.</summary>
    public PlayerScore Score { get; } = new();

    /// <summary>Raised when <see cref="Score"/> has changed and is worth persisting.</summary>
    public event Action? ScoreChanged;

    /// <summary>Renames the player and reports the change so it gets saved.</summary>
    public void SetNickname(string nickname)
    {
        Score.Nickname = nickname.Trim();
        NeedsRedraw = true;
        ScoreChanged?.Invoke();
    }

    public BoardLayout Layout { get; }

    /// <summary>The stack currently held by the pointer, or null.</summary>
    public DragState? Drag => input.Drag;

    /// <summary>The pile the pointer is resting on and could pick up from, or null. Hidden
    /// while the board is playing itself out — nothing there is grabbable.</summary>
    public Location? HoverPile => fastForwarding ? null : input.HoverPile;

    /// <summary>Index of the lowest card the pointer would pick up from
    /// <see cref="HoverPile"/>.</summary>
    public int HoverIndex => input.HoverIndex;

    /// <summary>Whether the game is still going, and if not, how it ended.</summary>
    public GameState State => Board.State;

    /// <summary>Last unhandled exception, painted on the board. Null when all is well.</summary>
    public string? Error { get; private set; }

    /// <summary>
    /// True when the picture is out of date. A solitaire board is static almost all the
    /// time, so the host skips drawing entirely until something changes — the canvas keeps
    /// the last frame either way.
    /// </summary>
    public bool NeedsRedraw { get; private set; } = true;

    /// <summary>
    /// Whether the board is offering to play the rest of the game out. False while it is
    /// already doing so — the offer and the act are never both on screen.
    /// </summary>
    public bool CanFastForward => Board.CanFastForward && !fastForwarding && Drag == null;

    /// <summary>Whether the rest of the game is currently playing itself out.</summary>
    public bool IsFastForwarding => fastForwarding;

    /// <summary>Whether the draw-time overlay is shown. Toggled with F.</summary>
    public bool ShowStats { get; private set; }

    /// <summary>Time since the game started. Set by <see cref="Update"/>.</summary>
    public TimeSpan Elapsed { get; private set; }

    /// <summary>Called by the host once the current picture has been drawn.</summary>
    public void MarkClean()
    {
        NeedsRedraw = false;
        Board.ClearDirty();
    }

    public void Resize(double width, double height) => Guarded(() => Layout.Resize(width, height));

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

    /// <summary>How the search on the current position is going. Null before it starts.</summary>
    public SolveResult Analysis => solver?.Result ?? SolveResult.Searching;

    /// <summary>Positions the search has examined on the current board.</summary>
    public int AnalysisNodes => solver?.Nodes ?? 0;

    /// <summary>Distinct positions the search is holding on to.</summary>
    public int AnalysisStates => solver?.States ?? 0;

    /// <summary>
    /// Called once per animation frame, before the drawer runs. This is where the search gets
    /// its time: a slice per frame, spread over however many frames it takes, so proving a
    /// deal dead costs the player nothing they can feel. The board is idle between moves
    /// anyway — the frame loop is already running and drawing nothing.
    /// </summary>
    /// <summary>
    /// Frames between cards during a fast-forward. Instant would be a worse answer than fast:
    /// the player asked to skip the clicking, not to skip seeing the game finish.
    /// </summary>
    private const int FramesPerCard = 3;

    /// <summary>
    /// A stop for a fast-forward that is turning the stock over without ever finding anything
    /// to play. It cannot happen from a position that offers the button — but the button is
    /// not the only thing that could ever call it, and a loop that never ends is a worse bug
    /// than one card left unplayed.
    /// </summary>
    private const int StallLimit = 60;

    public void Update(TimeSpan elapsed)
    {
        Elapsed = elapsed;
        AdvanceFastForward();
        RecordResult();

        // A move invalidates whatever the search was working on; start again on the new
        // position. Restarting rather than repairing is the honest thing: a move can turn a
        // lost position into a won one and vice versa.
        if (analysedVersion != Board.Version)
        {
            analysedVersion = Board.Version;
            solver = Board.State == GameState.Playing ? new Solver(Board) : null;
        }

        // A held stack is the one time the board animates, and the search is the one thing
        // big enough to be felt inside a frame. Everything is on one thread here, so the
        // slice and the draw are strictly in series: spending it mid-drag buys an answer
        // nobody is waiting for at the cost of the only motion the player can see. The
        // board stops moving the moment the stack is dropped, and the search resumes there.
        if (solver == null || solver.Done || input.Drag != null || fastForwarding)
        {
            return;
        }

        if (solver.Step(SearchSlice, SearchBudget) && solver.Result == SolveResult.Unwinnable)
        {
            Board.MarkUnwinnable();
            NeedsRedraw = true;
        }
    }

    /// <summary>
    /// Counts a deal once it is over — won, stuck, or proved lost — and once only. The flag
    /// rather than a state comparison, because a finished board keeps reporting the same
    /// state on every frame until the player deals again.
    /// </summary>
    private void RecordResult()
    {
        if (recordedThisGame || Board.State == GameState.Playing)
        {
            return;
        }

        recordedThisGame = true;
        Score.Games++;

        if (Board.State == GameState.Won)
        {
            Score.Wins++;
        }

        NeedsRedraw = true;
        ScoreChanged?.Invoke();
    }

    /// <summary>
    /// Plays one more card home, if a fast-forward is running. Paced across frames, and it
    /// gives up the moment the board stops making progress or the game ends.
    /// </summary>
    private void AdvanceFastForward()
    {
        if (!fastForwarding)
        {
            return;
        }

        if (Board.State != GameState.Playing || stepsWithoutProgress > StallLimit)
        {
            fastForwarding = false;
            NeedsRedraw = true;
            return;
        }

        if (--framesUntilNextCard > 0)
        {
            return;
        }

        framesUntilNextCard = FramesPerCard;

        int before = Board.FoundationTotal;
        if (!Guarded(Board.FastForwardStep))
        {
            fastForwarding = false;
            return;
        }

        // Turning the stock over is a step but not progress. Only cards reaching a
        // foundation reset the stall count.
        stepsWithoutProgress = Board.FoundationTotal > before ? 0 : stepsWithoutProgress + 1;
    }

    /// <summary>Starts playing the rest of the game out. Ignored unless the board is offering
    /// to — a decided game is the only kind there is nothing to decide about.</summary>
    public void StartFastForward()
    {
        if (!CanFastForward)
        {
            return;
        }

        fastForwarding = true;
        framesUntilNextCard = 1;
        stepsWithoutProgress = 0;
        NeedsRedraw = true;
    }

    public void OnPointerDown(double x, double y)
    {
        // The button sits over the felt, so it has to take the press before the piles get a
        // look at it. The release that follows has to be swallowed too: PointerInput never
        // saw the press, and left to itself it would treat the release as a tap at wherever
        // the last real press happened to be.
        if (CanFastForward && Layout.FastForwardButton.Contains(x, y))
        {
            pressConsumed = true;
            StartFastForward();
            return;
        }

        pressConsumed = false;
        Guarded(() => input.Down(x, y));
    }

    public void OnPointerUp(double x, double y)
    {
        if (pressConsumed)
        {
            pressConsumed = false;
            return;
        }

        Guarded(() => input.Up(x, y));
    }

    public void OnPointerCancel()
    {
        pressConsumed = false;
        Guarded(input.Cancel);
    }

    /// <summary>Double-click at (x, y): a shortcut for sending a card to its foundation.</summary>
    public void OnDoubleClick(double x, double y) => Guarded(() => input.DoubleClick(x, y));

    /// <summary>
    /// The one hot input path — it fires continuously — so it sidesteps <see cref="Guarded"/>
    /// and only dirties the picture while a stack is actually held. Hovering must not
    /// defeat the redraw check.
    /// </summary>
    public void OnPointerMove(double x, double y)
    {
        if (input.Move(x, y))
        {
            NeedsRedraw = true;
        }
    }

    /// <summary>A key press, using KeyboardEvent.code values ("KeyR", "Space", "ArrowLeft"...).</summary>
    public void OnKeyDown(string code)
    {
        if (code == "KeyF")
        {
            ShowStats = !ShowStats;
            NeedsRedraw = true;
        }
        else if (code == "KeyR")
        {
            Reset();
        }
        else if (code is "Space" or "Enter")
        {
            StartFastForward();
        }
    }

    /// <summary>Abandons the current game and deals a new one.</summary>
    public void Reset()
    {
        fastForwarding = false;
        recordedThisGame = false;
        Guarded(Board.Reset);
    }

    /// <summary>
    /// Every input entry point funnels through here, so the error boundary and the redraw
    /// flag are each written in exactly one place instead of once per handler.
    /// </summary>
    private void Guarded(Action action) => Guarded(() => { action(); return true; });

    private bool Guarded(Func<bool> action)
    {
        try
        {
            bool result = action();
            Error = null;
            return result;
        }
        catch (Exception ex)
        {
            Error = ex.ToString();
            return false;
        }
        finally
        {
            NeedsRedraw = true;
        }
    }
}
