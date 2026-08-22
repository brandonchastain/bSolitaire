namespace BSolitaire.Game;

/// <summary>
/// Rendering seam. <see cref="Solitaire"/> never talks to a drawer; the host
/// component owns one and hands it the game each frame — as an
/// <see cref="ISolitaireView"/>, so a drawer can read the position but not play it.
/// </summary>
internal interface IGameDrawer
{
    ValueTask Draw(ISolitaireView game);
}
