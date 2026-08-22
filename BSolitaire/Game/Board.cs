namespace BSolitaire.Game;

/// <summary>
/// Holds the cards, piles, and state of the Solitaire game.
/// </summary>
public partial class Board
{
    /// <inheritdoc cref="Position.AllKinds"/>
    public static readonly PileKind[] AllKinds = Position.AllKinds;
    private static readonly Dealer Dealer = new();

    /// <summary>Ace through king: a full foundation.</summary>
    private const int FoundationSize = 13;


    public Board() => Reset();

    /// <summary>
    /// Where the cards are. The board is this, plus everything that has to happen when they
    /// move: the undo history, the change log, and the rules that decide whether they may.
    /// </summary>
    public Position Position { get; } = new();

    /// <inheritdoc cref="Position.FaceDownPile"/>
    public IReadOnlyList<Card> FaceDownPile => Position.FaceDownPile;

    /// <inheritdoc cref="Position.FaceUpPile"/>
    public IReadOnlyList<Card> FaceUpPile => Position.FaceUpPile;

    /// <summary>The four foundations. No pile is reserved for a suit — whichever ace lands on a
    /// pile first claims it for the rest of the game.</summary>
    public IReadOnlyList<IReadOnlyList<Card>> FoundationPiles => Position.FoundationPiles;

    /// <summary>The seven tableau piles, left to right.</summary>
    public IReadOnlyList<IReadOnlyList<Card>> TableauPiles => Position.TableauPiles;

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
    /// Says a pile needs repainting although its contents did not change. The animator is
    /// what needs this: a card in flight is held out of the pile it landed on, so the pile
    /// has to be redrawn once more when the flight ends and the card belongs there again.
    /// </summary>
    public void Touch(Location loc) => MarkDirty(loc);

    private readonly List<Motion> motions = new();

    /// <summary>
    /// Cards that have moved or turned over since the last <see cref="ClearMotions"/>. Named
    /// in the same spirit as <see cref="Sounds"/> — the position reports what happened and
    /// nothing more; <see cref="Animator"/> is what turns that into something on screen.
    /// </summary>
    public IReadOnlyList<Motion> Motions => motions;

    public void ClearMotions() => motions.Clear();

    /// <summary>The same ceiling as the sound queue, and for the same reason: nothing drains
    /// this unless a frame loop is running.</summary>
    private const int MaxQueuedMotions = 64;

    private void Moved(Motion motion)
    {
        if (motions.Count < MaxQueuedMotions)
        {
            motions.Add(motion);
        }
    }

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

    /// <summary>
    /// Which deal this is. Bumped only by <see cref="Reset"/>, unlike <see cref="Version"/>,
    /// which counts positions. Anything that must happen once per game rather than once per
    /// position — counting a deal in the record — keys off this, so taking a move back does
    /// not present the same deal as a fresh one.
    /// </summary>
    public int DealId { get; private set; }

    /// <summary>
    /// Positions to go back to, most recent last. A snapshot rather than an inverse move:
    /// undoing a move also has to put back the card it turned over, and the cheapest way to
    /// be sure every such consequence is undone is to not work out what they were.
    /// </summary>
    private readonly List<Snapshot> history = new();

    /// <summary>How far back a player can go. Long enough to cover any mistake worth taking
    /// back, short enough that a very long game cannot grow the list without bound.</summary>
    private const int MaxUndo = 200;

    /// <summary>
    /// Whether there is a move to take back. A won deal is finished and stays finished —
    /// it has been counted in the record, and the cards are busy falling off the screen.
    /// </summary>
    public bool CanUndo => history.Count > 0 && State != GameState.Won;

    private void PushUndo()
    {
        if (history.Count == MaxUndo)
        {
            history.RemoveAt(0);
        }

        history.Add(Snapshot.Of(this));
    }

    /// <summary>Puts the board back the way it was before the last move. Returns false when
    /// there is nothing to go back to.</summary>
    public bool Undo()
    {
        if (!CanUndo)
        {
            return false;
        }

        history[^1].RestoreTo(this);
        history.RemoveAt(history.Count - 1);

        Play(Sound.Undo);
        AllDirty = true;
        Version++;
        return true;
    }

    /// <summary>Shuffles a new deck and deals it. The old game is simply dropped.</summary>
    public void Reset()
    {
        history.Clear();
        Position.Strip();
        Dealer.Deal(Position);
        Play(Sound.Deal);
        AnnounceDeal();
        AllDirty = true;
        State = GameState.Playing;
        CanFastForward = false;
        Version++;
        DealId++;
    }

