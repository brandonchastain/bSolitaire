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
///
/// The class is internal: this is an application, not a library, and nothing outside the
/// assembly has any business holding a game. That is what lets the members below be public —
/// <see cref="ISolitaireView"/> requires it — without any of them becoming public API.
/// </summary>
internal sealed class Solitaire : ISolitaireView
{
    private readonly Controls controls;
    private readonly Analyzer analyzer;
    private readonly FastForward fastForward;
    private readonly ScoreKeeper scores;
    private readonly Animator animator;
    private readonly WinCascade cascade;

    /// <summary>The deal the cascade has already been thrown for, so a won board that sits
    /// on screen does not restart it on every frame.</summary>
    private int celebratedDeal = -1;

    public Solitaire()
    {
        Board = new Board();
        // Placeholder size; the host calls Resize with the real viewport on first render.
        Layout = new BoardLayout(800, 600);
        controls = new Controls(Board, Layout);
        analyzer = new Analyzer(Board);
        fastForward = new FastForward(Board);
        scores = new ScoreKeeper(Board);
        animator = new Animator(Board, Layout);
        cascade = new WinCascade(Board.Position, Layout);
    }

    /// <summary>Raised when <see cref="Score"/> has changed and is worth persisting.</summary>
    public event Action? ScoreChanged
    {
        add => scores.Changed += value;
        remove => scores.Changed -= value;
    }

    public Board Board { get; }

    public BoardLayout Layout { get; }

    /// <summary>The local player's running record. The host loads it before the first frame
    /// and saves it whenever <see cref="ScoreChanged"/> fires.</summary>
    public PlayerScore Score => scores.Score;

    /// <summary>The stack currently held by the pointer, or null.</summary>
    public DragState? Drag => controls.Drag;

    /// <summary>Cards part-way between two piles, to be painted over the board.</summary>
    public IReadOnlyList<CardInFlight> InFlight => animator.InFlight;

    /// <summary>Piles that would accept the stack being dragged. Empty unless one is held —
    /// this is the touch answer to a hover, which a finger cannot do.</summary>
    public IReadOnlyList<Location> DropTargets => controls.DropTargets;

    /// <summary>Cards bouncing down the board after a win.</summary>
    public IReadOnlyList<FallingCard> Falling => cascade.Falling;

    /// <summary>
    /// Whether there is a move to take back, and so whether the button is drawn. Unaffected
    /// by a stack being held: a press mid-drag belongs to the drag and never reaches the
    /// button anyway, and blinking the button out for the duration is both a flicker and a
    /// repaint of a corner of the board that had no reason to change.
    /// </summary>
    public bool CanUndo => Board.CanUndo && !fastForward.IsRunning;

    /// <summary>
    /// Whether the celebration still has the board. True from the moment the last card lands
    /// until the last one has finished falling — including the beat in between, while the
    /// winning move is still in the air and the cascade has not been thrown yet. The panel
    /// hides on this, and that gap is the difference between the panel waiting its turn and
    /// it blinking on for a few frames before the cards come down.
    /// </summary>
    public bool Celebrating =>
        cascade.IsRunning || (State == GameState.Won && celebratedDeal != Board.DealId);

    /// <summary>
    /// Whether this is a moment worth asking the player their name. A win is what asks, not a
    /// deal: the first deal interrupts someone who has not yet decided they care. Whether the
    /// asking has already happened is the host's business, since it owns the dialog.
    /// </summary>
    public bool WantsNickname =>
        State == GameState.Won && string.IsNullOrWhiteSpace(Score.Nickname);

    /// <summary>
    /// The pile the pointer could pick up from right now, or null if it could not pick up
    /// anything. Not the same as what the pointer is over: while the board is playing itself
    /// out nothing is grabbable, however the pointer is resting. <see cref="Controls"/> keeps
    /// the plain hover; the fast-forward is what this class knows and it does not.
    /// </summary>
    public Location? GrabbablePile => fastForward.IsRunning ? null : controls.HoverPile;

    /// <summary>Index of the lowest card that would come up from
    /// <see cref="GrabbablePile"/>, or -1 when there is no such pile.</summary>
    public int GrabbableIndex => GrabbablePile == null ? -1 : controls.HoverIndex;

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

    /// <summary>Whether the draw-time overlay is shown. Toggled with F, and saved with the
    /// score.</summary>
    public bool ShowStats => Score.ShowStats;

    /// <summary>
    /// Noises the board has asked for since the host last drained them. Nothing here makes a
    /// sound; the host plays them and calls <see cref="ClearSounds"/>.
    /// </summary>
    public IReadOnlyList<Sound> Sounds => Board.Sounds;

    /// <summary>Whether the board is silent. Toggled with M, and saved with the score.</summary>
    public bool Muted => Score.Muted;

    /// <summary>How the search on the current position is going.</summary>
    public SolveResult Analysis => analyzer.Result;

    /// <summary>Positions the search has examined on the current board.</summary>
    public int AnalysisNodes => analyzer.Nodes;

