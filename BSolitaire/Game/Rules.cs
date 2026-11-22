namespace BSolitaire.Game;


internal static class Rules
{
    /// <summary>What a deck the player may turn over indefinitely can still put in front of
    /// them. <see cref="Several"/> and <see cref="Nothing"/> are not degrees of the same
    /// thing: one is a deal with choices left in it, the other is a deal that is over.</summary>
    private enum DeckOffer
    {
        /// <summary>No card the deck can reach has anywhere to go.</summary>
        Nothing,

        /// <summary>Exactly one card, with exactly one place to put it.</summary>
        One,

        /// <summary>More than one play, so what happens next is the player's choice.</summary>
        Several
    }

    public static bool CanStack(Card moving, Card? onto)
    {
        if (onto == null)
        {
            return moving.Rank == Rank.King;
        }

        // TODO: alternating color, descending rank
        if (moving.IsRed == onto.IsRed)
        {
            return false;
        }

        int aRank = (int)moving.Rank;
        int bRank = (int)onto.Rank;

        if (aRank != bRank - 1)
        {
            return false;
        }

        return true;
    }

    public static bool CanFound(Card moving, Card? onto)
    {
        // TODO: same suit, ascending rank
        if (onto == null)
        {
            return moving.Rank == Rank.Ace;
        }
        
        if (moving.Suit != onto.Suit)
        {
            return false;
        }

        int aRank = (int)moving.Rank;
        int bRank = (int)onto.Rank;
        if (aRank != bRank + 1)
        {
            return false;
        }

        return true;
    }

    public static bool IsLegal(Position position, Move move)
    {
        if (move.From == move.To)
        {
            return false;
            //throw new InvalidOperationException("Cannot move cards to the same pile.");
        }

        if (move.Count <= 0)
        {
            return false;
            //throw new InvalidOperationException("Cannot move zero or negative number of cards.");
        }
        
        var from = move.From.Kind switch
        {
            PileKind.FaceDown => position.FaceDownPile,
            PileKind.FaceUp => position.FaceUpPile,
            PileKind.Foundation => position.FoundationPiles[move.From.PileIndex],
            PileKind.Tableau => position.TableauPiles[move.From.PileIndex],
            _ => throw new ArgumentOutOfRangeException(nameof(move.From.Kind), move.From.Kind, null)
        };

        if (from.Count < move.Count)
        {
            return false;
            //throw new InvalidOperationException("Not enough cards in the source pile to move.");
        }

        var to = move.To.Kind switch
        {
            PileKind.FaceDown => position.FaceDownPile,
            PileKind.FaceUp => position.FaceUpPile,
            PileKind.Foundation => position.FoundationPiles[move.To.PileIndex],
            PileKind.Tableau => position.TableauPiles[move.To.PileIndex],
            _ => throw new ArgumentOutOfRangeException(nameof(move.To.Kind), move.To.Kind, null)
        };

        if (move.To.Kind == PileKind.FaceDown)
        {
            return false;
            //throw new InvalidOperationException("Cannot move cards to the face down or face up piles.");
        }

        if (move.From.Kind != PileKind.FaceDown && move.To.Kind == PileKind.FaceUp)
        {
            return false;
            //throw new InvalidOperationException("Cannot move cards to the face down or face up piles.");
        }

        // Nothing the player cannot see can be moved. The stock's deal is the exception that
        // makes the rule: it is the move whose whole purpose is to turn a card over, and the
        // flip happens after it lands. Every other pile gives up only what is face up.
        if (move.From.Kind != PileKind.FaceDown)
        {
            for (int i = from.Count - move.Count; i < from.Count; i++)
            {
                if (!from[i].IsFaceUp)
                {
                    return false;
                }
            }
        }

        if (move.To.Kind == PileKind.Foundation)
        {
            // Foundations take one card at a time. Without this a whole run drags onto a
            // foundation whenever its last card happens to fit, since only from[^1] is checked.
            if (move.Count != 1)
            {
                return false;
            }

            // A card on a foundation is already home. The only such move the ranks allow is
            // an ace sliding to another empty foundation, which changes nothing except which
            // foundation is empty — and left legal it reads as a way out of a dead position.
            if (move.From.Kind == PileKind.Foundation)
            {
                return false;
            }

            if (to.Count == 0)
            {
                return CanFound(from[^1], null);
            }

            return CanFound(from[^1], to[^1]);
        }

        if (move.To.Kind == PileKind.Tableau)
        {
            if (to.Count == 0)
            {
                return CanStack(from[from.Count - move.Count], null);
            }
            return CanStack(from[from.Count - move.Count], to[^1]);
        }

        // TODO: is this move of the `count` cards at move.From to move.To legal?

        return true;
    }

