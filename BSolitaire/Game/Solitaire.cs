namespace BSolitaire.Game;

/// <summary>
/// One game session, and the only object the host component and the drawer talk to.
/// It owns the pieces and wires them together — the rules live in <see cref="Rules"/>,
/// moves in <see cref="Board"/>, geometry in <see cref="BoardLayout"/>, gestures in
/// <see cref="PointerInput"/>, the search's frame budget in <see cref="Analyzer"/>, the
/// play-it-out pacing in <see cref="FastForward"/>, and the record in
/// <see cref="ScoreKeeper"/>. What it adds is what the host needs and none of them should
/// know: whether the picture needs redrawing, and whether anything blew up.
/// It knows nothing about Blazor, canvas, or JS, so it stays unit-testable.
/// </summary>
public class Solitaire
{
    private readonly PointerInput input;
    private readonly Analyzer analyzer;
    private readonly FastForward fastForward;
    private readonly ScoreKeeper scores;

    private bool pressConsumed;

    public Solitaire()
    {
        Board = new Board();
        // Placeholder size; the host calls Resize with the real viewport on first render.
        Layout = new BoardLayout(800, 600);
        input = new PointerInput(Board, Layout);
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

    /// <summary>Renames the player and reports the change so it gets saved.</summary>
    public void SetNickname(string nickname)
    {
        scores.SetNickname(nickname);
        NeedsRedraw = true;
    }

    /// <summary>The stack currently held by the pointer, or null.</summary>
    public DragState? Drag => input.Drag;

    /// <summary>The pile the pointer is resting on and could pick up from, or null. Hidden
    /// while the board is playing itself out — nothing there is grabbable.</summary>
    public Location? HoverPile => fastForward.IsRunning ? null : input.HoverPile;

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
    public bool CanFastForward => Board.CanFastForward && !fastForward.IsRunning && Drag == null;

    /// <summary>Whether the rest of the game is currently playing itself out.</summary>
    public bool IsFastForwarding => fastForward.IsRunning;

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

        if (analyzer.Update(paused: input.Drag != null || fastForward.IsRunning))
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

    /// <summary>Starts playing the rest of the game out. Ignored unless the board is offering
    /// to — a decided game is the only kind there is nothing to decide about.</summary>
    public void StartFastForward()
    {
        if (!CanFastForward)
        {
            return;
        }

        fastForward.Start();
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

        // The mute toggle sits in the gap column, where no pile ever is, so it only has to
        // take the press ahead of the felt. Same swallowed release as the button above.
        if (Layout.MuteButton.Contains(x, y))
        {
            pressConsumed = true;
            ToggleMute();
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
        else if (code == "KeyM")
        {
            ToggleMute();
        }
        else if (code is "Space" or "Enter")
        {
            StartFastForward();
        }
    }

    /// <summary>Silences the board, or lets it speak again. Saved with the score, so it is
    /// still true the next time this browser opens the game.</summary>
    public void ToggleMute()
    {
        scores.ToggleMute();
        NeedsRedraw = true;
    }

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