    /// <summary>
    /// The index from which a pile must not be drawn, because the cards above it are still
    /// in the air on their way to it.
    /// </summary>
    public int HiddenFrom(Location loc) => animator.HiddenFrom(loc);

    public void ClearSounds() => Board.ClearSounds();

    /// <summary>Called by the host once the current picture has been drawn.</summary>
    public void MarkClean()
    {
        NeedsRedraw = false;
        Board.ClearDirty();
    }

    public void Resize(double width, double height) => Guarded(() =>
    {
        Layout.Resize(width, height);

        // Every flight was aimed at a slot that has just moved. They are half a second long
        // at most, so there is nothing worth re-aiming — the board simply catches up.
        animator.Clear();
        cascade.Stop();
    });

    /// <summary>
    /// Called once per animation frame, before the drawer runs. This is where the fast-forward
    /// takes its step and the search gets its slice: the board is idle between moves anyway —
    /// the frame loop is already running and drawing nothing — so proving a deal dead costs
    /// the player nothing they can feel.
    /// </summary>
    public void Update(TimeSpan elapsed, PointerAt? pointer = null)
    {
        // Motion first, so a hover is settled before anything is drawn from it. It arrives
        // here rather than as its own call because it is frame data: the browser has already
        // coalesced it to one position for this frame.
        if (pointer is { } at)
        {
            OnPointerMove(at.X, at.Y);
        }

        AdvanceFastForward();

        // Anything the board did — this frame or since the last one — goes into the air
        // before it is moved on, so a move made a moment ago is already travelling by the
        // time this frame is drawn.
        animator.Capture(elapsed.TotalMilliseconds);

        if (animator.Tick(elapsed.TotalMilliseconds))
        {
            NeedsRedraw = true;
        }

        Celebrate();

        if (scores.Update())
        {
            NeedsRedraw = true;
        }

        if (analyzer.Update(paused: Drag != null || fastForward.IsRunning || animator.Busy || cascade.IsRunning))
        {
            NeedsRedraw = true;
        }
    }

    /// <param name="touch">True for a finger or a pen rather than a mouse.</param>
    public void OnPointerDown(double x, double y, bool touch = false) => Guarded(() =>
    {
        // A press during the celebration is asking for it to stop, and nothing else. The
        // press is swallowed so it does not also land on the board it uncovers.
        if (cascade.IsRunning)
        {
            cascade.Stop();
            controls.Consume();
            return;
        }

        // The panel is pressable exactly when it is painted — which is not simply "the game
        // is over": the celebration holds it back, and a button nobody can see must not take
        // a press meant for the board behind it.
        Do(controls.Down(x, y, touch, CanFastForward, CanUndo, State != GameState.Playing && !Celebrating));
    });

    public void OnPointerUp(double x, double y) => Guarded(() =>
    {
        // Read where the cards are before letting go of them. A drop is a move like any
        // other by the time the board sees it, and the board says only which pile it left —
        // so animating it would start the stack back at that pile, and the player would
        // watch what they just dragged across the board jump home and fly out again.
        if (Drag is { } held)
        {
            animator.ReleaseAt(
                held.From,
                held.Index,
                new Rect(
                    held.X - held.OffsetX,
                    held.Y - held.OffsetY,
                    Layout.CardWidth,
                    Layout.CardHeight));
        }

        controls.Up(x, y);
    });

    public void OnPointerCancel() => Guarded(controls.Cancel);

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

    /// <summary>Renames the player and reports the change so it gets saved.</summary>
    public void SetNickname(string nickname) => Guarded(() => scores.SetNickname(nickname));

    /// <inheritdoc cref="ScoreKeeper.ToJson"/>
    public string ScoreJson() => scores.ToJson();

    /// <inheritdoc cref="ScoreKeeper.Load"/>
    public bool LoadScore(string json) => scores.Load(json);

    /// <summary>Abandons the current game and deals a new one.</summary>
    public void Reset()
    {
        fastForward.Stop();
        cascade.Stop();
        animator.Clear();
        Guarded(Board.Reset);
    }

    /// <summary>
    /// Throws the foundations down the board once, on the frame a deal is won, and moves
    /// them on for as long as they are falling. Keyed to the deal rather than to the state,
    /// because a won board goes on reporting that it is won for as long as it is on screen.
    /// </summary>
    private void Celebrate()
    {
        if (State == GameState.Won && celebratedDeal != Board.DealId && !animator.Busy)
        {
            celebratedDeal = Board.DealId;
            cascade.Start();
        }

        if (cascade.Tick())
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
                scores.ToggleStats();
                break;

            case PlayerCommand.Undo:
                if (CanUndo)
                {
                    // Whatever was in the air was on its way somewhere that is no longer
                    // true. Undo is the one move that is better shown instantly.
                    animator.Clear();
                    Board.Undo();
                }

                break;

            case PlayerCommand.NewGame:
                Reset();
                break;
        }
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
