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
    /// <summary>Playing-card proportions, height over width.</summary>
    private const double CardAspect = 1.4;

    /// <summary>Gap between cards, as a fraction of card width.</summary>
    private const double GutterRatio = 0.14;

    /// <summary>Cards stop growing here, so the board doesn't look absurd on a big screen.</summary>
    private const double MaxCardWidth = 110;

    private const int TableauColumns = 7;

    /// <summary>How many fanned cards a tableau column tries to show without running off
    /// the bottom. Longer columns exist but are rare enough not to drive the layout.</summary>
    private const int FannedCardsToFit = 13;

    private double gutter;
    private double originX;
    private double topRowY;
    private double tableauY;

    public BoardLayout(double width, double height)
    {
        Resize(width, height);
    }

    /// <summary>Board size in CSS pixels. Updated whenever the window resizes.</summary>
    public double Width { get; private set; }

    public double Height { get; private set; }

    /// <summary>Derived from the viewport — never set directly.</summary>
    public double CardWidth { get; private set; }

    public double CardHeight { get; private set; }

    /// <summary>Vertical gap between consecutive cards in a fanned tableau pile.</summary>
    public double FanOffset { get; private set; }

    /// <summary>The panel shown when a game is over. Drawn only then, but always positioned,
    /// so the drawer and the hit test can't disagree about where it is.</summary>
    public Rect Banner { get; private set; }

    /// <summary>The button inside <see cref="Banner"/> that deals a new game.</summary>
    public Rect NewGameButton { get; private set; }

    /// <summary>The offer to play out a game that is already decided. Sits at the bottom of
    /// the board, clear of the tableau: by the time it appears the columns have shrunk to the
    /// runs left over, so nothing it could cover is still there.</summary>
    public Rect FastForwardButton { get; private set; }

    /// <summary>Piles that can be dropped onto. The stock and waste are never drop targets.</summary>
    private static readonly PileKind[] DropTargetKinds = [PileKind.Tableau, PileKind.Foundation];

    /// <summary>
    /// Recomputes every dimension from the viewport. Card size is whatever lets the whole
    /// board fit, so the same code lays out a phone in portrait and a desktop window.
    /// </summary>
    public void Resize(double width, double height)
    {
        Width = width;
        Height = height;

        // Width has to hold seven tableau columns plus a gutter between each and either
        // side. Height has to hold the top row, a tableau column, and room to fan below
        // it. Whichever constraint is tighter decides the card size.
        double byWidth = width / (TableauColumns + (TableauColumns + 1) * GutterRatio);
        double byHeight = height / (3 * GutterRatio + 3.2 * CardAspect);

        CardWidth = Math.Max(1, Math.Min(MaxCardWidth, Math.Min(byWidth, byHeight)));
        CardHeight = CardWidth * CardAspect;
        gutter = CardWidth * GutterRatio;

        // Centre the board rather than letting it hug the left edge of a wide window.
        double boardWidth = TableauColumns * CardWidth + (TableauColumns + 1) * gutter;
        originX = Math.Max(0, (width - boardWidth) / 2);

        topRowY = gutter;
        tableauY = topRowY + CardHeight + gutter * 1.5;

        // Fan tightly enough that a long column still fits, but never so tightly that the
        // cards underneath stop being grabbable — especially with a fingertip.
        double available = Math.Max(CardHeight, height - tableauY - gutter);
        double toFit = (available - CardHeight) / (FannedCardsToFit - 1);
        FanOffset = Math.Clamp(toFit, CardHeight * 0.12, CardHeight * 0.28);

        // Sized off the card rather than the viewport, so the panel and its text keep the
        // same proportions as everything else on the board.
        double bannerW = Math.Min(width - 2 * gutter, CardWidth * 4.4);
        double bannerH = CardHeight * 1.15;
        Banner = new Rect((width - bannerW) / 2, (height - bannerH) / 2, bannerW, bannerH);

        double buttonW = Math.Min(bannerW - gutter * 2, CardWidth * 2.1);
        double buttonH = bannerH * 0.34;
        NewGameButton = new Rect(
            Banner.X + (bannerW - buttonW) / 2,
            Banner.Y + bannerH - buttonH - bannerH * 0.14,
            buttonW,
            buttonH);

        double ffW = Math.Min(width - 2 * gutter, CardWidth * 2.8);
        double ffH = CardHeight * 0.42;
        FastForwardButton = new Rect((width - ffW) / 2, height - ffH - gutter * 2, ffW, ffH);
    }

    /// <summary>Left edge of one of the seven columns the whole board is built on.</summary>
    private double ColumnX(int column) => originX + gutter + column * (CardWidth + gutter);

    public Rect CardRect(Location loc, int indexInPile)
    {
        return loc.Kind switch
        {
            // The stock and waste show only their top card, so every card in them shares
            // one slot. Foundations stack rather than fan, same reason.
            PileKind.FaceDown => new Rect(ColumnX(0), topRowY, CardWidth, CardHeight),
            PileKind.FaceUp => new Rect(ColumnX(1), topRowY, CardWidth, CardHeight),

            // Foundations take the right end of the top row, leaving column 2 as a gap.
            PileKind.Foundation => new Rect(ColumnX(3 + loc.PileIndex), topRowY, CardWidth, CardHeight),

            PileKind.Tableau => new Rect(ColumnX(loc.PileIndex), tableauY + indexInPile * FanOffset, CardWidth, CardHeight),
            _ => throw new ArgumentOutOfRangeException(nameof(loc), loc.Kind, null)
        };
    }

    /// <summary>
    /// Everything a pile could be covering, which is what has to be cleared and repainted
    /// when it changes. Tableau columns run to the bottom of the board because a fan can be
    /// any length; nothing else grows past its slot. Columns never overlap each other, so a
    /// region can be repainted without disturbing its neighbours.
    /// </summary>
    public Rect PileRegion(Location loc)
    {
        var slot = EmptySlot(loc);
        double bottom = loc.Kind == PileKind.Tableau ? Height : slot.Y + slot.H;
        return new Rect(slot.X - 1, slot.Y - 1, slot.W + 2, Math.Max(slot.H, bottom - slot.Y) + 2);
    }

    public Rect EmptySlot(Location loc)
    {
        var cardRect = CardRect(loc, 0);
        return new Rect(cardRect.X, cardRect.Y, CardWidth, CardHeight);
    }

    /// <summary>
    /// Pile-level hit test, used for drop targets. Extends tableau columns to bottom of the board.
    /// </summary>
    public bool TryHitPile(Board board, double x, double y, out Location loc)
    {
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