    /// <summary>
    /// Whether the cards at and above <paramref name="index"/> can be lifted off a pile at
    /// all, before any question of where they might land. Position and face-up-ness are as
    /// much a rule of solitaire as rank and colour are — a buried card is not playable, the
    /// waste offers only its top, and the stock is dealt from rather than dragged.
    ///
    /// The pointer needs this in three places: to decide what a press picks up, what a hover
    /// highlights, and what a tap selects. Answering it here rather than at each of them is
    /// what keeps the three from drifting — and the drop that follows still goes through
    /// <see cref="IsLegal"/>, which is the only thing that can approve a move.
    /// </summary>
    public static bool CanLift(Position position, Location loc, int index)
    {
        var pile = position.Pile(loc);

        if (index < 0 || index >= pile.Count)
        {
            return false;
        }

        switch (loc.Kind)
        {
            case PileKind.Tableau:
                // A run comes up together, so every card in it has to be face up — not just
                // the one under the pointer.
                for (int i = index; i < pile.Count; i++)
                {
                    if (!pile[i].IsFaceUp)
                    {
                        return false;
                    }
                }

                return true;

            case PileKind.FaceUp:
            case PileKind.Foundation:
                return index == pile.Count - 1;

            default:
                return false;
        }
    }

    /// <summary>
    /// Every legal move available right now. Candidates are generated here and filtered
    /// through <see cref="IsLegal"/>, so the rules stay in one place. Turning the stock is
    /// included; recycling the waste is not, since it is not expressed as a move. Whether a
    /// move is any *use* is a separate question — see <see cref="IsStuck"/>.
    /// </summary>
    /// <param name="drawCount">How many cards a turn of the stock brings over. Carried as a
    /// parameter rather than read from somewhere because it belongs to the deal being played,
    /// and the stuck check and the fast-forward must both judge the deal actually in front of
    /// the player rather than a constant.</param>
    public static IEnumerable<Move> LegalMoves(Position position, int drawCount = 1)
    {
        if (position.FaceDownPile.Count > 0)
        {
            yield return new Move(
                new Location(PileKind.FaceDown, 0),
                new Location(PileKind.FaceUp, 0),
                Math.Min(drawCount, position.FaceDownPile.Count));
        }

        foreach (var from in Sources(position))
        {
            var pile = position.Pile(from);

            // Only the top card of a pile can be founded, and foundations take one at a time.
            foreach (var to in Destinations(position, PileKind.Foundation))
            {
                var move = new Move(from, to, 1);
                if (IsLegal(position, move))
                {
                    yield return move;
                }
            }

            // A tableau run can be moved from any face-up card down; every other pile
            // gives up its top card only.
            int deepest = from.Kind == PileKind.Tableau ? FirstFaceUp(pile) : pile.Count - 1;
            for (int i = deepest; i >= 0 && i < pile.Count; i++)
            {
                foreach (var to in Destinations(position, PileKind.Tableau))
                {
                    var move = new Move(from, to, pile.Count - i);
                    if (IsLegal(position, move))
                    {
                        yield return move;
                    }
                }
            }
        }
    }

    /// <summary>
    /// True when the game cannot be finished from here: no move on the position does anything,
    /// and no card still in the stock or waste can be played either.
    ///
    /// The deck has to be looked through rather than just asked for its top card, because
    /// turning the stock is always available and recycling the waste is unlimited — so every
    /// card down there will come round again, and the position is only really frozen if none of
    /// them has a home. Conservative by design: any move a player could actually make,
    /// including pulling a card back off a foundation, counts as a move.
    /// </summary>
    public static bool IsStuck(Position position, int drawCount = 1)
    {
        foreach (var move in LegalMoves(position, drawCount))
        {
            if (IsProgress(position, move))
            {
                return false;
            }
        }

        // Nothing on the board moves, so the deal is over unless the deck can still put
        // something playable under the player's hand. Asking that properly means walking the
        // pass rather than scanning the deck, and then following where the pass leads.
        return TheDeckOffersNothing(position, drawCount);
    }

