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

    public Solitaire()
    {
        Board = new Board();
        // Placeholder size; the host calls Resize with the real viewport on first render.
        Layout = new BoardLayout(800, 600);
        input = new PointerInput(Board, Layout);
    }

    public Board Board { get; }

    public BoardLayout Layout { get; }

    /// <summary>The stack currently held by the pointer, or null.</summary>
    public DragState? Drag => input.Drag;

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

    /// <summary>Whether the draw-time overlay is shown. Toggled with F.</summary>
    public bool ShowStats { get; private set; }

    /// <summary>Time since the game started. Set by <see cref="Update"/>.</summary>
    public TimeSpan Elapsed { get; private set; }

    /// <summary>Called by the host once the current picture has been drawn.</summary>
    public void MarkClean() => NeedsRedraw = false;

    public void Resize(double width, double height) => Guarded(() => Layout.Resize(width, height));

    /// <summary>Called once per animation frame, before the drawer runs.</summary>
    public void Update(TimeSpan elapsed) => Elapsed = elapsed;

    public void OnPointerDown(double x, double y) => Guarded(() => input.Down(x, y));

    public void OnPointerUp(double x, double y) => Guarded(() => input.Up(x, y));

    public void OnPointerCancel() => Guarded(input.Cancel);

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
    }

    /// <summary>Abandons the current game and deals a new one.</summary>
    public void Reset() => Guarded(Board.Reset);

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
