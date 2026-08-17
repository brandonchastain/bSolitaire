namespace BSolitaire.Game;

/// <summary>What a search found out about a position.</summary>
public enum SolveResult
{
    /// <summary>Still looking.</summary>
    Searching,

    /// <summary>A line to a finished board exists. The game is worth playing on.</summary>
    Winnable,

    /// <summary>Every line from here loses. Proved by exhausting the position's whole tree.</summary>
    Unwinnable,

    /// <summary>The search ran out of budget before it could prove anything either way.</summary>
    Unknown
}

/// <summary>
/// Searches a deal for a line that finishes it.
///
/// The player cannot see the face-down cards, but the program can, so this asks the
/// full-information question — "is this deal still winnable by someone who knows where every
/// card is" — which is decidable, unlike the question the player faces. Klondike's state
/// space is far too big to walk naively, so the search leans on three things: positions are
/// canonicalised and memoised, cards that can never be needed again are sent home without
/// branching, and no-op moves are not generated at all.
///
/// It is deliberately budgeted and resumable: <see cref="Step"/> does a slice of work and
/// returns, so the search rides along on the frame loop instead of freezing the board.
/// Running out of budget yields <see cref="SolveResult.Unknown"/>, never a wrong answer —
/// <see cref="SolveResult.Unwinnable"/> is only ever reported after the entire tree below the
/// position has been examined.
/// </summary>
public sealed class Solver
{
    /// <summary>Positions examined before the search gives up and answers Unknown.</summary>
    public const int DefaultNodeCap = 250_000;

    private const int Suits = 4;
    private const int Piles = 7;

    private readonly Stack<Frame> stack = new();

    // Canonical hashes of positions already expanded. A 64-bit hash rather than the position
    // itself: at a quarter of a million states the table would otherwise dominate the browser
    // tab's memory, and a collision costs a pruned branch, not a wrong answer about a win.
    private readonly HashSet<ulong> seen = new();

    private readonly int nodeCap;
    private readonly bool autoplay;

    /// <param name="autoplay">
    /// Whether cards that can never be wanted again are sent home without branching. On in
    /// the game, where it is most of what makes the search finish. Exists as a switch because
    /// it is the one prune that could in principle hide a win, so it can be cross-checked:
    /// a position called unwinnable with it on must still be unwinnable with it off.
    /// </param>
    public Solver(Board board, int nodeCap = DefaultNodeCap, bool autoplay = true)
    {
        this.nodeCap = nodeCap;
        this.autoplay = autoplay;

        var root = Position.From(board);
        if (root.IsWon)
        {
            Result = SolveResult.Winnable;
            return;
        }

        seen.Add(root.Hash());
        stack.Push(new Frame(Successors(root, autoplay)));
    }

    public SolveResult Result { get; private set; } = SolveResult.Searching;

    /// <summary>Positions examined so far.</summary>
    public int Nodes { get; private set; }

    public bool Done => Result != SolveResult.Searching;

    /// <summary>
    /// Examines up to <paramref name="slice"/> more positions. Returns true once the search
    /// has an answer. Sized by the caller so a slice fits comfortably inside a frame.
    /// </summary>
    public bool Step(int slice)
    {
        if (Done)
        {
            return true; // an answer already, including one the constructor reached
        }

        int examined = 0;

        while (examined < slice)
        {
            if (stack.Count == 0)
            {
                // Every position reachable from the root has been expanded and none of them
                // was a finished board.
                Result = SolveResult.Unwinnable;
                return true;
            }

            var frame = stack.Peek();
            if (frame.Next >= frame.Children.Count)
            {
                stack.Pop();
                continue;
            }

            var child = frame.Children[frame.Next++];
            examined++;
            Nodes++;

            if (child.IsWon)
            {
                Result = SolveResult.Winnable;
                return true;
            }

            if (Nodes >= nodeCap)
            {
                Result = SolveResult.Unknown;
                return true;
            }

            if (!seen.Add(child.Hash()))
            {
                continue;
            }

            stack.Push(new Frame(Successors(child, autoplay)));
        }

        return false;
    }

