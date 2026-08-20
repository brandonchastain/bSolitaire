namespace BSolitaire.Game;

/// <summary>
/// A noise the board asks for. The game never makes a sound itself — it names one, the
/// host drains the list each frame, and the browser decides what that actually sounds
/// like. Same seam as <see cref="IGameDrawer"/>, for the same reason: the rules stay
/// unit-testable and know nothing about a browser.
/// </summary>
public enum Sound
{
    /// <summary>A fresh deal: one riffle, not twenty-eight card noises.</summary>
    Deal,

    /// <summary>A tableau card turned face up by the move that uncovered it.</summary>
    Flip,

    /// <summary>A card or stack landing on a pile.</summary>
    Place,

    /// <summary>A card turned off the stock onto the waste.</summary>
    Stock,

    /// <summary>The waste turned back over to form a new stock.</summary>
    Recycle,

    /// <summary>A drop the rules refused. The cards never moved.</summary>
    Invalid,

    /// <summary>A card reaching a foundation.</summary>
    Foundation,

    /// <summary>All fifty-two home.</summary>
    Win,

    /// <summary>A move taken back. Deliberately unlike a place: the ear should be able to
    /// tell a move being made from a move being unmade without looking.</summary>
    Undo
}
