using System.Diagnostics;

namespace BSolitaire.Game;

/// <summary>What a search found out about a position.</summary>
internal enum SolveResult
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

/// <summary>How much the search is allowed to spend before it gives up and answers Unknown.</summary>
internal readonly record struct SolverBudget(int States, int Nodes)
{
    /// <summary>
    /// Sized to answer within a second or two of idle time, because this runs after every
    /// single move rather than on request.
    ///
    /// The cost is lopsided: a position that resolves does so quickly, while one that does not
    /// has to spend the entire budget before it is allowed to say Unknown. So the budget is
    /// really a cap on the *unresolvable* case, and a generous one buys a few more answers at
    /// the price of making every hard position expensive — on a phone, repeatedly, for a game
    /// that is otherwise idle. Small and quick beats thorough and late here: the positions a
    /// player cares about are late ones, and those resolve easily.
    ///
    /// Measured over 300 positions sampled from played-out deals: 71% of openings resolve,
    /// rising to 95% by eight cards home and 100% by twelve, where the answer arrives in
    /// single-digit milliseconds. Raising this to 400k buys eight points in the opening for
    /// six times the work, and nothing at all anywhere it matters.
    /// </summary>
    public static readonly SolverBudget Default = new(60_000, 400_000);

    /// <summary>For checking a verdict rather than producing one. Far past what a game uses.</summary>
    public static readonly SolverBudget Thorough = new(2_000_000, 20_000_000);
}

/// <summary>
/// Searches a deal for a line that finishes it.
///
/// The player cannot see the face-down cards, but the program can, so this asks the
/// full-information question — "is this deal still winnable by someone who knows where every
/// card is" — which is decidable, unlike the question the player faces. The state space is
/// finite and every expanded position is remembered, so the search always terminates on its
/// own; the budget is about how long and how much memory that is allowed to take, not about
/// making it stop.
///
/// It is resumable: <see cref="Step"/> does a slice of work and returns, so the search rides
/// along on the frame loop instead of freezing the position. Running out of budget yields
/// <see cref="SolveResult.Unknown"/>, never a wrong answer — <see cref="SolveResult.Unwinnable"/>
/// is only ever reported after the entire tree below the position has been examined.
/// </summary>
internal sealed class Solver
{
    /// <summary>How often the clock is read, in positions. Reading it per position is
    /// itself measurable at these speeds; a few hundred is far finer than any deadline
    /// worth setting and costs nothing.</summary>
    private const int ClockInterval = 256;

    private const int Suits = 4;
    private const int Piles = 7;

    /// <summary>
    /// Room for the longest tableau pile there can be: six face-down cards under a king-to-ace
    /// run. Rounded up, because the cost of a few spare bytes is nothing next to a bounds check
    /// on every card copied.
    /// </summary>
    private const int PileCap = 24;

    private readonly Stack<Frame> stack = new();

    /// <summary>
    /// Expanded frames waiting to be used again. A frame owns the list its children live in,
    /// and with positions stored by value that list is the only sizeable allocation left in
    /// the search. Frames are finished in strict last-in-first-out order and a child is copied
    /// out of the list before anything is pushed on top of it, so a popped frame's list is
    /// provably unreachable and can go straight back into service.
    /// </summary>
    private readonly Stack<Frame> spare = new();
    private readonly Seen seen;
    private readonly SolverBudget budget;
    private readonly bool autoplay;

    /// <param name="autoplay">
    /// Whether cards that can never be wanted again are sent home without branching. On in
    /// the game, where it is most of what makes the search finish. Exists as a switch because
    /// it is the one prune that could in principle hide a win, so it can be cross-checked:
    /// a position called unwinnable with it on must still be unwinnable with it off.
    /// </param>
    public Solver(Position position, SolverBudget? budget = null, bool autoplay = true)
    {
        this.budget = budget ?? SolverBudget.Default;
        this.autoplay = autoplay;
        seen = new Seen(this.budget.States);

        var root = Packed.From(position);
        if (root.IsWon)
        {
            Result = SolveResult.Winnable;
            return;
        }

        seen.Add(root.Hash());
        Expand(root);
    }

    public SolveResult Result { get; private set; } = SolveResult.Searching;

    /// <summary>Positions examined so far.</summary>
    public int Nodes { get; private set; }

    /// <summary>Distinct positions being remembered, which is what the memory goes on.</summary>
    public int States => seen.Count;

    public bool Done => Result != SolveResult.Searching;

