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

    public static IEnumerable<Move> LegalMoves(Board board)
    {
        // TODO: here's the set of all legal moves on this board

        yield break;
    }
}