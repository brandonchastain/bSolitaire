namespace BSolitaire.Game;

/// <summary>
/// Which cards are in which piles, in what order. That is the whole of it: a position does not
/// know a king from a two. Whether a move is legal belongs to <see cref="Rules"/>, where a card
/// goes home belongs there too, and whether the game is over belongs to <see cref="Board"/>.
/// This holds thirteen ordered lists and hands them out.
///
/// The lists are private and given out read-only. Cards change hands through
/// <see cref="Take"/> and <see cref="Place"/> and nowhere else, because moving a card is never
/// only a splice — see <see cref="Board.MakeMove"/> for the undo step, the motion, the repaint
/// mark, the sound and the state refresh that have to go with it. Keeping the splice here and
/// the bookkeeping there is what stops the two coming apart.
/// </summary>
public sealed class Position
{
    public const int FoundationCount = 4;
    public const int TableauCount = 7;

    /// <summary>Every pile kind. Cached because Enum.GetValues allocates, and the
    /// draw loop and hit test both walk this on every frame and every pointer event.</summary>
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

    /// <summary>The four foundations. No pile is reserved for a suit — whichever ace lands on a
    /// pile first claims it for the rest of the game.</summary>
    public IReadOnlyList<IReadOnlyList<Card>> FoundationPiles => foundations;

    /// <summary>The seven tableau piles, left to right.</summary>
    public IReadOnlyList<IReadOnlyList<Card>> TableauPiles => tableaus;

    /// <summary>The cards in a pile.</summary>
    public IReadOnlyList<Card> Pile(Location loc) => Mutable(loc);

    /// <summary>How many piles of this kind there are.</summary>
    public int PileCountOf(PileKind kind) => kind switch
    {
        PileKind.Foundation => FoundationCount,
        PileKind.Tableau => TableauCount,
        _ => 1
    };

    /// <summary>How many cards are already home. Counting them needs to know nothing about
    /// solitaire, which is why it lives here rather than with the rules.</summary>
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

    /// <summary>Puts cards on a pile, on top of whatever is already there.</summary>
    public void Place(Location loc, params Card[] cards) => Place(loc, (IEnumerable<Card>)cards);

    /// <inheritdoc cref="Place(Location, Card[])"/>
    public void Place(Location loc, IEnumerable<Card> cards) => Mutable(loc).AddRange(cards);

    /// <summary>
    /// Lifts the top <paramref name="count"/> cards off a pile and hands them back in the order
    /// they were sitting in, bottom of the lifted run first. They are gone from the pile when
    /// this returns — the caller is holding the only reference, and is expected to
    /// <see cref="Place"/> them somewhere.
    /// </summary>
    public IReadOnlyList<Card> Take(Location loc, int count)
    {
        var pile = Mutable(loc);
        var lifted = pile.GetRange(pile.Count - count, count);
        pile.RemoveRange(pile.Count - count, count);
        return lifted;
    }

    /// <summary>Takes every card off, leaving thirteen empty piles.</summary>
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

    /// <summary>Takes every card off one pile.</summary>
    public void Strip(Location loc) => Mutable(loc).Clear();

    /// <summary>Every pile, in one fixed order. Only the snapshot needs the position flattened
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
