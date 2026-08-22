namespace BSolitaire.Game;

/// <summary>
/// A stack of cards held by the pointer. The cards are NOT removed from their pile while
/// dragging — the drawer skips them and paints them at the cursor instead — so abandoning
/// a drag needs no undo, and an illegal drop snaps back for free.
/// </summary>
internal sealed class DragState
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
internal sealed class PointerInput
{
    /// <summary>How far the pointer must travel before a press counts as a drag, not a tap.</summary>
    private const double DragThreshold = 4;

    /// <summary>
    /// How far above a fingertip the held stack rides, as a fraction of a card. A mouse
    /// pointer is a few pixels of arrow and the card can sit under it; a finger covers the
    /// card it picked up, and a card you cannot see is one you cannot aim. Folded into the
    /// grab offset rather than added at draw time, so the drop lands where the card looks
    /// like it will — the alternative is a card that reads one column and drops in another.
    ///
    /// A quarter of a card rather than a half. The lift is paid for twice: once when the card
    /// jumps out from under the finger that grabbed it, and again on every drop, because
    /// placing the card on a pile means carrying the finger that far below it. Half a card
    /// clears a fingertip with room to spare and made every move that much longer; a quarter
    /// still shows the card and asks for less travel.
    /// </summary>
    private const double TouchLift = 0.25;

    /// <summary>The only two kinds of pile anything can be dropped on.</summary>
    private static readonly PileKind[] DropKinds = [PileKind.Tableau, PileKind.Foundation];

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

    // Where the held stack would be accepted, worked out once when it is picked up. A touch
    // screen has no hover, so this is the only way the board can answer "where does this
    // go?" before the player commits to an answer of their own.
    private readonly List<Location> dropTargets = new();

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

    /// <summary>Every pile that would accept the stack being dragged. Empty unless a stack
    /// is actually held.</summary>
    public IReadOnlyList<Location> DropTargets =>
        drag is { Active: true } ? dropTargets : Array.Empty<Location>();

    /// <summary>
    /// Pointer pressed at (x, y). Grabs a stack if one is under the pointer.
    /// </summary>
    /// <param name="touch">
    /// True for a finger or a pen rather than a mouse. The only thing it changes is where the
    /// held cards ride relative to the pointer — see <see cref="TouchLift"/>.
    /// </param>
    public void Down(double x, double y, bool touch = false)
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

        // The selection deliberately survives this. A press is also how a tap starts, and
        // the second tap of a two-tap move lands on a pile that can usually be grabbed from
        // — clearing here left that move unreachable for every pile holding a card.
        var rect = layout.CardRect(loc, indexInPile);
        drag = new DragState
        {
            From = loc,
            Index = indexInPile,
            Cards = cards,
            OffsetX = x - rect.X,
            OffsetY = y - rect.Y + (touch ? layout.CardHeight * TouchLift : 0),
            X = x,
            Y = y,
        };

        FindDropTargets(loc, cards.Count);
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
            dropTargets.Clear();
        }
    }

    /// <summary>Pointer left the board or was cancelled mid-drag. The cards never left their
    /// pile, so abandoning the drag is all that's needed.</summary>
    public void Cancel()
    {
        drag = null;
        dropTargets.Clear();
        ClearSelection();
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
        var lifted = new List<Card>(pile.Count - indexInPile);

        for (int i = indexInPile; i < pile.Count; i++)
        {
            lifted.Add(pile[i]);
        }

        cards = lifted;
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
        ClearSelection();
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

        // Tapping the selection again is the shortcut for sending a card home. It is the
        // double-click move, spelt as two separate taps: mobile browsers synthesise dblclick
        // unreliably under a board that has claimed the touch gesture for dragging, and a
        // player who has already tapped a card once is holding exactly the right idea of
        // what a second tap on it should do. Falls back to putting the card down.
        if (loc == selected)
        {
            if (!SendHome(loc))
            {
                ClearSelection();
            }

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
    /// Plays the top card of a pile to its foundation, if it has one. The same move a drag
    /// there would make — <see cref="Rules"/> still has the last word — so a card with no
    /// home does nothing and the caller can treat that as "the shortcut did not apply".
    /// </summary>
    private bool SendHome(Location loc)
    {
        var pile = board.Pile(loc);

        if (loc.Kind == PileKind.Foundation || selectedCount != 1 || pile.Count == 0)
        {
            return false;
        }

        if (board.FoundationFor(pile[^1]) is not { } dest)
        {
            return false;
        }

        ClearSelection();
        return board.MakeMove(new Move(loc, dest, 1));
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

    /// <summary>
    /// Asks the rules where this stack could go, once, at the moment it leaves its pile.
    /// Neither the stack nor the rest of the board can change while it is held, so the answer
    /// cannot go stale — and the same <see cref="Rules.IsLegal"/> that will judge the drop is
    /// what answers, so the board never offers a target that would then refuse the card.
    /// </summary>
    private void FindDropTargets(Location from, int count)
    {
        dropTargets.Clear();

        foreach (var kind in DropKinds)
        {
            int pileCount = board.PileCountOf(kind);
            for (int pileIndex = 0; pileIndex < pileCount; pileIndex++)
            {
                var to = new Location(kind, pileIndex);

                if (to != from && Rules.IsLegal(board, new Move(from, to, count)))
                {
                    dropTargets.Add(to);
                }
            }
        }
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
}
