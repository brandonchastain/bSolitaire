namespace BSolitaire.Game;

public readonly record struct Location(PileKind Kind, int PileIndex);

public readonly record struct Move(Location From, Location To, int Count);

public enum PileKind
{
    FaceDown,
    FaceUp,
    Foundation,
    Tableau
}

public enum Suit
{
    Clubs,
    Diamonds,
    Hearts,
    Spades
}

public enum Rank
{
    Ace = 1,
    Two,
    Three,
    Four,
    Five,
    Six,
    Seven,
    Eight,
    Nine,
    Ten,
    Jack,
    Queen,
    King
}