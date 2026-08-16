namespace BSolitaire.Game;

/// <summary>
/// The whole game lives here. It knows nothing about Blazor, canvas, or JS —
/// it is driven entirely by the four methods below, so it stays unit-testable.
/// </summary>
public class Solitaire
{
    /// <summary>Board size in CSS pixels. Updated whenever the window resizes.</summary>
    public double Width { get; private set; }

    public double Height { get; private set; }

    /// <summary>Time since the game started. Set by <see cref="Update"/>.</summary>
    public TimeSpan Elapsed { get; private set; }

    public void Resize(double width, double height)
    {
        Width = width;
        Height = height;
    }

    /// <summary>Called once per animation frame, before the drawer runs.</summary>
    public void Update(TimeSpan elapsed)
    {
        Elapsed = elapsed;
    }

    /// <summary>A click/tap at (x, y) in CSS pixels, origin at the top-left of the board.</summary>
    public void OnClick(double x, double y)
    {
    }

    /// <summary>A key press, using KeyboardEvent.code values ("KeyR", "Space", "ArrowLeft"...).</summary>
    public void OnKeyDown(string code)
    {
    }

    public void Reset()
    {
    }
}
