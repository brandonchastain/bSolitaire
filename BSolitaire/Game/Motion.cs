namespace BSolitaire.Game;

/// <summary>What kind of movement a <see cref="Motion"/> describes.</summary>
internal enum MotionKind
{
    /// <summary>A card travelled from one pile to another.</summary>
    Move,

    /// <summary>A card turned over where it lay, because the move above it uncovered it.</summary>
    Flip
}

/// <summary>
/// One card going somewhere, named by the board and forgotten. The same seam as
/// <see cref="Sound"/>: the position says what happened in terms of piles and indices, and
/// something that knows the geometry — <see cref="Animator"/> — decides what that looks like.
/// The board is already in its new state by the time anyone reads these, so a motion is a
/// description of a change that has happened, not a request for one.
/// </summary>
/// <param name="Kind">Whether the card moved or turned over.</param>
/// <param name="Card">The card itself, so the animation can paint it after it has left its pile.</param>
/// <param name="From">Where it came from. Equal to <paramref name="To"/> for a flip.</param>
/// <param name="FromIndex">Where in that pile it sat before the move.</param>
/// <param name="To">Where it ended up.</param>
/// <param name="ToIndex">Where in that pile it now sits.</param>
/// <param name="Reveals">
/// True when the card was face down on the way and face up on arrival — the stock's deal.
/// The card's own state is already the arrival state, so this is the only record that it
/// spent the first half of the trip showing its back.
/// </param>
internal readonly record struct Motion(
    MotionKind Kind,
    Card Card,
    Location From,
    int FromIndex,
    Location To,
    int ToIndex,
    bool Reveals = false);
