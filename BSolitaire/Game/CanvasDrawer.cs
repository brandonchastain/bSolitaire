using Blazor.Extensions.Canvas.Canvas2D;

namespace BSolitaire.Game;

/// <summary>
/// Draws the game to a 2D canvas. Placeholder for now — replace the body of
/// <see cref="Draw"/> as the game takes shape.
/// </summary>
public class CanvasDrawer : IGameDrawer
{
    private readonly Canvas2DContext ctx;

    public CanvasDrawer(Canvas2DContext ctx)
    {
        this.ctx = ctx;
    }

    public async ValueTask Draw(Solitaire game)
    {
        await ctx.SetFillStyleAsync("#0b6b3a");
        await ctx.FillRectAsync(0, 0, game.Width, game.Height);

        await ctx.SetFillStyleAsync("#e8f0e8");
        await ctx.SetFontAsync("bold 20px sans-serif");
        await ctx.FillTextAsync("bSolitaire", 24, 44);
    }
}
