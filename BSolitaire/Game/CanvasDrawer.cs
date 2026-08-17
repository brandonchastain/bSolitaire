using System.Diagnostics;
using Blazor.Extensions.Canvas.Canvas2D;

namespace BSolitaire.Game;

/// <summary>
/// Draws the game to a 2D canvas.
/// </summary>
public class CanvasDrawer : IGameDrawer
{
    private readonly Canvas2DContext ctx;

    // Every canvas call crosses into JS, so the drawer keeps track of the context's
    // current fill/stroke/font and skips setting a value that is already set. Cards
    // repeat the same handful of styles dozens of times per frame.
    private string? fillStyle;
    private string? strokeStyle;
    private string? font;

    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Queue<double> recentDraws = new();
    private double lastDrawMs;

    public CanvasDrawer(Canvas2DContext ctx)
    {
        this.ctx = ctx;
    }

    public async ValueTask Draw(Solitaire game)
    {
        Board board = game.Board;
        BoardLayout layout = game.Layout;
        DragState? drag = game.Drag;

        double startedAt = clock.Elapsed.TotalMilliseconds;

        // The cached styles describe the context, not this call, but resetting them per
        // frame costs three redundant sets and keeps the cache honest if anything else
        // ever touches the context.
        fillStyle = null;
        strokeStyle = null;
        font = null;

        // One batch per frame: without it every Set*/Fill* call below is its own JS
        // interop round trip, and a dealt board costs several hundred of them per frame.
        await ctx.BeginBatchAsync();

        await Fill("#0b6b3a");
        await ctx.FillRectAsync(0, 0, layout.Width, layout.Height);

        await Fill("#e8f0e8");
        await Font("bold 20px sans-serif");
        await ctx.FillTextAsync("bSolitaire", 24, 44);

        // draw the piles
        foreach (var kind in Board.AllKinds)
        {
            int pileCount = board.PileCountOf(kind);
            for (int pileIndex = 0; pileIndex < pileCount; pileIndex++)
            {
                var location = new Location(kind, pileIndex);
                var pile = board.Pile(location);

                // Cards from the grab index up are held by the pointer, so they are
                // skipped here and painted at the cursor after every pile is drawn.
                int visibleCount = drag != null && drag.From.Kind == kind && drag.From.PileIndex == pileIndex
                    ? Math.Min(pile.Count, drag.Index)
                    : pile.Count;

                if (visibleCount == 0)
                {
                    // draw empty slot
                    var spot = layout.EmptySlot(location);
                    await Stroke("#ffffff");
                    await ctx.StrokeRectAsync(spot.X, spot.Y, spot.W, spot.H);
                    continue;
                }

                // The stock draws every card to the same rect, so only its top card is
                // ever visible — painting the other 23 is pure waste.
                int firstVisible = kind == PileKind.FaceDown ? visibleCount - 1 : 0;

                for (int indexInPile = firstVisible; indexInPile < visibleCount; indexInPile++)
                {
                    await DrawCard(pile[indexInPile], layout.CardRect(location, indexInPile));
                }
            }
        }

        if (drag != null)
        {
            // Lift the held stack off the felt so it reads as above the board.
            await ctx.SetShadowColorAsync("rgba(0, 0, 0, 0.45)");
            await ctx.SetShadowBlurAsync(12);
            await ctx.SetShadowOffsetYAsync(6);

            for (int i = 0; i < drag.Cards.Count; i++)
            {
                var rect = new Rect(
                    drag.X - drag.OffsetX,
                    drag.Y - drag.OffsetY + i * layout.FanOffset,
                    layout.CardWidth,
                    layout.CardHeight);

                await DrawCard(drag.Cards[i], rect);
            }

            await ctx.SetShadowColorAsync("rgba(0, 0, 0, 0)");
            await ctx.SetShadowBlurAsync(0);
            await ctx.SetShadowOffsetYAsync(0);
        }

        if (game.Error != null)
        {
            await Fill("#ff0000");
            await Font("bold 20px sans-serif");
            await ctx.FillTextAsync($"Error: {game.Error}", 24, 80);
        }

        if (game.ShowStats)
        {
            await DrawStats(layout);
        }

        await ctx.EndBatchAsync();

        // Measured after the batch flushes, so it includes the interop round trip.
        // Reported on the next draw, since the text is already in this one.
        lastDrawMs = clock.Elapsed.TotalMilliseconds - startedAt;
    }

    /// <summary>
    /// Draw time and draws per second. Draws per second is not frame rate — the host
    /// skips drawing when nothing changed, so it reads zero on an idle board and only
    /// means anything while something is moving.
    /// </summary>
    private async ValueTask DrawStats(BoardLayout layout)
    {
        double now = clock.Elapsed.TotalMilliseconds;
        while (recentDraws.Count > 0 && now - recentDraws.Peek() > 1000)
        {
            recentDraws.Dequeue();
        }

        recentDraws.Enqueue(now);

        await Fill("#e8f0e8");
        await Font("bold 14px monospace");
        await ctx.FillTextAsync($"{lastDrawMs,5:F1} ms   {recentDraws.Count,3} draws/s", layout.Width - 220, 30);
    }

    private async ValueTask DrawCard(Card card, Rect rect)
    {
        await Stroke("#000000");
        await ctx.StrokeRectAsync(rect.X, rect.Y, rect.W, rect.H);

        if (!card.IsFaceUp)
        {
            // No white undercoat: the back covers exactly the same rect.
            await Fill("#b42020");
            await ctx.FillRectAsync(rect.X, rect.Y, rect.W, rect.H);
            return;
        }

        await Fill("#ffffff");
        await ctx.FillRectAsync(rect.X, rect.Y, rect.W, rect.H);
        await Fill(card.IsRed ? "#ff0000" : "#000000");

        string suitGlyph = card.Suit switch
        {
            Suit.Clubs => "♣",
            Suit.Diamonds => "♦",
            Suit.Hearts => "♥",
            Suit.Spades => "♠",
            _ => throw new ArgumentOutOfRangeException()
        };

        int rank = (int)card.Rank;
        string rankStr = rank switch
        {
            1 => "A",
            11 => "J",
            12 => "Q",
            13 => "K",
            _ => rank.ToString()
        };

        // All four corner marks share one font, so set it once rather than per mark.
        await Font("bold 16px sans-serif");
        await ctx.FillTextAsync(rankStr, rect.X + 4, rect.Y + rect.H / 2 - 40);
        await ctx.FillTextAsync(suitGlyph, rect.X + 4, rect.Y + rect.H / 2 - 20);
        await ctx.FillTextAsync(suitGlyph, rect.X + rect.W - 16, rect.Y + rect.H - 20);
        await ctx.FillTextAsync(rankStr, rect.X + rect.W - 16, rect.Y + rect.H - 4);

        // Add big suit glyph in the center of the card
        await Font("bold 32px sans-serif");
        await ctx.FillTextAsync(suitGlyph, rect.X + rect.W / 2 - 8, rect.Y + rect.H / 2 + 12);
    }

    private async ValueTask Fill(string style)
    {
        if (fillStyle == style)
        {
            return;
        }

        fillStyle = style;
        await ctx.SetFillStyleAsync(style);
    }

    private async ValueTask Stroke(string style)
    {
        if (strokeStyle == style)
        {
            return;
        }

        strokeStyle = style;
        await ctx.SetStrokeStyleAsync(style);
    }

    private async ValueTask Font(string value)
    {
        if (font == value)
        {
            return;
        }

        font = value;
        await ctx.SetFontAsync(value);
    }
}
