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
    ///
    /// The only way a deal ends badly. A search proving that no line from here wins used to
    /// be a state too, and ended the deal the moment it landed; it is a verdict about the
    /// position rather than a fact about the game, the player can still move, and they are
    /// now told so without the game being taken away from them. The verdict lives where it
    /// is reached, on <see cref="Analyzer"/>.
    /// </summary>
    Stuck
}

/// <summary>
/// How many cards a turn of the stock brings over. The classic game is three; one is the
/// kinder version and the one this game shipped with first.
/// </summary>
internal enum DrawMode
{
    DrawThree,
    DrawOne
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

/// <summary>Where the pointer is, as read once per frame. Motion is continuous, so it arrives
/// with the frame already coalesced to its newest position rather than as its own event.</summary>
internal readonly record struct PointerAt(double X, double Y);

internal readonly record struct Move(Location From, Location To, int Count);
