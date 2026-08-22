namespace BSolitaire.Game;


internal static class Rules
{
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
    public static IEnumerable<Move> LegalMoves(Position position)
    {
        if (position.FaceDownPile.Count > 0)
        {
            yield return new Move(new Location(PileKind.FaceDown, 0), new Location(PileKind.FaceUp, 0), 1);
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
    public static bool IsStuck(Position position)
    {
        foreach (var move in LegalMoves(position))
        {
            if (IsProgress(position, move))
            {
                return false;
            }
        }

        foreach (var card in position.FaceDownPile)
        {
            if (HasHome(position, card))
            {
                return false;
            }
        }

        foreach (var card in position.FaceUpPile)
        {
            if (HasHome(position, card))
            {
                return false;
            }
        }

        return true;
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