    /// <summary>
    /// The foundation <paramref name="card"/> belongs on, or null if it has no home yet. A card
    /// that continues a started pile goes there; an ace prefers the pile matching its suit and
    /// settles for any empty one, so the first four aces still end up on four separate piles
    /// whatever order they arrive in.
    /// </summary>
    public static Location? FoundationFor(Position position, Card card)
    {
        for (int i = 0; i < Position.FoundationCount; i++)
        {
            var pile = position.FoundationPiles[i];
            if (pile.Count > 0 && CanFound(card, pile[^1]))
            {
                return new Location(PileKind.Foundation, i);
            }
        }

        if (!CanFound(card, null))
        {
            return null;
        }

        int preferred = (int)card.Suit;
        if (position.FoundationPiles[preferred].Count == 0)
        {
            return new Location(PileKind.Foundation, preferred);
        }

        for (int i = 0; i < Position.FoundationCount; i++)
        {
            if (position.FoundationPiles[i].Count == 0)
            {
                return new Location(PileKind.Foundation, i);
            }
        }

        return null;
    }
    /// <summary>
    /// Whether a deck that can be turned over for as long as the player likes has nothing left
    /// to offer — and, when the only thing it offers is forced, whether what follows is any
    /// better.
    ///
    /// The old answer was to look for any card in the stock or waste with somewhere to go.
    /// That is right as far as it goes, but it stops one question short: a single card with a
    /// home keeps a deal alive even when playing it unlocks nothing, so the player cycles the
    /// deck, plays it, and arrives at the same dead position a few moves later having known
    /// all along how it ends. Following the forced line says so at once.
    ///
    /// Only forced lines are followed, and only while they stay forced. Two playable cards, or
    /// one card with two places to go, is a choice, and a choice means the lines differ —
    /// proving one of them dead proves nothing about the deal. It abstains rather than guesses,
    /// because the two ways of being wrong are not equal: a dead deal left alive is an
    /// annoyance, and a live deal declared over is a game taken from a player who could still
    /// have won it.
    /// </summary>
    private static bool TheDeckOffersNothing(Position position, int drawCount)
    {
        // The common answer, and the one the board asks for after nearly every move: no card
        // the deck can reach has anywhere to go. Nothing is copied to find that out.
        var offer = WhatTheDeckOffers(position, drawCount, out var card, out var destination);

        if (offer != DeckOffer.One)
        {
            return offer == DeckOffer.Nothing;
        }

        // Exactly one play, so the line is forced. Follow it — on copies of the cards, because
        // the position being judged is the one the board is about to be drawn from.
        var line = Copy(position);
        offer = WhatTheDeckOffers(line, drawCount, out card, out destination);

        // Each round takes a card out of the deck, so the deck is the bound.
        for (int round = 0; round < 52 && offer == DeckOffer.One; round++)
        {
            Play(line, card!, destination);

            foreach (var move in LegalMoves(line, drawCount))
            {
                if (IsProgress(line, move))
                {
                    // The forced play opened something up after all.
                    return false;
                }
            }

            offer = WhatTheDeckOffers(line, drawCount, out card, out destination);
        }

        return offer == DeckOffer.Nothing;
    }

    /// <summary>
    /// What the deck has for the player: nothing playable at all, exactly one play, or a
    /// choice between several. Only the middle one comes with a card and a destination.
    /// </summary>
    private static DeckOffer WhatTheDeckOffers(
        Position position,
        int drawCount,
        out Card? card,
        out Location destination)
    {
        card = null;
        destination = default;

        foreach (var surfacing in Surfacing(position, drawCount))
        {
            bool found = false;

            foreach (var to in Homes(position, surfacing))
            {
                if (found || card is not null)
                {
                    // A second place for this card, or a second playable card. Which is
                    // taken, and in which order, can change where the deal goes.
                    card = null;
                    return DeckOffer.Several;
                }

                found = true;
                destination = to;
            }

            if (found)
            {
                card = surfacing;
            }
        }

        return card is null ? DeckOffer.Nothing : DeckOffer.One;
    }

    /// <summary>Every place a card could legally land as the position stands.</summary>
    private static IEnumerable<Location> Homes(Position position, Card card)
    {
        // One foundation, not four. Which empty pile an ace starts is no choice at all, and
        // FoundationFor is where the game already settles that.
        if (FoundationFor(position, card) is { } home)
        {
            yield return home;
        }

        foreach (var to in Destinations(position, PileKind.Tableau))
        {
            var pile = position.Pile(to);

            if (CanStack(card, pile.Count > 0 ? pile[^1] : null))
            {
                yield return to;
            }
        }
    }

    /// <summary>
    /// Takes a card out of the deck, wherever in it the card is sitting, and puts it where it
    /// is going. Reaching into the middle of the stock stands for the turns of the stock that
    /// would bring the card to the top of the waste, which cost the player nothing but time
    /// and leave the order of everything else alone.
    ///
    /// Nothing is turned over on the way. A card played off the waste covers nothing, so no
    /// card is uncovered by this, and the flip-on-uncover rule stays where it belongs, in
    /// <see cref="Board.MakeMove"/>, with no second copy of it here to forget.
    /// </summary>
    private static void Play(Position line, Card card, Location destination)
    {
        LiftOut(line, new Location(PileKind.FaceDown, 0), card);
        LiftOut(line, new Location(PileKind.FaceUp, 0), card);

        card.SetFaceUp(true);
        line.Place(destination, card);
    }

