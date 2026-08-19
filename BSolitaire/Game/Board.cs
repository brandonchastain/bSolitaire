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

    private readonly HashSet<Location> dirty = new();

    /// <summary>
    /// The piles whose contents have changed since the last <see cref="ClearDirty"/>. A move
    /// touches two of them out of thirteen, and repainting a pile costs far more than working
    /// out which ones to repaint — so the board says what it changed rather than leaving the
    /// drawer to diff the whole position or give up and redraw everything.
    /// </summary>
    public IReadOnlyCollection<Location> DirtyPiles => dirty;

    /// <summary>True when the whole position changed at once and naming piles is pointless.</summary>
    public bool AllDirty { get; private set; } = true;

    public void ClearDirty()
    {
        dirty.Clear();
        AllDirty = false;
    }

    private readonly List<Sound> sounds = new();

    /// <summary>
    /// Noises the position has asked for since the last <see cref="ClearSounds"/>. The board
    /// names them and forgets them; playing one is the host's business.
    /// </summary>
    public IReadOnlyList<Sound> Sounds => sounds;

    public void ClearSounds() => sounds.Clear();

    /// <summary>
    /// A ceiling on how many can pile up before anyone listens. Nothing a player can do
    /// reaches it, but the queue is only drained by a running frame loop — if drawing stops,
    /// this stops the list growing for the rest of the session.
    /// </summary>
    private const int MaxQueuedSounds = 32;

    private void Play(Sound sound)
    {
        if (sounds.Count < MaxQueuedSounds)
        {
            sounds.Add(sound);
        }
    }

    private void MarkDirty(Location loc) => dirty.Add(loc);

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
        Play(Sound.Deal);
        AllDirty = true;
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

    /// <summary>How many cards are already home. Used to tell a fast-forward that is making
    /// progress from one that is only turning the stock over.</summary>
    public int FoundationTotal
    {
        get
        {
            int total = 0;
            foreach (var pile in FoundationPiles)
            {
                total += pile.Count;
            }

            return total;
        }
    }

    /// <summary>
    /// Whether the rest of the game is a formality. Once no tableau card is face down there
    /// is nothing left to discover: every remaining card is either on a tableau top or in the
    /// stock, which recycles without limit, so all of them can be reached. Playing it out is
    /// then just clicking, and the game offers to do it instead.
    ///
    /// Greedily sending home whatever can go home always finishes from here. Take the lowest
    /// rank not yet on a foundation: every card below it is already home, tableau piles run
    /// downwards to their top card, so nothing is covering it, and its foundation is waiting
    /// at exactly one less. So a card is always playable until none are left.
    /// </summary>
    public bool CanFastForward { get; private set; }

    /// <summary>
    /// Sends one more card home, or turns the stock over to reach one. Returns false when
    /// there is nothing left to do. One card per call rather than the whole finish at once,
    /// so the caller can spread it over frames and the player gets to watch it happen.
    /// </summary>
    public bool FastForwardStep()
    {
        for (int i = 0; i < NumTableauPiles; i++)
        {
            var pile = TableauPiles[i];
            if (pile.Count > 0 && FoundationFor(pile[^1]) is { } home)
            {
                return MakeMove(new Move(new Location(PileKind.Tableau, i), home, 1));
            }
        }

        if (FaceUpPile.Count > 0 && FoundationFor(FaceUpPile[^1]) is { } wasteHome)
        {
            return MakeMove(new Move(new Location(PileKind.FaceUp, 0), wasteHome, 1));
        }

        // Nothing is playable from where the stock happens to be sitting, so turn it over
        // until the card that is playable comes up. This is the same cycling a player would
        // do by hand, and it terminates for the same reason the finish does.
        return DealFromStock() || RecycleWaste();
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
            // The one place a refusal is heard. Every illegal drop, tap, and shortcut ends
            // up here, so the thunk is written once rather than at each of them.
            Play(Sound.Invalid);
            return false;
        }

        MarkDirty(move.From);
        MarkDirty(move.To);

        if (move.From.Kind == PileKind.FaceDown && move.To.Kind == PileKind.FaceUp)
        {
            var topCard = FaceUpPile[^1];
            topCard.Flip();
            Play(Sound.Stock);
        }
        else
        {
            Play(move.To.Kind == PileKind.Foundation ? Sound.Foundation : Sound.Place);

            if (move.From.Kind == PileKind.Tableau && TableauPiles[move.From.PileIndex].Count > 0)
            {
                var topCard = TableauPiles[move.From.PileIndex][^1];
                if (!topCard.IsFaceUp)
                {
                    topCard.Flip();

                    // After the landing, not instead of it: uncovering a card is a second
                    // thing happening, and the ear expects it a beat late.
                    Play(Sound.Flip);
                }
            }
        }

        RefreshState();

        if (State == GameState.Won)
        {
            Play(Sound.Win);
        }

        return true;
    }

    /// <summary>
    /// Works out whether the game is over. Only called after a move, since nothing else can
    /// end a game — the board is otherwise idle between pointer events.
    /// </summary>
    private void RefreshState()
    {
        Version++;
        CanFastForward = false;

        foreach (var pile in FoundationPiles)
        {
            if (pile.Count < FoundationSize)
            {
                State = Rules.IsStuck(this) ? GameState.Stuck : GameState.Playing;
                CanFastForward = State == GameState.Playing && NothingLeftFaceDown();
                return;
            }
        }

        State = GameState.Won;
    }

    private bool NothingLeftFaceDown()
    {
        foreach (var pile in TableauPiles)
        {
            foreach (var card in pile)
            {
                if (!card.IsFaceUp)
                {
                    return false;
                }
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
        Play(Sound.Recycle);
        MarkDirty(new Location(PileKind.FaceDown, 0));
        MarkDirty(new Location(PileKind.FaceUp, 0));
        RefreshState();
        return true;
    }
}