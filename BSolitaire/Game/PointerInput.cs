namespace BSolitaire.Game;

/// <summary>
/// A stack of cards held by the pointer. The cards are NOT removed from their pile while
/// dragging — the drawer skips them and paints them at the cursor instead — so abandoning
/// a drag needs no undo, and an illegal drop snaps back for free.
/// </summary>
public sealed class DragState
{
    public required Location From { get; init; }

    /// <summary>Index within the source pile of the lowest dragged card.</summary>
    public required int Index { get; init; }

    public required IReadOnlyList<Card> Cards { get; init; }

    /// <summary>Cursor position relative to the grabbed card's top-left corner.</summary>
    public required double OffsetX { get; init; }

    public required double OffsetY { get; init; }

    public double X { get; set; }

    public double Y { get; set; }

    /// <summary>False until the pointer has travelled far enough to count as a drag.</summary>
    public bool Active { get; set; }
}

/// <summary>
/// Turns pointer gestures into board moves. This type owns everything about *how* the
/// player is interacting — the drag in progress, the tapped selection — and nothing about
/// what the rules are (<see cref="Rules"/>), how a move is applied (<see cref="Board"/>),
/// or how any of it is drawn.
/// </summary>
public sealed class PointerInput
{
    /// <summary>How far the pointer must travel before a press counts as a drag, not a tap.</summary>
    private const double DragThreshold = 4;

    private readonly Board board;
    private readonly BoardLayout layout;

    private DragState? drag;
    private double downX;
    private double downY;

    // Tap-to-select: the second interaction mode, kept because it is the natural one on
    // touch. Only the location and size of the selection matter; the card itself is
    // whatever sits there when the move is made.
    private Location? selected;
    private int selectedCount;

    // What the pointer is resting on, expressed the same way a press would pick it up:
    // a pile and the index of the lowest card that would come with it. Storing the grab
    // point rather than the card under the cursor means the highlight shows what would
    // actually move, which is the thing worth telling the player.
    private Location? hoverPile;
    private int hoverIndex = -1;

    public PointerInput(Board board, BoardLayout layout)
    {
        this.board = board;
        this.layout = layout;
    }

    /// <summary>The stack currently being dragged, or null. Stays null until the press has
    /// travelled far enough, so a tap never flickers a card off its pile.</summary>
    public DragState? Drag => drag is { Active: true } ? drag : null;

    /// <summary>The pile the pointer is over and could grab from, or null.</summary>
    public Location? HoverPile => hoverPile;

    /// <summary>Index of the lowest card a press here would take. Meaningless when
    /// <see cref="HoverPile"/> is null.</summary>
    public int HoverIndex => hoverIndex;

    /// <summary>Pointer pressed at (x, y). Grabs a stack if one is under the pointer.</summary>
    public void Down(double x, double y)
    {
        downX = x;
        downY = y;
        drag = null;
        ClearHover();

        if (OverBanner(x, y))
        {
            return; // the panel is on top; nothing under it is grabbable
        }

        if (!layout.TryHitTest(board, x, y, out Location loc, out int indexInPile))
        {
            return;
        }

        if (!TryGrab(loc, indexInPile, out var cards))
        {
            return;
        }

        ClearSelection();

        var rect = layout.CardRect(loc, indexInPile);
        drag = new DragState
        {
            From = loc,
            Index = indexInPile,
            Cards = cards,
            OffsetX = x - rect.X,
            OffsetY = y - rect.Y,
            X = x,
            Y = y,
        };
    }

    /// <summary>
    /// Pointer moved to (x, y). Returns true if the picture changed, which is only while a
    /// stack is actually held — plain hovering must not force a redraw.
    /// </summary>
    public bool Move(double x, double y)
    {
        bool hoverChanged = UpdateHover(x, y);

        if (drag == null)
        {
            return hoverChanged;
        }

        drag.X = x;
        drag.Y = y;

        if (!drag.Active &&
            (Math.Abs(x - downX) > DragThreshold || Math.Abs(y - downY) > DragThreshold))
        {
            drag.Active = true;
        }

        return true;
    }

    /// <summary>
    /// Works out what the pointer is resting on. Returns true when that changed, which is
    /// the only time hovering is worth a redraw — the pointer crosses a card once and then
    /// spends dozens of frames inside it.
    /// </summary>
    private bool UpdateHover(double x, double y)
    {
        Location? pile = null;
        int index = -1;

        if (drag == null &&
            !OverBanner(x, y) &&
            layout.TryHitTest(board, x, y, out Location loc, out int indexInPile) &&
            TryGrab(loc, indexInPile, out _))
        {
            pile = loc;
            index = indexInPile;
        }

        if (pile == hoverPile && index == hoverIndex)
        {
            return false;
        }

        hoverPile = pile;
        hoverIndex = index;
        return true;
    }

    private void ClearHover()
    {
        hoverPile = null;
        hoverIndex = -1;
    }

