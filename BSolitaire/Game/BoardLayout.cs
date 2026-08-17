namespace BSolitaire.Game;

public readonly record struct Rect(double X, double Y, double W, double H)
{
    public bool Contains(double x, double y) => x >= X && x < X + W && y >= Y && y < Y + H;
}

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

    /// <summary>Vertical gap between consecutive cards in a fanned pile.</summary>
    public double FanOffset => 20;

    /// <summary>Piles that can be dropped onto. The stock and waste are never drop targets.</summary>
    private static readonly PileKind[] DropTargetKinds = [PileKind.Tableau, PileKind.Foundation];

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
            PileKind.FaceUp => new Rect(20 + indexInPile * 15, RowOffset + CardHeight + 10, CardWidth, CardHeight),
            PileKind.Foundation => new Rect(CardWidth * 3 + loc.PileIndex * (CardWidth + 10), RowOffset + indexInPile * 20, CardWidth, CardHeight),
            PileKind.Tableau => new Rect(20 + loc.PileIndex * (CardWidth + 10), RowOffset * 7 + indexInPile * 20, CardWidth, CardHeight),
            _ => throw new ArgumentOutOfRangeException(nameof(loc.Kind), loc.Kind, null)
        };
    }

    public Rect EmptySlot(Location loc)
    {
        var cardRect = CardRect(loc, 0);
        return new Rect(cardRect.X, cardRect.Y, CardWidth, CardHeight);
    }

    /// <summary>
    /// Pile-level hit test, used for drop targets. Deliberately more forgiving than
    /// <see cref="TryHitTest"/>: it reports which pile a point falls in rather than
    /// which card, and a tableau column claims everything below its slot — so
    /// dropping past the end of a short column still lands in that column.
    /// </summary>
    public bool TryHitPile(Board board, double x, double y, out Location loc)
    {
        return TryHitTest(board, x, y, out loc, out _);
        foreach (var kind in DropTargetKinds)
        {
            int pileCount = board.PileCountOf(kind);
            for (int pileIndex = 0; pileIndex < pileCount; pileIndex++)
            {
                var location = new Location(kind, pileIndex);
                var pile = board.Pile(location);
                var slot = EmptySlot(location);

                // A tableau column runs to the bottom of the board; everything else
                // covers only the span its cards actually occupy.
                var top = pile.Count > 0 ? CardRect(location, pile.Count - 1) : slot;
                double bottom = kind == PileKind.Tableau
                    ? Math.Max(Height, top.Y + top.H)
                    : top.Y + top.H;

                if (new Rect(slot.X, slot.Y, slot.W, bottom - slot.Y).Contains(x, y))
                {
                    loc = location;
                    return true;
                }
            }
        }

        loc = default;
        return false;
    }

    public bool TryHitTest(Board board, double x, double y, out Location loc, out int indexInPile)
    {
        foreach (var kind in Board.AllKinds)
        {
            int pileCount = board.PileCountOf(kind);
            for (int pileIndex = 0; pileIndex < pileCount; pileIndex++)
            {
                var location = new Location(kind, pileIndex);
                var pile = board.Pile(location);

                if (pile.Count == 0)
                {
                    if (EmptySlot(location).Contains(x, y))
                    {
                        loc = location;
                        indexInPile = -1;
                        return true;
                    }
                }

                for (int cardIndex = pile.Count - 1; cardIndex >= 0; cardIndex--)
                {
                    if (CardRect(location, cardIndex).Contains(x, y))
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