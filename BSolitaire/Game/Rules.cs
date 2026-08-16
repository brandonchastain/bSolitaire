namespace BSolitaire.Game;


public static class Rules
{
    public static bool CanStack(Card moving, Card onto)
    {
        // TODO: alternating color, descending rank
        
        return false;
    }

    public static bool CanFound(Card moving, Card onto)
    {
        // TODO: same suit, ascending rank

        return false;
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

        // TODO: is this move of the `count` cards at move.From to move.To legal?

        return false;
    }

    public static IEnumerable<Move> LegalMoves(Board board)
    {
        // TODO: here's the set of all legal moves on this board

        yield break;
    }
}