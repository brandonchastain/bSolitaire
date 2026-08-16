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
                for (int indexInPile = 0; indexInPile < pile.Count; indexInPile++)
                {
                    // draw card
                    var card = pile[indexInPile];

                    // get card rect (xywh coords)
                    var rect = layout.CardRect(new Location(kind, pileIndex), indexInPile);
                    await ctx.SetFillStyleAsync("#ffffff");
                    await ctx.FillRectAsync(rect.X, rect.Y, rect.W, rect.H);

                    if (kind == PileKind.FaceDown || indexInPile < pile.Count - 1)
                    {
                        await ctx.SetFillStyleAsync("#b42020");
                        await ctx.FillRectAsync(rect.X, rect.Y, rect.W, rect.H);
                        continue;
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
                    await ctx.FillTextAsync($"{suitGlyph}", rect.X + 4, rect.Y + 40);

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
                    await ctx.FillTextAsync($"{rankStr}", rect.X + 4, rect.Y + 20);

                    // Add upside-down rank and suit in the bottom right corner
                    await ctx.SetFontAsync("bold 16px sans-serif");
                    await ctx.FillTextAsync($"{rankStr}", rect.X + rect.W - 16, rect.Y + rect.H - 4);
                    await ctx.FillTextAsync($"{suitGlyph}", rect.X + rect.W - 16, rect.Y + rect.H - 20);

                    // Add big suit glyph in the center of the card
                    await ctx.SetFontAsync("bold 32px sans-serif");
                    await ctx.FillTextAsync($"{suitGlyph}", rect.X + rect.W / 2 - 8, rect.Y + rect.H / 2 + 12);
                }
            }
        }
    }
}
