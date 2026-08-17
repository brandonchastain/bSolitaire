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
    private string? textAlign;

    // Card face fonts, rebuilt once per frame from the current card size rather than per
    // card, so the draw loop allocates nothing.
    private string cornerFont = "";
    private string centreFont = "";

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
        textAlign = null;

        cornerFont = $"bold {layout.CardHeight * 0.16:F0}px sans-serif";
        centreFont = $"bold {layout.CardHeight * 0.38:F0}px sans-serif";

        // One batch per frame: without it every Set*/Fill* call below is its own JS
        // interop round trip, and a dealt board costs several hundred of them per frame.
        await ctx.BeginBatchAsync();

        await Fill("#0b6b3a");
        await ctx.FillRectAsync(0, 0, layout.Width, layout.Height);

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

                // Only the tableau fans. Every other pile stacks its cards in one slot, so
                // painting anything below the top card is invisible work.
                int firstVisible = kind == PileKind.Tableau ? 0 : visibleCount - 1;

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

        // Bottom-left, where nothing else is drawn at any screen size.
        await Fill("#e8f0e8");
        await Font("bold 14px monospace");
        await Align("left");
        await ctx.FillTextAsync($"{lastDrawMs,5:F1} ms   {recentDraws.Count,3} draws/s", 8, layout.Height - 8);
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

        // Everything is proportional to the card, so a face reads the same whether the
        // card is 48px wide on a phone or 110px on a desktop.
        double pad = rect.W * 0.08;
        double corner = rect.H * 0.16;
        double centre = rect.H * 0.38;

        // All four corner marks share one font, so set it once rather than per mark.
        await Font(cornerFont);
        await Align("left");
        await ctx.FillTextAsync(rankStr, rect.X + pad, rect.Y + pad + corner);
        await ctx.FillTextAsync(suitGlyph, rect.X + pad, rect.Y + pad + corner * 2);

        await Align("right");
        await ctx.FillTextAsync(suitGlyph, rect.X + rect.W - pad, rect.Y + rect.H - pad - corner);
        await ctx.FillTextAsync(rankStr, rect.X + rect.W - pad, rect.Y + rect.H - pad);

        // Add big suit glyph in the center of the card
        await Font(centreFont);
        await Align("center");
        await ctx.FillTextAsync(suitGlyph, rect.X + rect.W / 2, rect.Y + rect.H / 2 + centre * 0.35);
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

    private async ValueTask Align(string value)
    {
        if (textAlign == value)
        {
            return;
        }

        textAlign = value;
        await ctx.SetTextAlignAsync(value switch
        {
            "right" => TextAlign.Right,
            "center" => TextAlign.Center,
            _ => TextAlign.Left,
        });
    }
}
