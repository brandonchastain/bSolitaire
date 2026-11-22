namespace BSolitaire.Game;

internal readonly record struct Rect(double X, double Y, double W, double H)
{
    public bool Contains(double x, double y) => x >= X && x < X + W && y >= Y && y < Y + H;
}

/// <summary>Where each thing along the bottom edge goes, worked out together so that no
/// two of them can be given the same pixels. Characters rather than widths, because the
/// band is drawn in a monospace font and the canvas cannot be asked to measure text
/// without stalling the batch it is in the middle of.</summary>
internal readonly record struct BottomRow(
    double ClockX,
    double ScoreRightX,
    double StatsX,
    double StatsRoom,
    double TextY);

/// <summary>
/// Handles board geometry and layout.
/// </summary>
internal sealed class BoardLayout
{
    /// <summary>Playing-card proportions, height over width.</summary>
    private const double CardAspect = 1.4;

    /// <summary>
    /// Gap between cards, as a fraction of card width. Seven columns and their gutters have
    /// to fit across the window, so on a narrow screen this is not decoration — it is the
    /// twelve per cent of the width that is not card. A phone gives it up: there is nothing
    /// else on screen for the board to be crowded by, and the cards need every pixel.
    /// </summary>
    private const double GutterRatio = 0.14;

    private const double CompactGutterRatio = 0.05;

    /// <summary>Margin down either side, as a fraction of card width. A full gutter on a
    /// desktop, next to nothing on a phone, for the same reason.</summary>
    private const double MarginRatio = 0.14;

    private const double CompactMarginRatio = 0.03;

    /// <summary>
    /// The board is compact below this and roomy above it, and in between it is somewhere
    /// between the two. Blended rather than switched: a threshold made the card jump nine
    /// per cent — and *downwards*, since a wider window that has just started paying for
    /// desktop gutters has less left over for card. Widening a window must never shrink a
    /// card, so nothing here is allowed to be a step.
    /// </summary>
    private const double CompactBelow = 420;

    private const double RoomyAbove = 700;

    /// <summary>Cards stop growing here, so the board doesn't look absurd on a big screen.</summary>
    private const double MaxCardWidth = 110;

    private const int TableauColumns = 7;

    /// <summary>How many fanned cards a tableau column tries to show without running off
    /// the bottom. Longer columns exist but are rare enough not to drive the layout.</summary>
    private const int FannedCardsToFit = 13;

    /// <summary>Where a full pip layout stops being worth printing: below this a pip is
    /// under four and a half pixels across.</summary>
    private const double SmallCardWidth = 64;

    /// <summary>The most cards the waste will ever show. Three is what a draw of three
    /// turns over, and the fan has to fit beside the foundations at any card size.</summary>
    private const int MostWasteFanCards = 3;

    /// <summary>The clear strip along the bottom edge that the text lives in. The corner
    /// buttons and the finish offer are both positioned against it, so nothing is ever
    /// drawn across the line.</summary>
    private const double BottomBandHeight = 26;

    private const double BottomTextPx = 14;

    private const double BottomPad = 8;

    /// <summary>
    /// How wide a character is, as a fraction of the font size, in the monospace the bottom
    /// band is drawn in. An upper bound rather than a measurement: Menlo and DejaVu Sans
    /// Mono are both .602 and Consolas is .55, and the canvas can only be asked to measure
    /// text by stalling the batch it is in the middle of — three times a frame, to save
    /// nothing. Being generous is the safe direction: it makes the band give up on the
    /// diagnostics line slightly early rather than slightly late.
    /// </summary>
    private const double MonospaceAdvance = 0.62;

    /// <summary>Clear characters between two things sharing the band.</summary>
    private const double BottomGapChars = 2;

    /// <summary>Piles that can be dropped onto. The stock and waste are never drop targets.</summary>
    private static readonly PileKind[] DropTargetKinds = [PileKind.Tableau, PileKind.Foundation];