    /// <summary>
    /// Reports the deal as twenty-eight cards leaving the stock, in the order they were
    /// dealt: a row at a time, left to right. The dealer has already put them where they go,
    /// so this is only the account of it — but the order is what lets the deal be watched
    /// rather than just appear, so it is worth being exact about.
    /// </summary>
    private void AnnounceDeal()
    {
        var stock = new Location(PileKind.FaceDown, 0);

        for (int row = 0; row < Position.TableauCount; row++)
        {
            for (int pileIndex = row; pileIndex < Position.TableauCount; pileIndex++)
            {
                var card = TableauPiles[pileIndex][row];
                Moved(new Motion(
                    MotionKind.Move,
                    card,
                    stock,
                    0,
                    new Location(PileKind.Tableau, pileIndex),
                    row,
                    Reveals: card.IsFaceUp));
            }
        }
    }

    /// <inheritdoc cref="Position.Pile"/>
    public IReadOnlyList<Card> Pile(Location loc) => Position.Pile(loc);

    /// <inheritdoc cref="Position.PileCountOf"/>
    public int PileCountOf(PileKind kind) => Position.PileCountOf(kind);

    /// <summary>
    /// The foundation <paramref name="card"/> belongs on, or null if it has no home yet.
    /// A card that continues a started pile goes there; an ace prefers the pile matching its
    /// suit — the soft default the comment used to claim — and settles for any empty one, so
    /// the first four aces still end up on four separate piles whatever order they arrive in.
    /// </summary>
    public Location? FoundationFor(Card card)
    {
        for (int i = 0; i < Position.FoundationCount; i++)
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

        for (int i = 0; i < Position.FoundationCount; i++)
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
    public int FoundationTotal => Position.FoundationTotal;

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
        for (int i = 0; i < Position.TableauCount; i++)
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
            // Where the cards sat and where they are about to sit, read off before the move
            // rather than after it: those two slots are the ends of the flight, and once the
            // lists have been spliced neither of them can be recovered.
            int fromIndex = from.Count - move.Count;
            int toIndex = to.Count;

            PushUndo();

            var cardsToMove = Position.Take(move.From, move.Count);
            Position.Place(move.To, cardsToMove);

            // The stock's deal is the one move that turns its card over on the way. Every
            // other card is showing the same face at both ends of the trip.
            bool reveals = move.From.Kind == PileKind.FaceDown;

            for (int i = 0; i < cardsToMove.Count; i++)
            {
                Moved(new Motion(
                    MotionKind.Move,
                    cardsToMove[i],
                    move.From,
                    fromIndex + i,
                    move.To,
                    toIndex + i,
                    reveals));
            }
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

                    var uncovered = move.From;
                    int index = TableauPiles[uncovered.PileIndex].Count - 1;
                    Moved(new Motion(MotionKind.Flip, topCard, uncovered, index, uncovered, index));
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

        PushUndo();

        var waste = new Location(PileKind.FaceUp, 0);
        var stock = new Location(PileKind.FaceDown, 0);

        for (int i = FaceUpPile.Count - 1; i >= 0; i--)
        {
            var card = FaceUpPile[i];
            card.Flip();
            Position.Place(stock, card);
        }

        Position.Strip(waste);
        Play(Sound.Recycle);
        MarkDirty(new Location(PileKind.FaceDown, 0));
        MarkDirty(new Location(PileKind.FaceUp, 0));
        RefreshState();
        return true;
    }

    /// <summary>
    /// A whole position, kept so it can be handed back. Cards are held by reference — they
    /// are the same fifty-two objects for the life of a deal — but which way up each one is
    /// lies on the card itself and is copied, because that is exactly what a move changes
    /// behind the player's back.
    /// </summary>
    private sealed class Snapshot
    {
        private readonly Card[][] piles;
        private readonly bool[][] faceUp;
        private readonly GameState state;
        private readonly bool canFastForward;

        private Snapshot(Card[][] piles, bool[][] faceUp, GameState state, bool canFastForward)
        {
            this.piles = piles;
            this.faceUp = faceUp;
            this.state = state;
            this.canFastForward = canFastForward;
        }

        public static Snapshot Of(Board board)
        {
            var sources = board.Position.EveryPile();
            var piles = new Card[sources.Length][];
            var faceUp = new bool[sources.Length][];

            for (int i = 0; i < sources.Length; i++)
            {
                piles[i] = sources[i].ToArray();
                faceUp[i] = new bool[piles[i].Length];

                for (int j = 0; j < piles[i].Length; j++)
                {
                    faceUp[i][j] = piles[i][j].IsFaceUp;
                }
            }

            return new Snapshot(piles, faceUp, board.State, board.CanFastForward);
        }

        public void RestoreTo(Board board)
        {
            var targets = board.Position.EveryPile();

            for (int i = 0; i < targets.Length; i++)
            {
                targets[i].Clear();
                targets[i].AddRange(piles[i]);

                for (int j = 0; j < piles[i].Length; j++)
                {
                    piles[i][j].SetFaceUp(faceUp[i][j]);
                }
            }

            board.State = state;
            board.CanFastForward = canFastForward;
        }
    }

}
