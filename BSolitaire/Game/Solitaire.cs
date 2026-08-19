namespace BSolitaire.Game;

/// <summary>
/// One game session: the pieces, and the frame they share. The rules live in
/// <see cref="Rules"/>, moves in <see cref="Board"/>, geometry in <see cref="BoardLayout"/>,
/// input in <see cref="Controls"/>, the search's frame budget in <see cref="Analyzer"/>, the
/// play-it-out pacing in <see cref="FastForward"/>, and the record in <see cref="ScoreKeeper"/>.
///
/// What is left here is the part none of them can hold: giving each one its slice of a frame,
/// carrying out the handful of commands the player can ask for, and telling the host the two
/// things it needs to know — whether the picture is out of date, and whether anything blew up.
/// It knows nothing about Blazor, canvas, or JS, so it stays unit-testable.
/// </summary>
public class Solitaire
{
    private readonly Controls controls;
    private readonly Analyzer analyzer;
    private readonly FastForward fastForward;
    private readonly ScoreKeeper scores;

    public Solitaire()
    {
        Board = new Board();
        // Placeholder size; the host calls Resize with the real viewport on first render.
        Layout = new BoardLayout(800, 600);
        controls = new Controls(Board, Layout);
        analyzer = new Analyzer(Board);
        fastForward = new FastForward(Board);
        scores = new ScoreKeeper(Board);
    }

    public Board Board { get; }

    public BoardLayout Layout { get; }

    /// <summary>The local player's running record. The host loads it before the first frame
    /// and saves it whenever <see cref="ScoreChanged"/> fires.</summary>
    public PlayerScore Score => scores.Score;

    /// <summary>Raised when <see cref="Score"/> has changed and is worth persisting.</summary>
    public event Action? ScoreChanged
    {
        add => scores.Changed += value;
        remove => scores.Changed -= value;
    }

    /// <summary>The stack currently held by the pointer, or null.</summary>
    public DragState? Drag => controls.Drag;

    /// <summary>The pile the pointer is resting on and could pick up from, or null. Hidden
    /// while the board is playing itself out — nothing there is grabbable.</summary>
    public Location? HoverPile => fastForward.IsRunning ? null : controls.HoverPile;

    /// <summary>Index of the lowest card the pointer would pick up from
    /// <see cref="HoverPile"/>.</summary>
    public int HoverIndex => controls.HoverIndex;

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
    public bool CanFastForward => Board.CanFastForward && !fastForward.IsRunning && Drag == null;

    /// <summary>Whether the draw-time overlay is shown. Toggled with F.</summary>
    public bool ShowStats { get; private set; }

    /// <summary>Time since the game started. Set by <see cref="Update"/>.</summary>
    public TimeSpan Elapsed { get; private set; }

    /// <summary>
    /// Noises the board has asked for since the host last drained them. Nothing here makes a
    /// sound; the host plays them and calls <see cref="ClearSounds"/>.
    /// </summary>
    public IReadOnlyList<Sound> Sounds => Board.Sounds;

    public void ClearSounds() => Board.ClearSounds();

    /// <summary>Whether the board is silent. Toggled with M, and saved with the score.</summary>
    public bool Muted => Score.Muted;

    /// <summary>How the search on the current position is going.</summary>
    public SolveResult Analysis => analyzer.Result;

    /// <summary>Positions the search has examined on the current board.</summary>
    public int AnalysisNodes => analyzer.Nodes;

    /// <summary>Distinct positions the search is holding on to.</summary>
    public int AnalysisStates => analyzer.States;

    /// <summary>Called by the host once the current picture has been drawn.</summary>
    public void MarkClean()
    {
        NeedsRedraw = false;
        Board.ClearDirty();
    }

    public void Resize(double width, double height) => Guarded(() => Layout.Resize(width, height));

    /// <summary>
    /// Called once per animation frame, before the drawer runs. This is where the fast-forward
    /// takes its step and the search gets its slice: the board is idle between moves anyway —
    /// the frame loop is already running and drawing nothing — so proving a deal dead costs
    /// the player nothing they can feel.
    /// </summary>
    public void Update(TimeSpan elapsed)
    {
        Elapsed = elapsed;
        AdvanceFastForward();

        if (scores.Update())
        {
            NeedsRedraw = true;
        }

        if (analyzer.Update(paused: Drag != null || fastForward.IsRunning))
        {
            NeedsRedraw = true;
        }
    }

    /// <summary>
    /// Lets a running fast-forward take its step. It is outside <see cref="Guarded"/> because
    /// this runs every frame rather than on an input, and dirtying the picture unconditionally
    /// would defeat the redraw check on the frames between cards.
    /// </summary>
    private void AdvanceFastForward()
    {
        if (!fastForward.IsRunning)
        {
            return;
        }

        try
        {
            if (fastForward.Tick() != FastForwardTick.Idle)
            {
                NeedsRedraw = true;
            }
        }
        catch (Exception ex)
        {
            fastForward.Stop();
            Error = ex.ToString();
            NeedsRedraw = true;
        }
    }

    public void OnPointerDown(double x, double y) =>
        Guarded(() => Do(controls.Down(x, y, CanFastForward)));

    public void OnPointerUp(double x, double y) => Guarded(() => controls.Up(x, y));

    public void OnPointerCancel() => Guarded(controls.Cancel);

    /// <summary>Double-click at (x, y): a shortcut for sending a card to its foundation.</summary>
    public void OnDoubleClick(double x, double y) => Guarded(() => controls.DoubleClick(x, y));

    /// <summary>
    /// The one hot input path — it fires continuously — so it sidesteps <see cref="Guarded"/>
    /// and only dirties the picture while a stack is actually held. Hovering must not
    /// defeat the redraw check.
    /// </summary>
    public void OnPointerMove(double x, double y)
    {
        if (controls.Move(x, y))
        {
            NeedsRedraw = true;
        }
    }

    /// <summary>A key press, using KeyboardEvent.code values ("KeyR", "Space", "ArrowLeft"...).</summary>
    public void OnKeyDown(string code) => Guarded(() => Do(controls.Key(code)));

    /// <summary>
    /// Carries out one thing the player asked for. Every command arrives here whether it came
    /// from a key or from a button on the felt, so the rule about when each is allowed is
    /// written once and the two routes cannot drift apart.
    /// </summary>
    private void Do(PlayerCommand command)
    {
        switch (command)
        {
            case PlayerCommand.FastForward:
                // A decided game is the only kind there is nothing left to decide about.
                if (CanFastForward)
                {
                    fastForward.Start();
                }

                break;

            case PlayerCommand.ToggleMute:
                scores.ToggleMute();
                break;

            case PlayerCommand.ToggleStats:
                ShowStats = !ShowStats;
                break;

            case PlayerCommand.NewGame:
                Reset();
                break;
        }
    }

    /// <summary>Renames the player and reports the change so it gets saved.</summary>
    public void SetNickname(string nickname) => Guarded(() => scores.SetNickname(nickname));

    /// <summary>Abandons the current game and deals a new one.</summary>
    public void Reset()
    {
        fastForward.Stop();
        Guarded(Board.Reset);
    }

    /// <summary>
    /// Every input entry point funnels through here, so the error boundary and the redraw
    /// flag are each written in exactly one place instead of once per handler.
    /// </summary>
    private void Guarded(Action action)
    {
        try
        {
            action();
            Error = null;
        }
        catch (Exception ex)
        {
            Error = ex.ToString();
        }
        finally
        {
            NeedsRedraw = true;
        }
    }
}
