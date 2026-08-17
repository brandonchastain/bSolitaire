namespace BSolitaire.Game;

/// <summary>
/// Holds the cards, piles, and state of the Solitaire game.
/// </summary>
public class Board
{
    private const int NumFoundationPiles = 4;
    private const int NumTableauPiles = 7;

    public List<Card> FaceDownPile { get; } = BuildDefaultDeck();
    public List<Card> FaceUpPile { get; } = new();
    public List<Card>[] FoundationPiles { get; } = new List<Card>[NumFoundationPiles]; // Index 0: Clubs, 1: Diamonds, 2: Hearts, 3: Spades
    public List<Card>[] TableauPiles { get; } = new List<Card>[NumTableauPiles]; // Index 0-6: Tableau piles

    /// <summary>Every pile kind. Cached because Enum.GetValues allocates, and the
    /// draw loop and hit test both walk this on every frame and every pointer event.</summary>
    public static readonly PileKind[] AllKinds = Enum.GetValues<PileKind>();

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

        this.Deal();
    }

    public bool MakeMove(Move move)
    {
        var from = move.From.Kind switch
        {
            PileKind.FaceDown => FaceDownPile,
            PileKind.FaceUp => FaceUpPile,
            PileKind.Foundation => FoundationPiles[move.From.PileIndex],
            PileKind.Tableau => TableauPiles[move.From.PileIndex],
            _ => throw new ArgumentOutOfRangeException(nameof(move.From.Kind), move.From.Kind, null)
        };

        var to = move.To.Kind switch
        {
            PileKind.FaceDown => FaceDownPile,
            PileKind.FaceUp => FaceUpPile,
            PileKind.Foundation => FoundationPiles[move.To.PileIndex],
            PileKind.Tableau => TableauPiles[move.To.PileIndex],
            _ => throw new ArgumentOutOfRangeException(nameof(move.To.Kind), move.To.Kind, null)
        };

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

    /// <summary>
    /// Deal cards from facedown into tableau piles. The first pile gets 1 card, the second gets 2, and so on, up to the seventh pile which gets 7 cards. The top card of each tableau pile is turned face up.
    /// Dealing must happen in order of tableau piles, one card at a time, from the top of the facedown pile.
    /// </summary>
    private void Deal()
    {
        // Deal left-to-right, adding one more card to each tableau pile than the previous one.
        // Each new card is taken from the top of the stock and placed on the current pile,
        // so the last card dealt to a pile is the one on top.
        for (int row = 0; row < NumTableauPiles; row++)
        {
            for (int pileIndex = row; pileIndex < NumTableauPiles; pileIndex++)
            {
                var card = FaceDownPile[^1];
                FaceDownPile.RemoveAt(FaceDownPile.Count - 1);
                TableauPiles[pileIndex].Add(card);

                if (pileIndex == row)
                {
                    card.Flip();
                }
            }
        }
    }

    private static List<Card> BuildDefaultDeck()
    {
        var deck = new List<Card>();
        foreach (Suit suit in Enum.GetValues<Suit>())
        {
            foreach (Rank rank in Enum.GetValues<Rank>())
            {
                deck.Add(new Card(suit, rank));
            }
        }

        // shuffle the deck
        var rng = new Random();
        int n = deck.Count;
        while (n > 1)
        {
            n--;
            int k = rng.Next(n + 1);
            (deck[k], deck[n]) = (deck[n], deck[k]);
        }

        return deck;
    }
}