    private double gutter;
    private double margin;
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

    /// <summary>
    /// How much of a phone this board is: 1 for a handset, 0 for a window, and the blend in
    /// between. Everything that differs between the two is interpolated across it, so a
    /// window being dragged narrower has cards that only ever grow.
    /// </summary>
    public double Compactness { get; private set; }

    /// <summary>Whether the board is laid out more for a phone than for a window. Only for
    /// the handful of decisions that cannot be a blend, like a minimum tap target.</summary>
    public bool Compact => Compactness > 0.5;

    /// <summary>
    /// Whether the deck should be printed with a jumbo index and one large suit rather than
    /// a full pip layout. This is a question about the card, not about the screen — a fifty
    /// pixel card is unreadable on a phone held upright and equally unreadable in a desktop
    /// window dragged narrow, and deciding it from the viewport had the smaller card getting
    /// the *more* detailed face on the wrong side of a threshold.
    /// </summary>
    public bool SmallCards => CardWidth < SmallCardWidth;

    /// <summary>Derived from the viewport — never set directly.</summary>
    public double CardWidth { get; private set; }

    public double CardHeight { get; private set; }

    /// <summary>Vertical gap between consecutive cards in a fanned tableau pile.</summary>
    public double FanOffset { get; private set; }

    /// <summary>
    /// How many of the waste's cards are shown. The deal's draw count, so a draw of one
    /// stacks its waste in a single slot and a draw of three fans what it turned over.
    /// State rather than a constant because both the drawer and the hit test read it, and
    /// they must never disagree about where a card is — a parameter only the drawer could
    /// supply would guarantee they do.
    /// </summary>
    public int WasteFanCards { get; private set; } = MostWasteFanCards;

    /// <summary>The strip along the bottom the text is drawn in, clear of everything.</summary>
    public Rect BottomBand { get; private set; }

    /// <summary>The middle of the bottom text, which is what the canvas is told, since the
    /// drawer sets a middle baseline once per pass.</summary>
    public double BottomTextY { get; private set; }

    /// <summary>How wide one character of the bottom band's font is.</summary>
    public double GlyphWidth { get; private set; }

    /// <summary>Horizontal gap between consecutive cards in the waste's fan. Its own
    /// property rather than a reuse of <see cref="FanOffset"/>, which is a vertical measure
    /// taken off the card's height and bounded by what a thirteen-card column needs.</summary>
    public double WasteFanOffset { get; private set; }

    /// <summary>The panel shown when a game is over. Drawn only then, but always positioned,
    /// so the drawer and the hit test can't disagree about where it is.</summary>
    public Rect Banner { get; private set; }

    /// <summary>The button inside <see cref="Banner"/> that deals a new game.</summary>
    public Rect NewGameButton { get; private set; }

    /// <summary>The corner toggle between turning one card off the stock and turning three.
    /// Third in the bottom-right row, left of the undo.</summary>
    public Rect DrawModeButton { get; private set; }

    /// <summary>
    /// Takes the last move back. Sits beside the mute toggle in the bottom-right corner, in
    /// the same reliably empty strip and for the same reasons — and it has to be on the felt
    /// rather than on a key, because the device most likely to need an undo is the one with
    /// no keyboard.
    /// </summary>
    public Rect UndoButton { get; private set; }

    /// <summary>The offer to play out a game that is already decided. Sits at the bottom of
    /// the board, clear of the tableau: by the time it appears the columns have shrunk to the
    /// runs left over, so nothing it could cover is still there.</summary>
    public Rect FastForwardButton { get; private set; }

    /// <summary>
    /// The mute toggle, in the bottom-right corner just above the score line. Out of the way
    /// of the deal: the tableau is centred and its columns are only as long as the cards in
    /// them, so the bottom corner is the one part of the board that is reliably empty.
    /// </summary>
    public Rect MuteButton { get; private set; }

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
        Compactness = 1 - Smoothstep(width, CompactBelow, RoomyAbove);
        double gutterRatio = Blend(CompactGutterRatio, GutterRatio);
        double marginRatio = Blend(CompactMarginRatio, MarginRatio);

