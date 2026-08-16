namespace BSolitaire.Game;

/// <summary>
/// Rendering seam. <see cref="Solitaire"/> never talks to a drawer; the host
/// component owns one and hands it the game each frame.
/// </summary>
public interface IGameDrawer
{
    ValueTask Draw(Solitaire game);
}
