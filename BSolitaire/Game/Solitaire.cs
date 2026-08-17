namespace BSolitaire.Game;

/// <summary>
/// The whole game lives here. It knows nothing about Blazor, canvas, or JS —
/// it is driven entirely by the four methods below, so it stays unit-testable.
/// </summary>
public class Solitaire
{
    public Board Board { get; } = new();
    public BoardLayout Layout { get; } = new BoardLayout(800, 600, 80, 120);
    public string? Error { get; private set; } = null;

    private Card? selectedCard = null;
    private Location? selectedCardLocation = null;
    private int selectedCardCount = 0;

    private DragState? drag = null;
    private double pointerDownX;
    private double pointerDownY;

    /// <summary>How far the pointer must travel before a press counts as a drag rather than a tap.</summary>
    private const double DragThreshold = 4;

    /// <summary>
    /// A stack of cards held by the pointer. The cards are NOT removed from their pile
    /// while dragging — the drawer skips them and paints them at the cursor instead — so
    /// abandoning a drag needs no undo, and an illegal drop snaps back for free.
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

        /// <summary>False until the pointer has moved past <see cref="DragThreshold"/>.</summary>
        public bool Active { get; set; }
    }

    /// <summary>The stack currently being dragged, or null. Stays null until the press
    /// has travelled far enough to be a drag, so taps never flicker a card off its pile.</summary>
    public DragState? Drag => drag is { Active: true } ? drag : null;

    public Solitaire()
    {
    }

    /// <summary>Time since the game started. Set by <see cref="Update"/>.</summary>
    public TimeSpan Elapsed { get; private set; }

    public void Resize(double width, double height)
    {
        Layout.Resize(width, height);
    }

    /// <summary>Called once per animation frame, before the drawer runs.</summary>
    public void Update(TimeSpan elapsed)
    {
        Elapsed = elapsed;
    }

    /// <summary>A click/tap at (x, y) in CSS pixels, origin at the top-left of the board.</summary>
    public void OnClick(double x, double y)
    {
        try
        {
            if (Layout.TryHitTest(Board, x, y, out Location loc, out int indexInPile))
            {
                //Error = $"{loc.Kind}[{loc.PileIndex}] index {indexInPile}";
                if (loc.Kind == PileKind.FaceDown)
                {   
                    if (Board.FaceDownPile.Count > 0)
                    {
                        // move the top card from facedown to faceup
                        var dest = new Location(PileKind.FaceUp, 0);
                        Board.MakeMove(new Move(loc, dest, 1));

                        selectedCard = null;
                        selectedCardLocation = null;
                        selectedCardCount = 0;
                    }
                    else
                    {
                        // copy all cards from FaceUp to FaceDown and flip them over
                        // don't use Board.MakeMove
                        Board.FaceDownPile.AddRange(Board.FaceUpPile);
                        Board.FaceDownPile.Reverse();
                        Board.FaceUpPile.Clear();
                        // flip over the cards 
                        foreach (var card in Board.FaceDownPile)
                        {
                            card.Flip();
                        }
                        
                        selectedCard = null;
                        selectedCardLocation = null;
                        selectedCardCount = 0;
                    }
                }
                else if (selectedCard == null && loc.Kind == PileKind.FaceUp && indexInPile == Board.FaceUpPile.Count - 1)
                {
                    selectedCard = Board.FaceUpPile[indexInPile];
                    selectedCardLocation = loc;
                    selectedCardCount = 1;  
                }
                else if (selectedCard == null && loc.Kind == PileKind.Tableau)
                {
                    var pile = Board.TableauPiles[loc.PileIndex];
                    selectedCard = pile[indexInPile];
                    selectedCardLocation = loc;
                    selectedCardCount = pile.Count - indexInPile;
                }
                else if (selectedCard != null && loc.Kind != PileKind.FaceUp && loc.Kind != PileKind.FaceDown)
                {
                    // try to move the selected card to the clicked location
                    var dest = loc;
                    var move = new Move(selectedCardLocation!.Value, dest, selectedCardCount);

                    Board.MakeMove(move);
                    selectedCard = null;
                    selectedCardLocation = null;
                    selectedCardCount = 0;
                }
            }
            else
            {
                // click on empty space, deselect any selected card
                selectedCard = null;
                selectedCardLocation = null;
                selectedCardCount = 0;
            }
            
            Error = null;
        }
        catch (Exception ex)
        {
            Error = ex.ToString();
        }
    }

    /// <summary>Pointer pressed at (x, y). Grabs a stack if one is under the pointer.</summary>
    public void OnPointerDown(double x, double y)
    {
        try
        {
            pointerDownX = x;
            pointerDownY = y;
            drag = null;

            if (!Layout.TryHitTest(Board, x, y, out Location loc, out int indexInPile))
            {
                return;
            }

            if (!TryGrab(loc, indexInPile, out var cards))
            {
                return;
            }

            ClearSelection();

            var rect = Layout.CardRect(loc, indexInPile);
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

            Error = null;
        }
        catch (Exception ex)
        {
            Error = ex.ToString();
        }
    }

    /// <summary>Pointer moved to (x, y). Promotes a press to a drag once it clears the threshold.</summary>
    public void OnPointerMove(double x, double y)
    {
        if (drag == null)
        {
            return;
        }

        drag.X = x;
        drag.Y = y;

        if (!drag.Active &&
            (Math.Abs(x - pointerDownX) > DragThreshold || Math.Abs(y - pointerDownY) > DragThreshold))
        {
            drag.Active = true;
        }
    }

    /// <summary>Pointer released at (x, y). Drops the dragged stack, or falls back to a tap.</summary>
    public void OnPointerUp(double x, double y)
    {
        try
        {
            if (drag is { Active: true } held)
            {
                Drop(held, x, y);
            }
            else
            {
                // Never travelled far enough to be a drag, so treat it as a tap and let
                // the click path handle dealing, recycling, and click-to-select.
                OnClick(pointerDownX, pointerDownY);
            }
        }
        catch (Exception ex)
        {
            Error = ex.ToString();
        }
        finally
        {
            drag = null;
        }
    }

    /// <summary>Pointer left the board or was cancelled mid-drag. The cards never left
    /// their pile, so abandoning the drag is all that's needed.</summary>
    public void OnPointerCancel()
    {
        drag = null;
    }

    /// <summary>
    /// Decides what a press at this location picks up. Only face-up-ness and pile
    /// position are considered here — whether the resulting move is legal is
    /// <see cref="Rules"/>' business, checked on drop.
    /// </summary>
    private bool TryGrab(Location loc, int indexInPile, out IReadOnlyList<Card> cards)
    {
        cards = Array.Empty<Card>();

        if (indexInPile < 0)
        {
            return false; // empty slot
        }

        switch (loc.Kind)
        {
            case PileKind.Tableau:
                var tableau = Board.TableauPiles[loc.PileIndex];
                for (int i = indexInPile; i < tableau.Count; i++)
                {
                    if (!tableau[i].IsFaceUp)
                    {
                        return false;
                    }
                }

                cards = tableau.GetRange(indexInPile, tableau.Count - indexInPile);
                return true;

            case PileKind.FaceUp:
                if (indexInPile != Board.FaceUpPile.Count - 1)
                {
                    return false; // only the top of the waste is playable
                }

                cards = new[] { Board.FaceUpPile[indexInPile] };
                return true;

            case PileKind.Foundation:
                var foundation = Board.FoundationPiles[loc.PileIndex];
                if (indexInPile != foundation.Count - 1)
                {
                    return false;
                }

                cards = new[] { foundation[indexInPile] };
                return true;

            default:
                return false; // the stock is dealt from, never dragged
        }
    }

    private void Drop(DragState held, double x, double y)
    {
        // Probe from the centre of the dragged card rather than the cursor: the cursor
        // sits wherever the card was grabbed, which may be a far corner.
        double probeX = x - held.OffsetX + Layout.CardWidth / 2;
        double probeY = y - held.OffsetY + Layout.CardHeight / 2;

        if (!Layout.TryHitPile(Board, probeX, probeY, out Location dest) || dest == held.From)
        {
            return;
        }

        // MakeMove refuses anything illegal, and the cards were never removed from their
        // pile, so a refusal is the snap-back.
        Board.MakeMove(new Move(held.From, dest, held.Cards.Count));
    }

    private void ClearSelection()
    {
        selectedCard = null;
        selectedCardLocation = null;
        selectedCardCount = 0;
    }

    /// <summary>A key press, using KeyboardEvent.code values ("KeyR", "Space", "ArrowLeft"...).</summary>
    public void OnKeyDown(string code)
    {
    }

    public void Reset()
    {
    }
}