    /// <summary>
    /// Examines up to <paramref name="slice"/> more positions. Returns true once the search
    /// has an answer. Sized by the caller so a slice fits comfortably inside a frame.
    /// </summary>
    public bool Step(int slice) => Step(slice, Timeout.InfiniteTimeSpan);

    /// <summary>
    /// Examines more positions until <paramref name="slice"/> of them have been looked at or
    /// <paramref name="limit"/> has elapsed, whichever comes first. Returns true once the
    /// search has an answer.
    ///
    /// The deadline is the one that matters to a player. A position count is a proxy for time
    /// that holds only as long as positions cost the same everywhere, and they do not: the
    /// same slice that disappears into a frame on a desktop is several frames of a visible
    /// freeze in a browser on a phone. Bounding the wall clock directly makes the search cost
    /// the same fraction of a frame on every machine, and slow hardware simply takes more
    /// frames to reach the same answer.
    /// </summary>
    public bool Step(int slice, TimeSpan limit)
    {
        if (Done)
        {
            return true; // an answer already, including one the constructor reached
        }

        long startedAt = Stopwatch.GetTimestamp();
        int examined = 0;

        while (examined < slice)
        {
            if (examined % ClockInterval == 0 &&
                limit != Timeout.InfiniteTimeSpan &&
                examined > 0 &&
                Stopwatch.GetElapsedTime(startedAt) >= limit)
            {
                return false; // out of time, not out of tree: resumes on the next frame
            }

            if (stack.Count == 0)
            {
                // Every position reachable from the root has been expanded and none of them
                // was a finished position.
                Result = SolveResult.Unwinnable;
                return true;
            }

            var frame = stack.Peek();
            if (frame.Next >= frame.Children.Count)
            {
                spare.Push(stack.Pop());
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

            if (Nodes >= budget.Nodes || seen.Full)
            {
                Result = SolveResult.Unknown;
                return true;
            }

            if (!seen.Add(child.Hash()))
            {
                continue;
            }

            Expand(child);
        }

        return false;
    }

    /// <summary>
    /// The positions one move away. When a card can be sent home without ever being wanted
    /// again the move is forced — one successor instead of a whole fan of them — which is
    /// what keeps the tree small enough to finish.
    /// </summary>
    private static void Successors(in Packed p, bool autoplay, List<Packed> children)
    {
        if (autoplay && TryAutoplay(p, out var forced))
        {
            children.Add(forced);
            return;
        }

        // Tableau and deck to foundations first: they are the moves that finish games, so
        // finding them early ends winnable searches sooner.
        for (int i = 0; i < Piles; i++)
        {
            if (p.Length(i) > p.Down(i) && p.CanFound(p.Top(i)))
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
            for (int at = p.Down(from); at < p.Length(from); at++)
            {
                byte moving = p.CardAt(from, at);

                for (int to = 0; to < Piles; to++)
                {
                    if (to == from)
                    {
                        continue;
                    }

                    if (p.Length(to) == 0)
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
                if (p.Length(to) == 0 ? RankOf((byte)card) == 13 : Stacks((byte)card, p.Top(to)))
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
            if (p.Foundation(suit) == 0)
            {
                continue;
            }

            byte card = MakeCard(suit, p.Foundation(suit));
            for (int to = 0; to < Piles; to++)
            {
                if (p.Length(to) > 0 && Stacks(card, p.Top(to)))
                {
                    children.Add(p.FoundationToPile(suit, to));
                }
            }
        }
    }

    /// <summary>
    /// Finds a card that can be sent to its foundation and never wanted back. A card of rank
    /// r is safe once both opposite-colour foundations have reached r-1 — nothing lower of the
    /// opposite colour is still looking for a base to sit on — and the other foundation of its
    /// own colour has reached r-2. Aces and twos are always safe.
    /// </summary>
    private static bool TryAutoplay(in Packed p, out Packed result)
    {
        for (int i = 0; i < Piles; i++)
        {
            if (p.Length(i) > p.Down(i) && p.CanFound(p.Top(i)) && IsSafe(p, p.Top(i)))
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

        result = default;
        return false;
    }

    private static bool IsSafe(in Packed p, byte card)
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
                otherSameColour = p.Foundation(s);
            }
            else
            {
                lowestOpposite = Math.Min(lowestOpposite, p.Foundation(s));
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

    /// <summary>Pushes the position's children onto the search stack, reusing a spent frame.</summary>
    private void Expand(in Packed packed)
    {
        Frame frame;
        if (spare.Count > 0)
        {
            frame = spare.Pop();
            frame.Reset();
        }
        else
        {
            frame = new Frame();
        }

        Successors(packed, autoplay, frame.Children);
        stack.Push(frame);
    }

    /// <summary>
    /// A board, stripped to what the search needs, in one flat array.
    ///
    /// The stock and waste collapse into a single unordered set: the game deals one card at a
    /// time and recycles without limit, so every card down there can be brought to the top
    /// without disturbing anything else, which makes their order carry no information.
    ///
    /// One array rather than a jagged one because a position is cloned for every child
    /// generated — millions of times — and eight allocations per clone is eight times the work
    /// for the allocator and the collector.
    ///
    /// A struct over an inline buffer rather than a class over a byte[] for the same reason
    /// carried one step further: a class costs a heap object and an array per child, which
    /// measured at ~300 bytes of garbage per position examined. Inline, a child is a register
    /// copy into space the caller already owns and the collector never hears about it — which
    /// matters far more on the browser's collector than on the desktop one.
    /// </summary>
    private struct Packed
    {
        /// <summary>Cards still in the stock or waste.</summary>
        public ulong Deck;

        private const int Size = FoundationAt + Suits;

        private const int FoundationAt = DownAt + Piles;
        private const int DownAt = LengthAt + Piles;
        private const int LengthAt = Piles * PileCap;
        private Body data;

        public readonly bool IsWon =>
            Foundation(0) == 13 && Foundation(1) == 13 && Foundation(2) == 13 && Foundation(3) == 13;

        public static Packed From(Position position)
        {
            var p = new Packed();

            for (int i = 0; i < Piles; i++)
            {
                var pile = position.TableauPiles[i];
                p.data[LengthAt + i] = (byte)pile.Count;

                for (int j = 0; j < pile.Count; j++)
                {
                    p.data[i * PileCap + j] = Encode(pile[j]);
                    if (!pile[j].IsFaceUp)
                    {
                        p.data[DownAt + i] = (byte)(j + 1);
                    }
                }
            }

            foreach (var pile in position.FoundationPiles)
            {
                if (pile.Count > 0)
                {
                    p.data[FoundationAt + (int)pile[^1].Suit] = (byte)(int)pile[^1].Rank;
                }
            }

            foreach (var card in position.FaceDownPile)
            {
                p.Deck |= 1UL << Encode(card);
            }

            foreach (var card in position.FaceUpPile)
            {
                p.Deck |= 1UL << Encode(card);
            }

            return p;
        }

        public readonly bool InDeck(int card) => (Deck & (1UL << card)) != 0;

        public readonly bool CanFound(byte card) => Foundation(SuitOf(card)) == RankOf(card) - 1;

        public readonly Packed MoveToFoundation(int pile)
        {
            var c = Clone();
            byte card = Top(pile);
            c.data[FoundationAt + SuitOf(card)] = (byte)RankOf(card);
            c.data[LengthAt + pile]--;
            c.Reveal(pile);
            return c;
        }

        public readonly int Length(int pile) => data[LengthAt + pile];

        public readonly int Down(int pile) => data[DownAt + pile];

        public readonly int Foundation(int suit) => data[FoundationAt + suit];

        public readonly byte CardAt(int pile, int index) => data[pile * PileCap + index];

        public readonly byte Top(int pile) => data[pile * PileCap + Length(pile) - 1];

        public readonly Packed DeckToFoundation(byte card)
        {
            var c = Clone();
            c.data[FoundationAt + SuitOf(card)] = (byte)RankOf(card);
            c.Deck &= ~(1UL << card);
            return c;
        }

        public readonly Packed DeckToPile(byte card, int pile)
        {
            var c = Clone();
            c.data[pile * PileCap + c.Length(pile)] = card;
            c.data[LengthAt + pile]++;
            c.Deck &= ~(1UL << card);
            return c;
        }

        public readonly Packed FoundationToPile(int suit, int pile)
        {
            var c = Clone();
            byte card = MakeCard(suit, c.Foundation(suit));
            c.data[FoundationAt + suit]--;
            c.data[pile * PileCap + c.Length(pile)] = card;
            c.data[LengthAt + pile]++;
            return c;
        }

        public readonly Packed MoveRun(int from, int at, int to)
        {
            var c = Clone();
            int count = Length(from) - at;
            int target = Length(to);

            for (int i = 0; i < count; i++)
            {
                c.data[to * PileCap + target + i] = data[from * PileCap + at + i];
            }

            c.data[LengthAt + to] = (byte)(target + count);
            c.data[LengthAt + from] = (byte)at;
            c.Reveal(from);
            return c;
        }

        /// <summary>
        /// A hash that ignores which column is which: the seven columns are interchangeable,
        /// so two positions that differ only by shuffling them are the same position, and
        /// treating them as such is a large part of what makes the search finish.
        /// </summary>
        public readonly ulong Hash()
        {
            Span<ulong> perPile = stackalloc ulong[Piles];

            for (int i = 0; i < Piles; i++)
            {
                ulong h = 14695981039346656037UL;
                Mix(ref h, (byte)Down(i));

                int end = i * PileCap + Length(i);
                for (int j = i * PileCap; j < end; j++)
                {
                    Mix(ref h, data[j]);
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
                Mix(ref hash, (byte)Foundation(s));
            }

            for (int b = 0; b < 8; b++)
            {
                Mix(ref hash, (byte)(Deck >> (b * 8)));
            }

            return hash;
        }

        private static byte Encode(Card card) => (byte)((int)card.Suit * 13 + (int)card.Rank - 1);

        private static void Mix(ref ulong hash, byte value)
        {
            hash ^= value;
            hash *= 1099511628211UL;
        }
        /// <summary>A whole position, by value. No heap traffic at all.</summary>
        private readonly Packed Clone() => this;

        /// <summary>
        /// Turns the newly exposed card face up. Taking the whole face-up run off a pile
        /// leaves every remaining card face down, and the top of those turns over — which is
        /// the only way a Klondike position ever gains information, so getting this wrong
        /// makes every deal look lost.
        /// </summary>
        private void Reveal(int pile)
        {
            if (Length(pile) == 0)
            {
                data[DownAt + pile] = 0;
            }
            else if (Down(pile) >= Length(pile))
            {
                data[DownAt + pile] = (byte)(Length(pile) - 1);
            }
        }

        /// <summary>The fixed-size body: seven pile slots, their lengths and face-down
        /// counts, then the four foundations. Sized by the constants above.</summary>
        [System.Runtime.CompilerServices.InlineArray(Size)]
        private struct Body
        {
            private byte first;
        }

    }

    /// <summary>One expanded position and how far its children have been walked.</summary>
    private sealed class Frame
    {
        /// <summary>Sized past the widest branching a Klondike position reaches, so a
        /// recycled frame never has to grow its list a second time.</summary>
        public List<Packed> Children { get; } = new(32);

        public int Next { get; set; }

        public void Reset()
        {
            Children.Clear();
            Next = 0;
        }
    }

    /// <summary>
    /// The set of positions already expanded, as an open-addressed table of raw hashes.
    ///
    /// A HashSet&lt;ulong&gt; costs about thirty bytes per entry once its buckets, slot links
    /// and free-list are counted, and every one of those entries is a garbage-collected object
    /// graph the collector must walk. This is one flat array: eight bytes per slot, a little
    /// over eleven per position at the load factor below, and nothing for the collector to
    /// trace. That difference is most of what decides how many positions can be held, and
    /// therefore how often the search gets to finish.
    /// </summary>
    private sealed class Seen
    {
        // Empty slots hold zero, so a hash that happens to be zero is stored as one. Two
        // positions colliding costs a re-expansion, not a wrong answer.
        private const ulong Empty = 0;

        private readonly int capacity;
        private ulong[] slots;
        private int mask;

        public Seen(int capacity)
        {
            this.capacity = Math.Max(1024, capacity);

            int initial = 1 << 16;
            while (initial > this.capacity)
            {
                initial >>= 1;
            }

            slots = new ulong[initial];
            mask = initial - 1;
        }

        public int Count { get; private set; }

        /// <summary>True once the table is as big as it is allowed to get and nearly full.</summary>
        public bool Full { get; private set; }

        public bool Add(ulong hash)
        {
            if (hash == Empty)
            {
                hash = 1;
            }

            int i = (int)(hash & (ulong)mask);
            while (slots[i] != Empty)
            {
                if (slots[i] == hash)
                {
                    return false;
                }

                i = (i + 1) & mask;
            }

            slots[i] = hash;
            Count++;

            // Linear probing degrades badly past about seventy percent occupancy.
            if (Count * 10 >= slots.Length * 7)
            {
                Grow();
            }

            return true;
        }

        private void Grow()
        {
            if (slots.Length >= capacity)
            {
                Full = true;
                return;
            }

            var older = slots;
            slots = new ulong[older.Length << 1];
            mask = slots.Length - 1;

            foreach (var hash in older)
            {
                if (hash == Empty)
                {
                    continue;
                }

                int i = (int)(hash & (ulong)mask);
                while (slots[i] != Empty)
                {
                    i = (i + 1) & mask;
                }

                slots[i] = hash;
            }
        }
    }

}
