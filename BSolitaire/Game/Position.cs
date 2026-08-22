namespace BSolitaire.Game;

/// <summary>
/// Which cards are in which piles, in what order. A position does not know a king from a two:
/// legality belongs to <see cref="Rules"/>, and whether the game is over to <see cref="Board"/>.
///
/// The lists are private and handed out read-only, so cards change hands only through
/// <see cref="Take"/> and <see cref="Place"/>. That is what keeps the splice separable from the
/// bookkeeping in <see cref="Board.MakeMove"/> that has to accompany it.
/// </summary>
internal sealed class Position
{
    public const int FoundationCount = 4;
    public const int TableauCount = 7;

    /// <summary>Cached because Enum.GetValues allocates, and the draw loop and hit test both
    /// walk this on every frame and every pointer event.</summary>
    public static readonly PileKind[] AllKinds = Enum.GetValues<PileKind>();

    private readonly List<Card> faceDown = new();
    private readonly List<Card> faceUp = new();
    private readonly List<Card>[] foundations = new List<Card>[FoundationCount];
    private readonly List<Card>[] tableaus = new List<Card>[TableauCount];

    public Position()
    {
        for (int i = 0; i < FoundationCount; i++)
        {
            foundations[i] = new List<Card>();
        }

        for (int i = 0; i < TableauCount; i++)
        {
            tableaus[i] = new List<Card>();
        }
    }

    public IReadOnlyList<Card> FaceDownPile => faceDown;

    public IReadOnlyList<Card> FaceUpPile => faceUp;

    /// <summary>No foundation is reserved for a suit — whichever ace lands on one claims it.</summary>
    public IReadOnlyList<IReadOnlyList<Card>> FoundationPiles => foundations;

    public IReadOnlyList<IReadOnlyList<Card>> TableauPiles => tableaus;

    public int FoundationTotal
    {
        get
        {
            int total = 0;

            foreach (var pile in foundations)
            {
                total += pile.Count;
            }

            return total;
        }
    }

    public IReadOnlyList<Card> Pile(Location loc) => Mutable(loc);

    public int PileCountOf(PileKind kind) => kind switch
    {
        PileKind.Foundation => FoundationCount,
        PileKind.Tableau => TableauCount,
        _ => 1
    };

    public void Place(Location loc, params Card[] cards) => Place(loc, (IEnumerable<Card>)cards);

    public void Place(Location loc, IEnumerable<Card> cards) => Mutable(loc).AddRange(cards);

    /// <summary>Lifts the top <paramref name="count"/> cards off, bottom of the run first. They
    /// are gone from the pile when this returns, so the caller owns them.</summary>
    public IReadOnlyList<Card> Take(Location loc, int count)
    {
        var pile = Mutable(loc);
        var lifted = pile.GetRange(pile.Count - count, count);
        pile.RemoveRange(pile.Count - count, count);
        return lifted;
    }

    public void Strip(Location loc) => Mutable(loc).Clear();

    public void Strip()
    {
        foreach (var kind in AllKinds)
        {
            for (int i = 0; i < PileCountOf(kind); i++)
            {
                Strip(new Location(kind, i));
            }
        }
    }

    /// <summary>Every pile in one fixed order. Only the snapshot needs the position flattened
    /// like this, and it needs the same order both times.</summary>
    internal List<Card>[] EveryPile()
    {
        var all = new List<Card>[2 + FoundationCount + TableauCount];
        all[0] = faceDown;
        all[1] = faceUp;
        foundations.CopyTo(all, 2);
        tableaus.CopyTo(all, 2 + FoundationCount);
        return all;
    }

    private List<Card> Mutable(Location loc) => loc.Kind switch
    {
        PileKind.FaceDown => faceDown,
        PileKind.FaceUp => faceUp,
        PileKind.Foundation => foundations[loc.PileIndex],
        PileKind.Tableau => tableaus[loc.PileIndex],
        _ => throw new ArgumentOutOfRangeException(nameof(loc), loc.Kind, null)
    };
}