        double byWidth = width /
            (TableauColumns + (TableauColumns - 1) * gutterRatio + 2 * marginRatio);
        double byHeight = height / (3 * gutterRatio + 3.2 * CardAspect);

        CardWidth = Math.Max(1, Math.Min(MaxCardWidth, Math.Min(byWidth, byHeight)));
        CardHeight = CardWidth * CardAspect;
        gutter = CardWidth * gutterRatio;
        margin = CardWidth * marginRatio;

        // Centre the board rather than letting it hug the left edge of a wide window.
        double boardWidth = TableauColumns * CardWidth + (TableauColumns - 1) * gutter + 2 * margin;
        originX = Math.Max(0, (width - boardWidth) / 2);

        // The gutter is nearly nothing on a phone, and a top row flush against the edge of
        // the screen reads as cut off rather than as tight. This is the one gap that gets a
        // floor under it.
        topRowY = Math.Max(gutter, CardWidth * 0.08);
        tableauY = topRowY + CardHeight + Math.Max(gutter * 1.5, CardWidth * 0.1);

        // Fan tightly enough that a long column still fits, but never so tightly that the
        // cards underneath stop being grabbable — especially with a fingertip.
        double available = Math.Max(CardHeight, height - tableauY - gutter);
        double toFit = (available - CardHeight) / (FannedCardsToFit - 1);

        // A phone is short of width and long on height, and seven columns across a narrow
        // screen cap the card at about a seventh of it however the gutters are spent. So the
        // height is where the difference gets made: fanning at half a card rather than a
        // quarter shows the index *and* the suit of every buried card instead of a sliver of
        // the index, and it fills a board that otherwise sits in the top third of the screen.
        // A thirteen-card column still fits — that is what toFit is — so this only ever
        // takes room nothing else was using.
        FanOffset = Math.Clamp(toFit, CardHeight * 0.12, CardHeight * Blend(0.5, 0.28));

        // The waste fans sideways into the gap the top row leaves at column 2. From the
        // waste's own slot to the first foundation there is a card's width and two gutters,
        // and a three-card fan spends two offsets of it — so half that is the most an offset
        // may be before the fan reaches the foundations. It almost never binds; it is there
        // so that no card size can make the fan collide rather than merely look tight.
        // A fan of one has no offset — every card shares the slot — and the room each of
        // the others gets would be a division by zero without the floor.
        double fanRoom = (CardWidth + 2 * gutter) / Math.Max(1, WasteFanCards - 1);
        WasteFanOffset = WasteFanCards <= 1
            ? 0
            : Math.Min(CardWidth * Blend(0.30, 0.24), fanRoom * 0.9);

        BottomBand = new Rect(0, height - BottomBandHeight, width, BottomBandHeight);
        BottomTextY = height - (BottomBandHeight / 2);
        GlyphWidth = BottomTextPx * MonospaceAdvance;

        // Sized off the card rather than the viewport, so the panel and its text keep the
        // same proportions as everything else on the position.
        double bannerW = Math.Min(width - 2 * gutter, CardWidth * 4.4);

        // Three lines now rather than two — what happened, how long it took, and how that
        // compares — so the panel is taller than the headline and detail alone needed.
        double bannerH = CardHeight * 1.4;
        Banner = new Rect((width - bannerW) / 2, (height - bannerH) / 2, bannerW, bannerH);

        double buttonW = Math.Min(bannerW - gutter * 2, CardWidth * 2.1);
        double buttonH = bannerH * 0.27;
        NewGameButton = new Rect(
            Banner.X + (bannerW - buttonW) / 2,
            Banner.Y + bannerH - buttonH - bannerH * 0.11,
            buttonW,
            buttonH);

