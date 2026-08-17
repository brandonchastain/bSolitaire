namespace BSolitaire.Game;

/// <summary>
/// Holds the cards, piles, and state of the Solitaire game.
/// </summary>
public class Board
{
    /// <summary>Every pile kind. Cached because Enum.GetValues allocates, and the
    /// draw loop and hit test both walk this on every frame and every pointer event.</summary>
    public static readonly PileKind[] AllKinds = Enum.GetValues<PileKind>();
    private static readonly Dealer Dealer = new();
    private const int NumFoundationPiles = 4;
    private const int NumTableauPiles = 7;


    public Board()
    {
        for (int i = 0; i < NumFoundationPiles; i++)
        {
            FoundationPiles[i] = new List<Card>();
        }

        for (int i = 0; i < NumTableauPiles; i++)
        {
            TableauPiles[i] = new List<Card>();
        }

        Dealer.Deal(FaceDownPile, TableauPiles);
    }

    public List<Card> FaceDownPile { get; } = new();
    public List<Card> FaceUpPile { get; } = new();
    public List<Card>[] FoundationPiles { get; } = new List<Card>[NumFoundationPiles]; // Index 0: Clubs, 1: Diamonds, 2: Hearts, 3: Spades
    public List<Card>[] TableauPiles { get; } = new List<Card>[NumTableauPiles]; // Index 0-6: Tableau piles

    /// <summary>The cards in a pile.</summary>
    public List<Card> Pile(Location loc) => loc.Kind switch
    {
        PileKind.FaceDown => FaceDownPile,
        PileKind.FaceUp => FaceUpPile,
        PileKind.Foundation => FoundationPiles[loc.PileIndex],
        PileKind.Tableau => TableauPiles[loc.PileIndex],
        _ => throw new ArgumentOutOfRangeException(nameof(loc), loc.Kind, null)
    };

    /// <summary>How many piles of this kind the board has.</summary>
    public int PileCountOf(PileKind kind) => kind switch
    {
        PileKind.Foundation => NumFoundationPiles,
        PileKind.Tableau => NumTableauPiles,
        _ => 1
    };

    public bool MakeMove(Move move)
    {
        var from = Pile(move.From);
        var to = Pile(move.To);

        if (Rules.IsLegal(this, move))
        {
            // move the cards (could be multiple) from from to to.
            // do not reverse the order of cards, preserve order.
            var cardsToMove = from.GetRange(from.Count - move.Count, move.Count);
            from.RemoveRange(from.Count - move.Count, move.Count);
            to.AddRange(cardsToMove);
        }
        else
        {
            return false;
        }

        if (move.From.Kind == PileKind.FaceDown && move.To.Kind == PileKind.FaceUp)
        {
            var topCard = FaceUpPile[^1];
            topCard.Flip();
        }
        else if (move.From.Kind == PileKind.Tableau && TableauPiles[move.From.PileIndex].Count > 0)
        {
            var topCard = TableauPiles[move.From.PileIndex][^1];
            if (!topCard.IsFaceUp)
            {
                topCard.Flip();
            }
        }

        return true;
    }

    /// <summary>Turns the top stock card face up onto the waste.</summary>
    public bool DealFromStock()
    {
        if (FaceDownPile.Count == 0)
        {
            return false;
        }

        return MakeMove(new Move(
            new Location(PileKind.FaceDown, 0),
            new Location(PileKind.FaceUp, 0),
            1));
    }

    /// <summary>
    /// Turns the whole waste back over to form a fresh stock, so the cards come off again
    /// in the order they went on. Not expressed as a Move: it touches every card at once
    /// and there is no legality question for Rules to answer.
    /// </summary>
    public bool RecycleWaste()
    {
        if (FaceDownPile.Count > 0 || FaceUpPile.Count == 0)
        {
            return false;
        }

        for (int i = FaceUpPile.Count - 1; i >= 0; i--)
        {
            var card = FaceUpPile[i];
            card.Flip();
            FaceDownPile.Add(card);
        }

        FaceUpPile.Clear();
        return true;
    }
}