    /// <summary>Pointer released at (x, y). Drops the held stack, or falls back to a tap.</summary>
    public void Up(double x, double y)
    {
        try
        {
            if (drag is { Active: true } held)
            {
                Drop(held, x, y);
            }
            else
            {
                // Never travelled far enough to be a drag. Replay it from where the press
                // started, so a few pixels of drift don't retarget the tap.
                Tap(downX, downY);
            }
        }
        finally
        {
            drag = null;
        }
    }

    /// <summary>
    /// Double-click at (x, y): send the card under the pointer to its foundation. A shortcut
    /// only — it plays exactly the move a drag there would, so a card with no legal foundation
    /// does nothing at all. Returns true if the board changed.
    /// </summary>
    public bool DoubleClick(double x, double y)
    {
        drag = null;

        if (OverBanner(x, y))
        {
            return false;
        }

        if (!layout.TryHitTest(board, x, y, out Location loc, out int indexInPile) ||
            indexInPile < 0)
        {
            return false;
        }

        // Only a single exposed card can be founded — never a buried one, and never a stack,
        // since foundations take one card at a time. A card already home is not going
        // anywhere either.
        var pile = board.Pile(loc);
        if (loc.Kind == PileKind.Foundation || indexInPile != pile.Count - 1 ||
            !Rules.CanLift(board, loc, indexInPile))
        {
            return false;
        }

        var dest = board.FoundationFor(pile[indexInPile]);
        if (dest == null)
        {
            return false;
        }

        ClearSelection();
        return board.MakeMove(new Move(loc, dest.Value, 1));
    }

    /// <summary>Pointer left the board or was cancelled mid-drag. The cards never left their
    /// pile, so abandoning the drag is all that's needed.</summary>
    public void Cancel()
    {
        drag = null;
    }

    /// <summary>
    /// Decides what a press picks up. Whether the cards can leave the pile is
    /// <see cref="Rules.CanLift"/>'s question; all this adds is handing back the cards it
    /// approved. Whether the resulting move is legal is asked separately, on drop.
    /// </summary>
    private bool TryGrab(Location loc, int indexInPile, out IReadOnlyList<Card> cards)
    {
        cards = Array.Empty<Card>();

        if (!Rules.CanLift(board, loc, indexInPile))
        {
            return false;
        }

        var pile = board.Pile(loc);
        cards = pile.GetRange(indexInPile, pile.Count - indexInPile);
        return true;
    }

    private void Drop(DragState held, double x, double y)
    {
        // Probe from the centre of the dragged card rather than the cursor: the cursor sits
        // wherever the card was grabbed, which may be a far corner.
        double probeX = x - held.OffsetX + layout.CardWidth / 2;
        double probeY = y - held.OffsetY + layout.CardHeight / 2;

        if (!layout.TryHitPile(board, probeX, probeY, out Location dest) || dest == held.From)
        {
            return;
        }

        // MakeMove refuses anything illegal, and the cards were never removed from their
        // pile, so a refusal is the snap-back.
        board.MakeMove(new Move(held.From, dest, held.Cards.Count));
    }

    /// <summary>Whether a point falls on the game-over panel, which is only there once the
    /// game has ended.</summary>
    private bool OverBanner(double x, double y) =>
        board.State != GameState.Playing && layout.Banner.Contains(x, y);

    private void Tap(double x, double y)
    {
        if (OverBanner(x, y))
        {
            if (layout.NewGameButton.Contains(x, y))
            {
                board.Reset();
                ClearSelection();
            }

            return;
        }

        if (!layout.TryHitTest(board, x, y, out Location loc, out int indexInPile))
        {
            ClearSelection(); // tapping bare felt cancels a selection
            return;
        }

        if (loc.Kind == PileKind.FaceDown)
        {
            if (!board.DealFromStock())
            {
                board.RecycleWaste();
            }

            ClearSelection();
            return;
        }

        if (selected == null)
        {
            Select(loc, indexInPile);
            return;
        }

        // With something selected, a tap on any playable pile is an attempt to move it
        // there. The stock and waste are not destinations.
        if (loc.Kind != PileKind.FaceUp && loc.Kind != PileKind.FaceDown)
        {
            board.MakeMove(new Move(selected.Value, loc, selectedCount));
            ClearSelection();
        }
    }

    /// <summary>
    /// Marks what a tap picked out, so the next tap can try to move it. Selecting asks the
    /// same question a press does — a tap and a drag pick up exactly the same cards, and
    /// letting them disagree is how a face-down run became selectable and, on the tap that
    /// followed, movable.
    /// </summary>
    private void Select(Location loc, int indexInPile)
    {
        if (!Rules.CanLift(board, loc, indexInPile))
        {
            return;
        }

        selected = loc;
        selectedCount = board.Pile(loc).Count - indexInPile;
    }

    private void ClearSelection()
    {
        selected = null;
        selectedCount = 0;
    }
}