    /// <summary>
    /// The positions one move away. When a card can be sent home without ever being wanted
    /// again the move is forced — one successor instead of a whole fan of them — which is
    /// what keeps the tree small enough to finish.
    /// </summary>
    private static List<Position> Successors(Position p, bool autoplay)
    {
        if (autoplay && TryAutoplay(p, out var forced))
        {
            return new List<Position> { forced };
        }

        var children = new List<Position>();

        // Tableau and deck to foundations first: they are the moves that finish games, so
        // finding them early ends winnable searches sooner.
        for (int i = 0; i < Piles; i++)
        {
            if (p.Length[i] > p.Down[i] && p.CanFound(p.Top(i)))
            {
                children.Add(p.MoveToFoundation(i));
            }
        }

        for (int card = 0; card < 52; card++)
        {
            if (p.InDeck(card) && p.CanFound((byte)card))
            {
                children.Add(p.DeckToFoundation((byte)card));
            }
        }

        // Tableau runs. Every face-up card is a possible grab point, since the face-up part of
        // a pile is always a properly ordered run.
        for (int from = 0; from < Piles; from++)
        {
            for (int at = p.Down[from]; at < p.Length[from]; at++)
            {
                byte moving = p.Cards[from][at];

                for (int to = 0; to < Piles; to++)
                {
                    if (to == from)
                    {
                        continue;
                    }

                    if (p.Length[to] == 0)
                    {
                        // A king onto an empty column is only progress if it uncovers
                        // something. Moving a whole column into an empty one just swaps
                        // which column is empty.
                        if (RankOf(moving) != 13 || at == 0)
                        {
                            continue;
                        }
                    }
                    else if (!Stacks(moving, p.Top(to)))
                    {
                        continue;
                    }

                    children.Add(p.MoveRun(from, at, to));
                }
            }
        }

        for (int card = 0; card < 52; card++)
        {
            if (!p.InDeck(card))
            {
                continue;
            }

            for (int to = 0; to < Piles; to++)
            {
                if (p.Length[to] == 0 ? RankOf((byte)card) == 13 : Stacks((byte)card, p.Top(to)))
                {
                    children.Add(p.DeckToPile((byte)card, to));
                }
            }
        }

        // Taking a card back off a foundation is a poor move often enough that it goes last,
        // but leaving it out would let the search call a position lost when a retrieval saves
        // it, so it has to be here.
        for (int suit = 0; suit < Suits; suit++)
        {
            if (p.Foundation[suit] == 0)
            {
                continue;
            }

            byte card = MakeCard(suit, p.Foundation[suit]);
            for (int to = 0; to < Piles; to++)
            {
                if (p.Length[to] > 0 && Stacks(card, p.Top(to)))
                {
                    children.Add(p.FoundationToPile(suit, to));
                }
            }
        }

        return children;
    }

    /// <summary>
    /// Finds a card that can be sent to its foundation and never wanted back. A card of rank
    /// r is safe once both opposite-colour foundations have reached r-1 — nothing lower of the
    /// opposite colour is still looking for a black or red base to sit on — and the other
    /// foundation of its own colour has reached r-2. Aces and twos are always safe.
    /// </summary>
    private static bool TryAutoplay(Position p, out Position result)
    {
        for (int i = 0; i < Piles; i++)
        {
            if (p.Length[i] > p.Down[i] && p.CanFound(p.Top(i)) && IsSafe(p, p.Top(i)))
            {
                result = p.MoveToFoundation(i);
                return true;
            }
        }

        for (int card = 0; card < 52; card++)
        {
            if (p.InDeck(card) && p.CanFound((byte)card) && IsSafe(p, (byte)card))
            {
                result = p.DeckToFoundation((byte)card);
                return true;
            }
        }

        result = null!;
        return false;
    }

    private static bool IsSafe(Position p, byte card)
    {
        int rank = RankOf(card);
        if (rank <= 2)
        {
            return true;
        }

        int suit = SuitOf(card);
        bool red = IsRed(suit);
        int otherSameColour = 13;
        int lowestOpposite = 13;

        for (int s = 0; s < Suits; s++)
        {
            if (s == suit)
            {
                continue;
            }

            if (IsRed(s) == red)
            {
                otherSameColour = p.Foundation[s];
            }
            else
            {
                lowestOpposite = Math.Min(lowestOpposite, p.Foundation[s]);
            }
        }

        return lowestOpposite >= rank - 1 && otherSameColour >= rank - 2;
    }

    private static bool Stacks(byte moving, byte onto) =>
        IsRed(SuitOf(moving)) != IsRed(SuitOf(onto)) && RankOf(moving) == RankOf(onto) - 1;

    private static int SuitOf(byte card) => card / 13;

    private static int RankOf(byte card) => card % 13 + 1;

    private static byte MakeCard(int suit, int rank) => (byte)(suit * 13 + rank - 1);

    private static bool IsRed(int suit) => suit == (int)Suit.Diamonds || suit == (int)Suit.Hearts;

    /// <summary>One expanded position and how far its children have been walked.</summary>
    private sealed class Frame
    {
        public Frame(List<Position> children)
        {
            Children = children;
        }

        public List<Position> Children { get; }

        public int Next { get; set; }
    }

    /// <summary>
    /// A board, stripped to what the search needs. The stock and waste collapse into one
    /// unordered set: the game deals one card at a time and recycles without limit, so every
    /// card down there can be brought to the top without disturbing anything else, which makes
    /// their order carry no information.
    /// </summary>
    private sealed class Position
    {
        public byte[][] Cards = null!;  // per pile, bottom to top
        public int[] Length = null!;
        public int[] Down = null!;      // how many of the pile's cards are face down
        public byte[] Foundation = null!; // by suit: highest rank home, 0 for empty
        public ulong Deck;              // cards still in the stock or waste

