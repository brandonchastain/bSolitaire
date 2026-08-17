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
        Board board = game.Board;
        BoardLayout layout = game.Layout;
        Solitaire.DragState? drag = game.Drag;

        // One batch per frame. Without it every Set*/Fill* call below is its own JS
        // interop round trip, and a dealt board costs several hundred of them per frame
        // — enough to make dragging feel laggy.
        await ctx.BeginBatchAsync();

        await ctx.SetFillStyleAsync("#0b6b3a");
        await ctx.FillRectAsync(0, 0, layout.Width, layout.Height);

        await ctx.SetFillStyleAsync("#e8f0e8");
        await ctx.SetFontAsync("bold 20px sans-serif");
        await ctx.FillTextAsync("bSolitaire", 24, 44);

        // draw the piles
        foreach (var kind in Enum.GetValues<PileKind>())
        {
            var piles = kind switch
            {
                PileKind.FaceDown => new[] { board.FaceDownPile },
                PileKind.FaceUp => new[] { board.FaceUpPile },
                PileKind.Foundation => board.FoundationPiles,
                PileKind.Tableau => board.TableauPiles,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
            };

            for (int pileIndex = 0; pileIndex < piles.Length; pileIndex++)
            {
                var pile = piles[pileIndex];

                // Cards from the grab index up are held by the pointer, so they are
                // skipped here and painted at the cursor after every pile is drawn.
                int visibleCount = drag != null && drag.From.Kind == kind && drag.From.PileIndex == pileIndex
                    ? Math.Min(pile.Count, drag.Index)
                    : pile.Count;

                if (visibleCount == 0)
                {
                    // draw empty slot
                    var spot = layout.EmptySlot(new Location(kind, pileIndex));
                    await ctx.SetStrokeStyleAsync("#ffffff");
                    await ctx.StrokeRectAsync(spot.X, spot.Y, spot.W, spot.H);
                    continue;
                }

                for (int indexInPile = 0; indexInPile < visibleCount; indexInPile++)
                {
                    await DrawCard(pile[indexInPile], layout.CardRect(new Location(kind, pileIndex), indexInPile));
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
            await ctx.SetFillStyleAsync("#ff0000");
            await ctx.SetFontAsync("bold 20px sans-serif");
            await ctx.FillTextAsync($"Error: {game.Error}", 24, 80);
        }

        await ctx.EndBatchAsync();
    }

    private async ValueTask DrawCard(Card card, Rect rect)
    {
        await ctx.SetStrokeStyleAsync("#000000");
        await ctx.StrokeRectAsync(rect.X, rect.Y, rect.W, rect.H);
        await ctx.SetFillStyleAsync("#ffffff");
        await ctx.FillRectAsync(rect.X, rect.Y, rect.W, rect.H);

        if (!card.IsFaceUp)
        {
            await ctx.SetFillStyleAsync("#b42020");
            await ctx.FillRectAsync(rect.X, rect.Y, rect.W, rect.H);
            return;
        }

        if (card.IsRed)
        {
            await ctx.SetFillStyleAsync("#ff0000");
        }
        else
        {
            await ctx.SetFillStyleAsync("#000000");
        }

        // draw the rank and suit
        string suitGlyph = card.Suit switch
        {
            Suit.Clubs => "♣",
            Suit.Diamonds => "♦",
            Suit.Hearts => "♥",
            Suit.Spades => "♠",
            _ => throw new ArgumentOutOfRangeException()
        };
        await ctx.SetFontAsync("bold 16px sans-serif");
        await ctx.FillTextAsync($"{suitGlyph}", rect.X + 4, rect.Y + rect.H / 2 -  20);

        // draw rank
        int rank = (int)card.Rank;
        string rankStr = rank switch
        {
            1 => "A",
            11 => "J",
            12 => "Q",
            13 => "K",
            _ => rank.ToString()
        };
        await ctx.SetFontAsync("bold 16px sans-serif");
        await ctx.FillTextAsync($"{rankStr}", rect.X + 4, rect.Y + rect.H / 2 - 40);

        // Add upside-down rank and suit in the bottom right corner
        await ctx.SetFontAsync("bold 16px sans-serif");
        await ctx.FillTextAsync($"{rankStr}", rect.X + rect.W - 16, rect.Y + rect.H - 4);
        await ctx.FillTextAsync($"{suitGlyph}", rect.X + rect.W - 16, rect.Y + rect.H - 20);

        // Add big suit glyph in the center of the card
        await ctx.SetFontAsync("bold 32px sans-serif");
        await ctx.FillTextAsync($"{suitGlyph}", rect.X + rect.W / 2 - 8, rect.Y + rect.H / 2 + 12);
    }
}
