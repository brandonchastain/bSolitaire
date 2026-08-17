namespace BSolitaire.Game;


public static class Rules
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

    public static bool IsLegal(Board board, Move move)
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
            PileKind.FaceDown => board.FaceDownPile,
            PileKind.FaceUp => board.FaceUpPile,
            PileKind.Foundation => board.FoundationPiles[move.From.PileIndex],
            PileKind.Tableau => board.TableauPiles[move.From.PileIndex],
            _ => throw new ArgumentOutOfRangeException(nameof(move.From.Kind), move.From.Kind, null)
        };

        if (from.Count < move.Count)
        {
            return false;
            //throw new InvalidOperationException("Not enough cards in the source pile to move.");
        }

        var to = move.To.Kind switch
        {
            PileKind.FaceDown => board.FaceDownPile,
            PileKind.FaceUp => board.FaceUpPile,
            PileKind.Foundation => board.FoundationPiles[move.To.PileIndex],
            PileKind.Tableau => board.TableauPiles[move.To.PileIndex],
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

        if (move.To.Kind == PileKind.Foundation)
        {
            // Foundations take one card at a time. Without this a whole run drags onto a
            // foundation whenever its last card happens to fit, since only from[^1] is checked.
            if (move.Count != 1)
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
    /// Every legal move available right now, candidates filtered through
    /// <see cref="IsLegal"/> so the rules live in exactly one place. Turning the stock is
    /// included; recycling the waste is not, since it is not expressed as a move.
    /// </summary>
    public static IEnumerable<Move> LegalMoves(Board board)
    {
        if (board.FaceDownPile.Count > 0)
        {
            yield return new Move(new Location(PileKind.FaceDown, 0), new Location(PileKind.FaceUp, 0), 1);
        }

        foreach (var from in Sources(board))
        {
            var pile = board.Pile(from);

            // Only the top card of a pile can be founded, and foundations take one at a time.
            foreach (var to in Destinations(board, PileKind.Foundation))
            {
                var move = new Move(from, to, 1);
                if (IsLegal(board, move))
                {
                    yield return move;
                }
            }

            // A tableau run can be moved from any face-up card down; every other pile
            // gives up its top card only.
            int deepest = from.Kind == PileKind.Tableau ? FirstFaceUp(pile) : pile.Count - 1;
            for (int i = deepest; i >= 0 && i < pile.Count; i++)
            {
                foreach (var to in Destinations(board, PileKind.Tableau))
                {
                    var move = new Move(from, to, pile.Count - i);
                    if (IsLegal(board, move))
                    {
                        yield return move;
                    }
                }
            }
        }
    }

    /// <summary>
    /// True when the game cannot be finished from here: no move on the board does anything,
    /// and no card still in the stock or waste can be played either.
    ///
    /// The deck has to be looked through rather than just asked for its top card, because
    /// turning the stock is always available and recycling the waste is unlimited — so every
    /// card down there will come round again, and the board is only really frozen if none of
    /// them has a home. Conservative by design: any move a player could actually make,
    /// including pulling a card back off a foundation, counts as a move.
    /// </summary>
    public static bool IsStuck(Board board)
    {
        foreach (var move in LegalMoves(board))
        {
            if (IsProgress(board, move))
            {
                return false;
            }
        }

        foreach (var card in board.FaceDownPile)
        {
            if (HasHome(board, card))
            {
                return false;
            }
        }

        foreach (var card in board.FaceUpPile)
        {
            if (HasHome(board, card))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether a legal move actually changes the position. Turning the stock only cycles the
    /// deck, and moving a whole face-up column to an empty one just relabels which column is
    /// empty — neither is a way out of a dead board.
    /// </summary>
    private static bool IsProgress(Board board, Move move)
    {
        if (move.From.Kind == PileKind.FaceDown)
        {
            return false;
        }

        return !(move.From.Kind == PileKind.Tableau &&
                 move.To.Kind == PileKind.Tableau &&
                 move.Count == board.Pile(move.From).Count &&
                 board.Pile(move.To).Count == 0);
    }

    /// <summary>Whether a card not yet in play could be placed somewhere as the board stands.</summary>
    private static bool HasHome(Board board, Card card)
    {
        foreach (var to in Destinations(board, PileKind.Foundation))
        {
            var pile = board.Pile(to);
            if (CanFound(card, pile.Count > 0 ? pile[^1] : null))
            {
                return true;
            }
        }

        foreach (var to in Destinations(board, PileKind.Tableau))
        {
            var pile = board.Pile(to);
            if (CanStack(card, pile.Count > 0 ? pile[^1] : null))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Index of the lowest face-up card in a pile, or the pile count if there is none.</summary>
    private static int FirstFaceUp(List<Card> pile)
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
    private static IEnumerable<Location> Sources(Board board)
    {
        if (board.FaceUpPile.Count > 0)
        {
            yield return new Location(PileKind.FaceUp, 0);
        }

        foreach (var kind in new[] { PileKind.Tableau, PileKind.Foundation })
        {
            for (int i = 0; i < board.PileCountOf(kind); i++)
            {
                if (board.Pile(new Location(kind, i)).Count > 0)
                {
                    yield return new Location(kind, i);
                }
            }
        }
    }

    private static IEnumerable<Location> Destinations(Board board, PileKind kind)
    {
        for (int i = 0; i < board.PileCountOf(kind); i++)
        {
            yield return new Location(kind, i);
        }
    }
}