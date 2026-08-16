namespace BSolitaire.Game;

public readonly record struct Rect(double X, double Y, double W, double H);

/// <summary>
/// Handles board geometry and layout.
/// </summary>
public sealed class BoardLayout
{
    public BoardLayout(double width, double height, double cardWidth, double cardHeight)
    {
        Width = width;
        Height = height;
        CardWidth = cardWidth;
        CardHeight = cardHeight;
    }

    /// <summary>Board size in CSS pixels. Updated whenever the window resizes.</summary>
    public double Width { get; private set; }

    public double Height { get; private set; }

    public double CardWidth { get; private set; }

    public double CardHeight { get; private set; }

    private double RowOffset => 50;


    public void Resize(double width, double height)
    {
        Width = width;
        Height = height;
    }

    public Rect CardRect(Location loc, int indexInPile)
    {
        // TODO: fan offset applied
        return loc.Kind switch
        {
            PileKind.FaceDown => new Rect(20, RowOffset, CardWidth, CardHeight),
            PileKind.FaceUp => new Rect(Width - CardWidth - 20, RowOffset + CardHeight + 10 + indexInPile * 5, CardWidth, CardHeight),
            PileKind.Foundation => new Rect(20 + loc.PileIndex * (CardWidth + 10), RowOffset + indexInPile * 5, CardWidth, CardHeight),
            PileKind.Tableau => new Rect(20 + loc.PileIndex * (CardWidth + 10), Height - CardHeight - RowOffset + indexInPile * 5, CardWidth, CardHeight),
            _ => throw new ArgumentOutOfRangeException(nameof(loc.Kind), loc.Kind, null)
        };
    }

    public Rect EmptySlot(Location loc)
    {
        return new Rect(loc.PileIndex * (CardWidth + 10) + 20, loc.Kind == PileKind.Tableau ? Height - CardHeight - 20 : 20, CardWidth, CardHeight);
    }

    public bool TryHitTest(Board board, double x, double y, out Location loc, out int indexInPile)
    {
        foreach (var kind in Enum.GetValues<PileKind>())
        {
            var piles = kind switch
            {
                PileKind.FaceDown => new[] { board.FaceDownPile },
                PileKind.FaceUp => new[] { board.FaceUpPile },
                PileKind.Foundation => board.FoundationPiles,
                PileKind.Tableau => board.TableauPiles,
                _ => throw new ArgumentOutOfRangeException()
            };

            for (int pileIndex = 0; pileIndex < piles.Length; pileIndex++)
            {
                var pile = piles[pileIndex];

                if (pile.Count == 0)
                {
                    var location = new Location(kind, pileIndex);
                    var rect = EmptySlot(location);
                    if (x >= rect.X && x <= rect.X + rect.W && y >= rect.Y && y <= rect.Y + rect.H)
                    {
                        loc = location;
                        indexInPile = -1;
                        return true;
                    }
                }

                for (int cardIndex = pile.Count - 1; cardIndex >= 0; cardIndex--)
                {
                    var location = new Location(kind, pileIndex);
                    var rect = CardRect(location, cardIndex);
                    if (x >= rect.X && x <= rect.X + rect.W && y >= rect.Y && y <= rect.Y + rect.H)
                    {
                        loc = location;
                        indexInPile = cardIndex;
                        return true;
                    }
                }
            }
        }

        loc = default;
        indexInPile = -1;

        return false;
    }
}