        // The score line runs along the bottom edge, so the button sits clear above it
        // rather than on top of it.
        // Big enough to hit with a thumb, which on a phone is the whole of the requirement:
        // a fifth of a card is a comfortable target even when a card is only fifty pixels.
        double muteSize = Math.Max(CardWidth * 0.42, Compact ? 36 : 0);
        MuteButton = new Rect(
            width - muteSize - margin,
            height - muteSize - BottomBandHeight,
            muteSize,
            muteSize);

        UndoButton = new Rect(MuteButton.X - muteSize - gutter, MuteButton.Y, muteSize, muteSize);

        // Third in the same row. A keyboard shortcut alone would do for the stats overlay,
        // which is a developer's switch, but how many cards the stock turns over is how the
        // game plays — and a phone has no D key to press.
        DrawModeButton = new Rect(UndoButton.X - muteSize - gutter, MuteButton.Y, muteSize, muteSize);

        double ffW = Math.Min(width - 2 * gutter, CardWidth * 2.8);
        double ffH = CardHeight * 0.42;
        double ffX = (width - ffW) / 2;

        // Clear of the text, whatever the gutter has collapsed to. On a narrow board two
        // gutters is a few pixels, and the offer was being painted straight across the
        // score line and the clock.
        double ffY = height - ffH - Math.Max(gutter * 2, BottomBandHeight);

        // And clear of the corner buttons, which a centred offer reaches once the board is
        // narrow enough. Controls.Down tests this rect first, so a corner button it covers
        // is a button that cannot be pressed while the offer stands.
        if (ffX + ffW > DrawModeButton.X && ffY + ffH > MuteButton.Y)
        {
            ffY = MuteButton.Y - ffH - gutter;
        }

