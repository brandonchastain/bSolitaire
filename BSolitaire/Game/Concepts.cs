namespace BSolitaire.Game;

/// <summary>Where a game has got to. Anything other than <see cref="Playing"/> is over.</summary>
internal enum GameState
{
    Playing,

    /// <summary>All fifty-two cards are on the foundations.</summary>
    Won,

    /// <summary>
    /// No legal move remains at all — there is nothing left to drag anywhere. This is about
    /// the position being frozen, not about whether it could ever have been won.
    /// </summary>
    Stuck,

    /// <summary>
    /// Moves remain, but a search has proved that none of them leads to a finished board.
    /// Strictly worse news than <see cref="Stuck"/>, and much harder to establish.
    /// </summary>
    Unwinnable
}

internal enum PileKind
{
    FaceDown,
    FaceUp,
    Foundation,
    Tableau
}

internal enum Suit
{
    Clubs,
    Diamonds,
    Hearts,
    Spades
}

internal enum Rank
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

internal readonly record struct Location(PileKind Kind, int PileIndex);

internal readonly record struct Move(Location From, Location To, int Count);
