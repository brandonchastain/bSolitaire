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

    /// <summary>Ace through king: a full foundation.</summary>
    private const int FoundationSize = 13;


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

        Reset();
    }

    public List<Card> FaceDownPile { get; } = new();
    public List<Card> FaceUpPile { get; } = new();
    /// <summary>The four foundations. No pile is reserved for a suit — whichever ace lands on a
    /// pile first claims it for the rest of the game.</summary>
    public List<Card>[] FoundationPiles { get; } = new List<Card>[NumFoundationPiles];
    public List<Card>[] TableauPiles { get; } = new List<Card>[NumTableauPiles]; // Index 0-6: Tableau piles

    /// <summary>
    /// Whether the game is still going, and if not, how it ended. Recomputed after every
    /// move rather than on demand: the answer only changes when the board does, and both the
    /// drawer and the input path ask for it.
    /// </summary>
    public GameState State { get; private set; }

    /// <summary>
    /// Bumped every time the position changes. Lets anything doing slow work about a position
    /// — the solver — notice that the board moved out from under it.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>
    /// Records that a search proved this position cannot be won. Only the search can know
    /// this, so it is told to the board rather than worked out by it. Ignored once a game has
    /// ended, and undone by the next move.
    /// </summary>
    public void MarkUnwinnable()
    {
        if (State == GameState.Playing)
        {
            State = GameState.Unwinnable;
        }
    }

    /// <summary>Shuffles a new deck and deals it. The old game is simply dropped.</summary>
    public void Reset()
    {
        FaceDownPile.Clear();
        FaceUpPile.Clear();

        foreach (var pile in FoundationPiles)
        {
            pile.Clear();
        }

        foreach (var pile in TableauPiles)
        {
            pile.Clear();
        }

        Dealer.Deal(FaceDownPile, TableauPiles);
        State = GameState.Playing;
        Version++;
    }

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

    /// <summary>
    /// The foundation <paramref name="card"/> belongs on, or null if it has no home yet.
    /// A card that continues a started pile goes there; an ace prefers the pile matching its
    /// suit — the soft default the comment used to claim — and settles for any empty one, so
    /// the first four aces still end up on four separate piles whatever order they arrive in.
    /// </summary>
    public Location? FoundationFor(Card card)
    {
        for (int i = 0; i < NumFoundationPiles; i++)
        {
            var pile = FoundationPiles[i];
            if (pile.Count > 0 && Rules.CanFound(card, pile[^1]))
            {
                return new Location(PileKind.Foundation, i);
            }
        }

        if (!Rules.CanFound(card, null))
        {
            return null;
        }

        int preferred = (int)card.Suit;
        if (FoundationPiles[preferred].Count == 0)
        {
            return new Location(PileKind.Foundation, preferred);
        }

        for (int i = 0; i < NumFoundationPiles; i++)
        {
            if (FoundationPiles[i].Count == 0)
            {
                return new Location(PileKind.Foundation, i);
            }
        }

        return null;
    }

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

        RefreshState();
        return true;
    }

    /// <summary>
    /// Works out whether the game is over. Only called after a move, since nothing else can
    /// end a game — the board is otherwise idle between pointer events.
    /// </summary>
    private void RefreshState()
    {
        Version++;

        foreach (var pile in FoundationPiles)
        {
            if (pile.Count < FoundationSize)
            {
                State = Rules.IsStuck(this) ? GameState.Stuck : GameState.Playing;
                return;
            }
        }

        State = GameState.Won;
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
        RefreshState();
        return true;
    }
}