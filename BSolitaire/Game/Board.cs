namespace BSolitaire.Game;

/// <summary>
/// Holds the cards, piles, and state of the Solitaire game.
/// </summary>
public class Board
{
    public List<Card> FaceDownPile { get; } = new();
    public List<Card> FaceUpPile { get; } = new();
    public List<Card>[] FoundationPiles { get; } = new List<Card>[4]; // Index 0: Clubs, 1: Diamonds, 2: Hearts, 3: Spades
    public List<Card>[] TableauPiles { get; } = new List<Card>[7]; // Index 0-6: Tableau piles
}

public enum PileKind
{
    FaceDown,
    FaceUp,
    Foundation,
    Tableau
}

public readonly record struct Location(PileKind Kind, int PileIndex);

public readonly record struct Move(Location From, Location To, int Count);

public static class Rules
{
    public static bool CanStack(Card moving, Card onto)
    {
        // alternating color, descending rank
    }

    public static bool CanFound(Card moving, Card onto)
    {
        
    }

    public static bool IsLegal(Board board, Move move);
    public static IEnumerable<Move> LegalMoves(Board board);
}