        public static Position From(Board board)
        {
            var p = new Position
            {
                Cards = new byte[Piles][],
                Length = new int[Piles],
                Down = new int[Piles],
                Foundation = new byte[Suits],
            };

            for (int i = 0; i < Piles; i++)
            {
                var pile = board.TableauPiles[i];
                p.Cards[i] = new byte[52];
                p.Length[i] = pile.Count;

                for (int j = 0; j < pile.Count; j++)
                {
                    p.Cards[i][j] = Encode(pile[j]);
                    if (!pile[j].IsFaceUp)
                    {
                        p.Down[i] = j + 1;
                    }
                }
            }

            foreach (var pile in board.FoundationPiles)
            {
                if (pile.Count > 0)
                {
                    p.Foundation[(int)pile[^1].Suit] = (byte)(int)pile[^1].Rank;
                }
            }

            foreach (var card in board.FaceDownPile)
            {
                p.Deck |= 1UL << Encode(card);
            }

            foreach (var card in board.FaceUpPile)
            {
                p.Deck |= 1UL << Encode(card);
            }

            return p;
        }

        private static byte Encode(Card card) => (byte)((int)card.Suit * 13 + (int)card.Rank - 1);

        public bool IsWon =>
            Foundation[0] == 13 && Foundation[1] == 13 && Foundation[2] == 13 && Foundation[3] == 13;

        public byte Top(int pile) => Cards[pile][Length[pile] - 1];

        public bool InDeck(int card) => (Deck & (1UL << card)) != 0;

        public bool CanFound(byte card) => Foundation[SuitOf(card)] == RankOf(card) - 1;

        public Position Clone()
        {
            var c = new Position
            {
                Cards = new byte[Piles][],
                Length = (int[])Length.Clone(),
                Down = (int[])Down.Clone(),
                Foundation = (byte[])Foundation.Clone(),
                Deck = Deck,
            };

            for (int i = 0; i < Piles; i++)
            {
                c.Cards[i] = (byte[])Cards[i].Clone();
            }

            return c;
        }

        public Position MoveToFoundation(int pile)
        {
            var c = Clone();
            byte card = c.Cards[pile][c.Length[pile] - 1];
            c.Foundation[SuitOf(card)] = (byte)RankOf(card);
            c.Length[pile]--;
            c.Reveal(pile);
            return c;
        }

        public Position DeckToFoundation(byte card)
        {
            var c = Clone();
            c.Foundation[SuitOf(card)] = (byte)RankOf(card);
            c.Deck &= ~(1UL << card);
            return c;
        }

        public Position DeckToPile(byte card, int pile)
        {
            var c = Clone();
            c.Cards[pile][c.Length[pile]++] = card;
            c.Deck &= ~(1UL << card);
            return c;
        }

        public Position FoundationToPile(int suit, int pile)
        {
            var c = Clone();
            byte card = Solver.MakeCard(suit, c.Foundation[suit]);
            c.Foundation[suit]--;
            c.Cards[pile][c.Length[pile]++] = card;
            return c;
        }

        public Position MoveRun(int from, int at, int to)
        {
            var c = Clone();
            int count = c.Length[from] - at;

            for (int i = 0; i < count; i++)
            {
                c.Cards[to][c.Length[to] + i] = c.Cards[from][at + i];
            }

            c.Length[to] += count;
            c.Length[from] = at;
            c.Reveal(from);
            return c;
        }

        /// <summary>
        /// Turns the newly exposed card face up. Taking the whole face-up run off a pile
        /// leaves every remaining card face down, and the top of those turns over — which is
        /// the only way a Klondike position ever gains information, so getting this wrong
        /// makes every deal look lost.
        /// </summary>
        private void Reveal(int pile)
        {
            if (Length[pile] == 0)
            {
                Down[pile] = 0;
            }
            else if (Down[pile] >= Length[pile])
            {
                Down[pile] = Length[pile] - 1;
            }
        }

        /// <summary>
        /// A hash that ignores which column is which: the seven columns are interchangeable,
        /// so two positions that differ only by shuffling them are the same position, and
        /// treating them as such is a large part of what makes the search finish.
        /// </summary>
        public ulong Hash()
        {
            Span<ulong> perPile = stackalloc ulong[Piles];

            for (int i = 0; i < Piles; i++)
            {
                ulong h = 14695981039346656037UL;
                Mix(ref h, (byte)Down[i]);

                for (int j = 0; j < Length[i]; j++)
                {
                    Mix(ref h, Cards[i][j]);
                }

                perPile[i] = h;
            }

            // Insertion sort: seven items, and it runs on every position examined.
            for (int i = 1; i < Piles; i++)
            {
                ulong v = perPile[i];
                int j = i - 1;
                while (j >= 0 && perPile[j] > v)
                {
                    perPile[j + 1] = perPile[j];
                    j--;
                }

                perPile[j + 1] = v;
            }

            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < Piles; i++)
            {
                for (int b = 0; b < 8; b++)
                {
                    Mix(ref hash, (byte)(perPile[i] >> (b * 8)));
                }
            }

            for (int s = 0; s < Suits; s++)
            {
                Mix(ref hash, Foundation[s]);
            }

            for (int b = 0; b < 8; b++)
            {
                Mix(ref hash, (byte)(Deck >> (b * 8)));
            }

            return hash;
        }

        private static void Mix(ref ulong hash, byte value)
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }
    }
}