        FastForwardButton = new Rect(ffX, ffY, ffW, ffH);
    }

    /// <param name="pileCount">
    /// How many cards the pile holds. Only the waste reads it, and only because its fan is
    /// anchored to the end of the pile rather than the start: the top card sits at the far
    /// end of the fan however many cards are buried behind it. Left at one, a card is placed
    /// as the only card in its pile, which is where an empty slot and a card in flight both
    /// want to be.
    /// </param>
    /// <summary>
    /// Says how many of the waste's cards this deal shows, which is how many it turns over.
    /// Belongs with a deal: a change arrives with <see cref="Board.Reset"/>, which repaints
    /// the whole board, and calling it at any other moment would leave the old fan's cards
    /// on the felt with nothing to clear them.
    /// </summary>
    public void SetWasteFan(int cardsPerDraw)
    {
        WasteFanCards = Math.Clamp(cardsPerDraw, 1, MostWasteFanCards);

        // The fan's offset is worked out from the number of places, so it has to be worked
        // out again. Resize only assigns this object's own properties.
        Resize(Width, Height);
    }

    /// <summary>
    /// Where the three things along the bottom edge go: the clock on the left, the player's
    /// tallies on the right, and the diagnostics overlay in whatever is left between them.
    /// Both the drawer and the tests ask this, so what is drawn and what is asserted cannot
    /// come apart.
    /// </summary>
    /// <param name="clockChars">Characters the clock will take.</param>
    /// <param name="scoreChars">Characters the tallies will take.</param>
    public BottomRow BottomRowFor(int clockChars, int scoreChars)
    {
        double clockX = BottomPad;
        double statsX = clockX + ((clockChars + BottomGapChars) * GlyphWidth);
        double scoreLeft = Width - BottomPad - (scoreChars * GlyphWidth);

        return new BottomRow(
            clockX,
            Width - BottomPad,
            statsX,
            scoreLeft - (BottomGapChars * GlyphWidth) - statsX,
            BottomTextY);
    }

    public Rect CardRect(Location loc, int indexInPile, int pileCount = 1)
    {
        return loc.Kind switch
        {
            // The stock shows only its top card, so every card in it shares one slot, and
            // foundations stack rather than fan for the same reason.
            PileKind.FaceDown => new Rect(ColumnX(0), topRowY, CardWidth, CardHeight),

            // The waste fans sideways, newest card last and furthest right.
            PileKind.FaceUp => new Rect(
                ColumnX(1) + (WasteFanSlot(indexInPile, pileCount) * WasteFanOffset),
                topRowY,
                CardWidth,
                CardHeight),

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

        // The waste runs sideways past its slot by the whole width of its fan. Miss this and
        // repainting the waste clears the top card and leaves the two behind it on the felt.
        double fan = loc.Kind == PileKind.FaceUp ? (WasteFanCards - 1) * WasteFanOffset : 0;

        // Reach a little past the slot, because a card's edge is stroked along its boundary
        // and half that line lies outside it. Never past half a gutter, though: the region is
        // cleared and only this pile is drawn back, so a region that reached its neighbour
        // would take a bite out of a column nobody asked to repaint.
        double bleed = Math.Max(1, Math.Min(gutter / 2, 3));

        return new Rect(
            slot.X - bleed,
            slot.Y - bleed,
            slot.W + fan + 2 * bleed,
            Math.Max(slot.H, bottom - slot.Y) + 2 * bleed);
    }

    public Rect EmptySlot(Location loc)
    {
        var cardRect = CardRect(loc, 0);
        return new Rect(cardRect.X, cardRect.Y, CardWidth, CardHeight);
    }

    /// <summary>
    /// Pile-level hit test, used for drop targets. Extends tableau columns to bottom of the position.
    /// </summary>
    public bool TryHitPile(Position position, double x, double y, out Location loc)
    {
        foreach (var kind in DropTargetKinds)
        {
            int pileCount = position.PileCountOf(kind);
            for (int pileIndex = 0; pileIndex < pileCount; pileIndex++)
            {
                var location = new Location(kind, pileIndex);
                var pile = position.Pile(location);
                var slot = EmptySlot(location);

                // A tableau column runs to the bottom of the board; everything else
                // covers only the span its cards actually occupy.
                var top = pile.Count > 0 ? CardRect(location, pile.Count - 1, pile.Count) : slot;
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

    public bool TryHitTest(Position position, double x, double y, out Location loc, out int indexInPile)
    {
        foreach (var kind in Position.AllKinds)
        {
            int pileCount = position.PileCountOf(kind);
            for (int pileIndex = 0; pileIndex < pileCount; pileIndex++)
            {
                var location = new Location(kind, pileIndex);
                var pile = position.Pile(location);

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
                    if (CardRect(location, cardIndex, pile.Count).Contains(x, y))
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

    /// <summary>A smooth 0-to-1 ramp between two widths, flat at both ends. Flat matters: a
    /// linear ramp still changes the layout's mind abruptly at each end of the range.</summary>
    private static double Smoothstep(double value, double from, double to)
    {
        double t = Math.Clamp((value - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>
    /// Which of the fan's places a waste card stands in, counting from the left. The fan is
    /// anchored at the end of the pile: the top card takes the last place the pile is long
    /// enough to fill, and the cards behind it step back towards the slot. Anything buried
    /// deeper than the fan shows shares the first place, since it is never drawn.
    /// </summary>
    private int WasteFanSlot(int indexInPile, int pileCount)
    {
        int places = Math.Clamp(pileCount, 1, WasteFanCards);
        int fromTop = Math.Max(0, pileCount - 1 - indexInPile);

        return Math.Clamp(places - 1 - fromTop, 0, WasteFanCards - 1);
    }

    /// <summary>The phone value or the window value, or wherever between them this board
    /// sits. Every difference between the two layouts goes through here.</summary>
    private double Blend(double phone, double window) =>
        phone + (window - phone) * (1 - Compactness);

    /// <summary>Left edge of one of the seven columns the whole board is built on.</summary>
    private double ColumnX(int column) => originX + margin + column * (CardWidth + gutter);
}