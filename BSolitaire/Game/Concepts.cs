namespace BSolitaire.Game;

public readonly record struct Location(PileKind Kind, int PileIndex);

public readonly record struct Move(Location From, Location To, int Count);

/// <summary>Where a game has got to. Anything other than <see cref="Playing"/> is over.</summary>
public enum GameState
{
    Playing,

    /// <summary>All fifty-two cards are on the foundations.</summary>
    Won,

    /// <summary>No legal move remains, so the game cannot be finished from here.</summary>
    Stuck
}

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