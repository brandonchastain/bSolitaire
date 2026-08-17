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

    /// <summary>A key press, using KeyboardEvent.code values ("KeyR", "Space", "ArrowLeft"...).</summary>
    public void OnKeyDown(string code)
    {
    }

    public void Reset()
    {
    }
}
