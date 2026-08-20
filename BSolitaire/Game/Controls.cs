namespace BSolitaire.Game;

/// <summary>
/// Something the player asked for that is not a move. Moves go straight to the board through
/// <see cref="PointerInput"/>; these are the handful of things a key or an on-felt button can
/// ask the session to do, and <see cref="Solitaire"/> is what carries them out.
/// </summary>
public enum PlayerCommand
{
    None,
    FastForward,
    Undo,
    ToggleMute,
    ToggleStats,
    NewGame
}

/// <summary>
/// Everything the player can touch, and the one place that decides what a press or a keystroke
/// means. It owns the gestures — <see cref="PointerInput"/> turns them into moves — and the
/// buttons painted on the felt, which have to take a press before the piles underneath get a
/// look at it.
///
/// It decides nothing about the game: a press over the fast-forward button yields
/// <see cref="PlayerCommand.FastForward"/> and stops there. Whether that is allowed, and what
/// it does, belongs to the session.
/// </summary>
public sealed class Controls
{
    private readonly PointerInput input;
    private readonly BoardLayout layout;

    private bool pressConsumed;

    public Controls(Board board, BoardLayout layout)
    {
        this.layout = layout;
        input = new PointerInput(board, layout);
    }

    /// <summary>The stack currently held by the pointer, or null.</summary>
    public DragState? Drag => input.Drag;

    /// <summary>The pile the pointer is resting on and could pick up from, or null.</summary>
    public Location? HoverPile => input.HoverPile;

    /// <summary>Every pile that would accept the stack being dragged.</summary>
    public IReadOnlyList<Location> DropTargets => input.DropTargets;

    /// <summary>Index of the lowest card the pointer would pick up from
    /// <see cref="HoverPile"/>.</summary>
    public int HoverIndex => input.HoverIndex;

    /// <param name="touch">True for a finger or a pen rather than a mouse.</param>
    /// <param name="fastForwardOffered">
    /// Whether the play-it-out button is on screen. The button is drawn only when the offer
    /// stands, and a button that is not drawn must not be pressable.
    /// </param>
    /// <param name="undoOffered">Whether there is a move to take back, and so whether the
    /// undo button is on screen at all. Same rule as above.</param>
    public PlayerCommand Down(double x, double y, bool touch, bool fastForwardOffered, bool undoOffered)
    {
        // The buttons sit over the felt, so they have to take the press before the piles do.
        // The release that follows has to be swallowed too: PointerInput never saw the press,
        // and left to itself it would treat the release as a tap at wherever the last real
        // press happened to be.
        if (fastForwardOffered && layout.FastForwardButton.Contains(x, y))
        {
            pressConsumed = true;
            return PlayerCommand.FastForward;
        }

        if (undoOffered && layout.UndoButton.Contains(x, y))
        {
            pressConsumed = true;
            return PlayerCommand.Undo;
        }

        // The mute toggle sits in the gap column, where no pile ever is, so it only has to
        // take the press ahead of the felt. Same swallowed release as the button above.
        if (layout.MuteButton.Contains(x, y))
        {
            pressConsumed = true;
            return PlayerCommand.ToggleMute;
        }

        pressConsumed = false;
        input.Down(x, y, touch);
        return PlayerCommand.None;
    }

    public void Up(double x, double y)
    {
        if (pressConsumed)
        {
            pressConsumed = false;
            return;
        }

        input.Up(x, y);
    }

    /// <summary>
    /// Swallows the press that is already in progress, and the release that will follow it.
    /// The session needs this for the one press that is neither a move nor a button: the tap
    /// that cuts the winning cascade short, which must not also land on the board underneath.
    /// </summary>
    public void Consume() => pressConsumed = true;

    public void Cancel()
    {
        pressConsumed = false;
        input.Cancel();
    }

    /// <summary>Double-click at (x, y): a shortcut for sending a card to its foundation.</summary>
    public void DoubleClick(double x, double y) => input.DoubleClick(x, y);

    /// <summary>Reports whether the picture changed — hovering must not defeat the redraw
    /// check, so only a held stack counts.</summary>
    public bool Move(double x, double y) => input.Move(x, y);

    /// <summary>
    /// A key press, using KeyboardEvent.code values ("KeyR", "Space", "ArrowLeft"...).
    /// Keys the board has no use for are simply not ours.
    /// </summary>
    public PlayerCommand Key(string code) => code switch
    {
        "KeyF" => PlayerCommand.ToggleStats,
        "KeyR" => PlayerCommand.NewGame,
        "KeyM" => PlayerCommand.ToggleMute,
        "KeyZ" or "Backspace" => PlayerCommand.Undo,
        "Space" or "Enter" => PlayerCommand.FastForward,
        _ => PlayerCommand.None
    };
}
