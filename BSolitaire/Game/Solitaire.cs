namespace BSolitaire.Game;

/// <summary>
/// The whole game lives here. It knows nothing about Blazor, canvas, or JS —
/// it is driven entirely by the four methods below, so it stays unit-testable.
/// </summary>
public class Solitaire
{
    public Board Board { get; } = new();
    public BoardLayout Layout { get; } = new BoardLayout(800, 600, 80, 120);

    private Card? selectedCard = null;

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
        if (Layout.TryHitTest(Board, x, y, out Location loc, out int indexInPile))
        {
            if (loc.Kind == PileKind.FaceDown && Board.FaceDownPile.Count > 0)
            {
                // move the top card from facedown to faceup
                var dest = new Location(PileKind.FaceUp, -1);
                var move = new Move(loc, dest, 1);

                Board.MakeMove(new Move(loc, dest, 1));
            }
            else if (loc.Kind == PileKind.FaceUp && indexInPile == Board.FaceUpPile.Count - 1)
            {
                // select the top card of the faceup pile
                selectedCard = Board.FaceUpPile[indexInPile];
            }
            else if (selectedCard != null)
            {
                // try to move the selected card to the clicked location
                var dest = loc;
                var move = new Move(new Location(PileKind.FaceUp, -1), dest, 1);

                Board.MakeMove(move);
                selectedCard = null;
            }
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