    private static void LiftOut(Position line, Location loc, Card card)
    {
        var pile = line.Pile(loc);
        var rest = new List<Card>(pile.Count);
        bool lifted = false;

        foreach (var other in pile)
        {
            if (!lifted && ReferenceEquals(other, card))
            {
                lifted = true;
                continue;
            }

            rest.Add(other);
        }

        if (!lifted)
        {
            return;
        }

        line.Strip(loc);
        line.Place(loc, rest);
    }

    /// <summary>
    /// The cards the deck can bring under the player's hand. Turning the stock is always
    /// available and the waste can be turned back over without limit, so every card down
    /// there comes round again.
    ///
    /// Which of them a draw of more than one actually offers is a narrower set — only one
    /// card of each group is ever on top of the waste — but it is not the obvious every-nth
    /// card, because turning the waste back over reverses it and so deals the next pass in a
    /// different grouping. A card out of reach on one pass can be in reach on the next. Until
    /// that is worked out properly and tested against a draw-three deal actually being played,
    /// this counts them all, which is what the game has always done. Counting a card that
    /// cannot really be reached only keeps a dead-looking deal alive; missing one that can be
    /// would end a game the player could still have won, and those are not the same mistake.
    /// </summary>
    private static IEnumerable<Card> Surfacing(Position position, int drawCount)
    {
        _ = drawCount;

        foreach (var card in position.FaceUpPile)
        {
            yield return card;
        }

        foreach (var card in position.FaceDownPile)
        {
            yield return card;
        }
    }

    /// <summary>A position to try things on. The cards are copies, so nothing done to it can
    /// reach the fifty-two the player is looking at.</summary>
    private static Position Copy(Position position)
    {
        var copy = new Position();

        foreach (var kind in Position.AllKinds)
        {
            for (int i = 0; i < position.PileCountOf(kind); i++)
            {
                var loc = new Location(kind, i);

                foreach (var card in position.Pile(loc))
                {
                    var clone = new Card(card.Suit, card.Rank);
                    clone.SetFaceUp(card.IsFaceUp);
                    copy.Place(loc, clone);
                }
            }
        }

        return copy;
    }

    /// <summary>
    /// Whether a legal move actually changes the position. Turning the stock only cycles the
    /// deck, and moving a whole face-up column to an empty one just relabels which column is
    /// empty — neither is a way out of a dead position.
    /// </summary>
    private static bool IsProgress(Position position, Move move)
    {
        if (move.From.Kind == PileKind.FaceDown)
        {
            return false;
        }

        return !(move.From.Kind == PileKind.Tableau &&
                 move.To.Kind == PileKind.Tableau &&
                 move.Count == position.Pile(move.From).Count &&
                 position.Pile(move.To).Count == 0);
    }

    /// <summary>Whether a card not yet in play could be placed somewhere as the position stands.</summary>
    private static bool HasHome(Position position, Card card)
    {
        foreach (var to in Destinations(position, PileKind.Foundation))
        {
            var pile = position.Pile(to);
            if (CanFound(card, pile.Count > 0 ? pile[^1] : null))
            {
                return true;
            }
        }

        foreach (var to in Destinations(position, PileKind.Tableau))
        {
            var pile = position.Pile(to);
            if (CanStack(card, pile.Count > 0 ? pile[^1] : null))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Index of the lowest face-up card in a pile, or the pile count if there is none.</summary>
    private static int FirstFaceUp(IReadOnlyList<Card> pile)
    {
        for (int i = 0; i < pile.Count; i++)
        {
            if (pile[i].IsFaceUp)
            {
                return i;
            }
        }

        return pile.Count;
    }

    /// <summary>Piles a move can come from: the waste, the tableau, and a foundation.</summary>
    private static IEnumerable<Location> Sources(Position position)
    {
        if (position.FaceUpPile.Count > 0)
        {
            yield return new Location(PileKind.FaceUp, 0);
        }

        foreach (var kind in new[] { PileKind.Tableau, PileKind.Foundation })
        {
            for (int i = 0; i < position.PileCountOf(kind); i++)
            {
                if (position.Pile(new Location(kind, i)).Count > 0)
                {
                    yield return new Location(kind, i);
                }
            }
        }
    }

    private static IEnumerable<Location> Destinations(Position position, PileKind kind)
    {
        for (int i = 0; i < position.PileCountOf(kind); i++)
        {
            yield return new Location(kind, i);
        }
    }

}