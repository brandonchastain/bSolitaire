using System.Diagnostics;
using Microsoft.AspNetCore.Components;
using Blazor.Extensions.Canvas.Canvas2D;

namespace BSolitaire.Game;

/// <summary>
/// Draws the game to a 2D canvas.
/// </summary>
public class CanvasDrawer : IGameDrawer
{
    /// <summary>Whichever context is being drawn into right now: the visible board, or the
    /// off-screen copy the static part of the picture is kept on.</summary>
    private Canvas2DContext ctx;

    private readonly Canvas2DContext board;
    private readonly Canvas2DContext cache;
    private readonly ElementReference cacheElement;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Queue<double> recentDraws = new();

    // Every canvas call crosses into JS, so the drawer keeps track of the context's
    // current fill/stroke/font and skips setting a value that is already set. Cards
    // repeat the same handful of styles dozens of times per frame.
    private string? fillStyle;
    private string? strokeStyle;
    private string? font;
    private string? textAlign;
    private double lineWidth = -1;
    private string rankFont = "";
    private string tenFont = "";
    private string courtFont = "";
    private double lastDrawMs;

    // Palette. Warm paper and a soft edge rather than white on hard black: a pure-white
    // card outlined in black is the single thing that most makes a drawn deck look drawn.
    private const string Paper = "#fbfaf7";
    private const string CardEdge = "#c9c3b8";
    private const string SuitRed = "#c8102e";
    private const string SuitBlack = "#1c1c1c";
    private const string BackField = "#b3122e";
    private const string BackLine = "#d9607a";
    private const string Felt = "#0b6b3a";

    // The face, as fractions of the card. A jumbo index: the rank is the thing a player
    // reads off a fanned column, and every other size on the card is set around leaving it
    // room. Every number here was checked for clearance rather than eyeballed — the tightest
    // gap between any two marks on any of the fifty-two faces is about 2px at a 110px card.
    private const double CornerRadius = 0.075;   // of card width
    private const double IndexAxis = 0.168;       // of card width, in from the near edge
    private const double RankRow = 0.118;        // of card height, in from the near edge
    private const double SuitRow = 0.28;
    private const double RankText = 0.24;

    /// <summary>
    /// How much narrower "10" is set than a single-digit rank. It is the only two-character
    /// rank and, left at full width, it alone decides how far the index column intrudes on
    /// the pips — so it is condensed instead, exactly as a jumbo index deck prints it.
    /// </summary>
    private const double TenScale = 0.70;

    private const double CornerSuitSize = 0.09;  // of card height
    private const double PipSize = 0.07;
    private const double AceSize = 0.34;
    private const double CourtText = 0.30;

    // Pip columns and the rows they sit on.
    private const double ColLeft = 0.37;
    private const double ColMid = 0.50;
    private const double ColRight = 0.63;
    private const double RowTop = 0.225;
    private const double RowBottom = 0.775;
    private const double RowMid = 0.50;
    private const double RowUpper = RowTop + (RowBottom - RowTop) / 3;
    private const double RowLower = RowBottom - (RowBottom - RowTop) / 3;

    private readonly record struct Pip(double Col, double Row);

    /// <summary>
    /// Pip arrangements by rank, ace first, straight off a standard deck. Anything below the
    /// middle of the card is printed upside down, which is decided from the row rather than
    /// stored — that is what keeps the layout symmetric by construction.
    /// </summary>
    private static readonly Pip[][] PipLayouts =
    [
        [new(ColMid, RowMid)],
        [new(ColMid, RowTop), new(ColMid, RowBottom)],
        [new(ColMid, RowTop), new(ColMid, RowMid), new(ColMid, RowBottom)],
        [new(ColLeft, RowTop), new(ColRight, RowTop), new(ColLeft, RowBottom), new(ColRight, RowBottom)],
        [new(ColLeft, RowTop), new(ColRight, RowTop), new(ColMid, RowMid), new(ColLeft, RowBottom), new(ColRight, RowBottom)],
        [new(ColLeft, RowTop), new(ColRight, RowTop), new(ColLeft, RowMid), new(ColRight, RowMid), new(ColLeft, RowBottom), new(ColRight, RowBottom)],
        [new(ColLeft, RowTop), new(ColRight, RowTop), new(ColMid, (RowTop + RowMid) / 2), new(ColLeft, RowMid), new(ColRight, RowMid), new(ColLeft, RowBottom), new(ColRight, RowBottom)],
        [new(ColLeft, RowTop), new(ColRight, RowTop), new(ColMid, (RowTop + RowMid) / 2), new(ColLeft, RowMid), new(ColRight, RowMid), new(ColMid, (RowMid + RowBottom) / 2), new(ColLeft, RowBottom), new(ColRight, RowBottom)],
        [new(ColLeft, RowTop), new(ColRight, RowTop), new(ColLeft, RowUpper), new(ColRight, RowUpper), new(ColMid, RowMid), new(ColLeft, RowLower), new(ColRight, RowLower), new(ColLeft, RowBottom), new(ColRight, RowBottom)],
        [new(ColLeft, RowTop), new(ColRight, RowTop), new(ColLeft, RowUpper), new(ColRight, RowUpper), new(ColMid, (RowTop + RowUpper) / 2), new(ColLeft, RowLower), new(ColRight, RowLower), new(ColMid, (RowLower + RowBottom) / 2), new(ColLeft, RowBottom), new(ColRight, RowBottom)],
    ];

    /// <summary>What the cached picture was drawn for. When this still describes the game,
    /// the cache is still the right picture and nothing under the cards needs redrawing.</summary>
    private double cachedWidth = -1;
    private double cachedHeight = -1;
    private bool cacheValid;

    /// <summary>The pile the last drawn frame had a stack lifted out of, so that when the
    /// stack is dropped or picked up somewhere else, the pile it left gets repainted.</summary>
    private Location? cachedDragSource;

    private readonly List<Location> repaint = new();

    public CanvasDrawer(Canvas2DContext board, Canvas2DContext cache, ElementReference cacheElement)
    {
        this.board = board;
        this.cache = cache;
        this.cacheElement = cacheElement;
        ctx = board;
    }

    public async ValueTask Draw(Solitaire game)
    {
        Board board = game.Board;
        BoardLayout layout = game.Layout;
        DragState? drag = game.Drag;

        double startedAt = clock.Elapsed.TotalMilliseconds;

        // The picture is in two parts. Everything under the pointer's hand — felt, empty
        // slots, every pile — changes only when a move is made, and costs by far the most to
        // draw: a printed pip layout runs to a hundred-odd canvas calls per face. Everything
        // over it — the held stack, the outline under the pointer, the panels — changes every
        // frame but is a handful of cards at most.
        //
        // So the bottom half is drawn once onto an off-screen canvas and copied from there in
        // a single call for as long as it stays true, and only the top half is redrawn per
        // frame. Dragging a stack was costing a full board repaint sixty times a second to
        // move one card.
        // A move touches two piles out of thirteen and a grab touches one, so the cache is
        // patched rather than rebuilt: the changed columns are cleared back to felt and
        // redrawn, and everything else is left alone. Rebuilding the whole board is what made
        // picking a card up and putting it down each cost a visible freeze.
        bool sizeChanged = layout.Width != cachedWidth || layout.Height != cachedHeight;

        repaint.Clear();

        if (!cacheValid || sizeChanged || board.AllDirty)
        {
            ctx = cache;
            await BeginPass(layout);
            await DrawStatic(board, layout, drag);
            await ctx.EndBatchAsync();

            cachedWidth = layout.Width;
            cachedHeight = layout.Height;
            cacheValid = true;
        }
        else
        {
            foreach (var loc in board.DirtyPiles)
            {
                repaint.Add(loc);
            }

            // A stack being lifted out of a pile changes how that pile looks, but only on the
            // frame it is lifted. Once the hole is in the cached picture it stays correct for
            // the whole drag -- the cards are gone from the pile and stay gone -- so this
            // compares against the last frame instead of repainting the column every frame.
            var dragSource = drag?.From;

            if (dragSource != cachedDragSource)
            {
                if (cachedDragSource is { } was && !repaint.Contains(was))
                {
                    repaint.Add(was);
                }

                if (dragSource is { } lifted && !repaint.Contains(lifted))
                {
                    repaint.Add(lifted);
                }
            }

            if (repaint.Count > 0)
            {
                ctx = cache;
                await BeginPass(layout);

                foreach (var loc in repaint)
                {
                    await DrawPileRegion(board, layout, loc, drag);
                }

                await ctx.EndBatchAsync();
            }
        }

        cachedDragSource = drag?.From;

        ctx = this.board;
        await BeginPass(layout);

        await ctx.DrawImageAsync(cacheElement, 0, 0, layout.Width, layout.Height);

        if (game.CanFastForward)
        {
            await DrawFastForward(layout);
        }

        // The stack under the pointer, outlined as one unit: a press takes all of it, so
        // highlighting only the card the cursor is literally over would understate the move.
        if (drag == null && game.HoverPile is { } hovered)
        {
            await DrawHover(board, layout, hovered, game.HoverIndex);
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

                await DrawCard(drag.Cards[i], rect, i < drag.Cards.Count - 1);
            }

            await ctx.SetShadowColorAsync("rgba(0, 0, 0, 0)");
            await ctx.SetShadowBlurAsync(0);
            await ctx.SetShadowOffsetYAsync(0);
        }

        if (game.State != GameState.Playing)
        {
            await DrawBanner(layout, game.State);
        }

        if (game.Error != null)
        {
            await Fill("#ff0000");
            await Font("bold 20px sans-serif");
            await ctx.FillTextAsync($"Error: {game.Error}", 24, 80);
        }

        await DrawScore(layout, game.Score);

        if (game.ShowStats)
        {
            await DrawStats(layout, game.Analysis, game.AnalysisNodes);
        }

        await ctx.EndBatchAsync();

        // Measured after the batch flushes, so it includes the interop round trip.
        // Reported on the next draw, since the text is already in this one.
        lastDrawMs = clock.Elapsed.TotalMilliseconds - startedAt;
    }

    /// <summary>
    /// Starts a batch on whichever context is current and forgets everything cached about
    /// its state. The style cache describes one context, and there are two of them.
    /// </summary>
    private async ValueTask BeginPass(BoardLayout layout)
    {
        fillStyle = null;
        strokeStyle = null;
        font = null;
        textAlign = null;
        lineWidth = -1;
        rankFont = $"bold {layout.CardHeight * RankText:F0}px sans-serif";
        tenFont = $"bold {layout.CardHeight * RankText * TenScale:F0}px sans-serif";
        courtFont = $"{layout.CardHeight * CourtText:F0}px Georgia, serif";

        // One batch per pass: without it every Set*/Fill* call is its own JS interop round
        // trip, and a dealt board costs several hundred of them.
        await ctx.BeginBatchAsync();

        // Everything on the board is positioned by the centre of its text, so this is set
        // once per pass. Positioning by the alphabetic baseline instead is what makes a suit
        // and a rank beside it sit at visibly different heights.
        await ctx.SetTextBaselineAsync(TextBaseline.Middle);
    }

    /// <summary>
    /// Repaints one pile in the cached picture: clear its column back to felt, then draw it
    /// as it now stands. Columns never overlap, so this cannot disturb a neighbour.
    /// </summary>
    private async ValueTask DrawPileRegion(Board board, BoardLayout layout, Location loc, DragState? drag)
    {
        var region = layout.PileRegion(loc);

        await Fill(Felt);
        await ctx.FillRectAsync(region.X, region.Y, region.W, region.H);
        await DrawPile(board, layout, loc, drag);
    }

    /// <summary>
    /// The part of the picture that only a move can change: the felt and every pile on it.
    /// Cards being dragged are left out — they are painted at the cursor instead.
    /// </summary>
    private async ValueTask DrawStatic(Board board, BoardLayout layout, DragState? drag)
    {
        await Fill(Felt);
        await ctx.FillRectAsync(0, 0, layout.Width, layout.Height);

        foreach (var kind in Board.AllKinds)
        {
            int pileCount = board.PileCountOf(kind);
            for (int pileIndex = 0; pileIndex < pileCount; pileIndex++)
            {
                await DrawPile(board, layout, new Location(kind, pileIndex), drag);
            }
        }
    }

    /// <summary>
    /// One pile, fanned or stacked as its kind calls for. Cards being dragged are left out —
    /// they are painted at the cursor instead.
    /// </summary>
    private async ValueTask DrawPile(Board board, BoardLayout layout, Location location, DragState? drag)
    {
        var pile = board.Pile(location);

        int visibleCount = drag != null && drag.From == location
            ? Math.Min(pile.Count, drag.Index)
            : pile.Count;

        if (visibleCount == 0)
        {
            var spot = layout.EmptySlot(location);
            await LineWidth(1);
            await Stroke("rgba(255, 255, 255, 0.28)");
            await RoundedPath(spot, spot.W * CornerRadius);
            await ctx.StrokeAsync();
            return;
        }

        // Only the tableau fans. Every other pile stacks its cards in one slot, so painting
        // anything below the top card is invisible work.
        int firstVisible = location.Kind == PileKind.Tableau ? 0 : visibleCount - 1;

        for (int indexInPile = firstVisible; indexInPile < visibleCount; indexInPile++)
        {
            await DrawCard(
                pile[indexInPile],
                layout.CardRect(location, indexInPile),
                indexInPile < visibleCount - 1);
        }
    }

    /// <summary>
    /// The offer to play out a decided game. Drawn over the cached board rather than into it,
    /// because whether it shows depends on the pointer as well as the position.
    /// </summary>
    private async ValueTask DrawFastForward(BoardLayout layout)
    {
        var button = layout.FastForwardButton;

        await RoundedPath(button, button.H * 0.28);
        await Fill("#12351f");
        await ctx.FillAsync();
        await LineWidth(1);
        await Stroke("#e8f0e8");
        await ctx.StrokeAsync();

        await Fill("#ffffff");
        await Font($"bold {button.H * 0.36:F0}px sans-serif");
        await Align("center");
        await ctx.FillTextAsync(
            "Finish \u25b6\u25b6",
            button.X + button.W / 2,
            button.Y + button.H / 2);
    }

    /// <summary>
    /// The end-of-game panel: what happened, and the button that deals again. Both rects come
    /// from the layout, which is also what the hit test reads, so the button can't drift away
    /// from the thing you click.
    /// </summary>
    private async ValueTask DrawBanner(BoardLayout layout, GameState state)
    {
        var panel = layout.Banner;
        var button = layout.NewGameButton;

        // Dim the board so the panel reads as being in front of it rather than part of it.
        await Fill("rgba(0, 0, 0, 0.45)");
        await ctx.FillRectAsync(0, 0, layout.Width, layout.Height);

        await RoundedPath(panel, panel.H * 0.10);
        await Fill("#12351f");
        await ctx.FillAsync();
        await LineWidth(1);
        await Stroke("#e8f0e8");
        await ctx.StrokeAsync();

        // The two ways of losing are different claims and are worth wording differently:
        // one is "nothing can move", the other is "moves remain but none of them wins".
        (string headline, string detail) = state switch
        {
            GameState.Won => ("You win!", "All 52 cards are home."),
            GameState.Stuck => ("No moves left", "Nothing on the board can move."),
            _ => ("This deal is lost", "No line from here wins."),
        };

        await Fill("#e8f0e8");
        await Font($"bold {panel.H * 0.19:F0}px sans-serif");
        await Align("center");
        await ctx.FillTextAsync(headline, panel.X + panel.W / 2, panel.Y + panel.H * 0.28);

        await Font($"{panel.H * 0.12:F0}px sans-serif");
        await ctx.FillTextAsync(detail, panel.X + panel.W / 2, panel.Y + panel.H * 0.47);

        await RoundedPath(button, button.H * 0.28);
        await Fill("#0b6b3a");
        await ctx.FillAsync();
        await Stroke("#e8f0e8");
        await ctx.StrokeAsync();

        await Fill("#ffffff");
        await Font($"bold {button.H * 0.42:F0}px sans-serif");
        await ctx.FillTextAsync(
            "New Game",
            button.X + button.W / 2,
            button.Y + button.H / 2);
    }

    /// <summary>
    /// The local player's record, bottom-right, where the board never reaches: the tableau is
    /// centred and the fast-forward offer sits in the middle. Drawn on the live pass rather
    /// than the cached board, so it survives the banner dimming the felt behind it.
    /// </summary>
    private async ValueTask DrawScore(BoardLayout layout, PlayerScore score)
    {
        await Fill("#cfe0d2");
        await Font("bold 14px monospace");
        await Align("right");
        await ctx.FillTextAsync(score.Summary, layout.Width - 8, layout.Height - 14);
    }

    /// <summary>
    /// Draw time and draws per second. Draws per second is not frame rate — the host
    /// skips drawing when nothing changed, so it reads zero on an idle board and only
    /// means anything while something is moving.
    /// </summary>
    private async ValueTask DrawStats(BoardLayout layout, SolveResult analysis, int nodes)
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
        await ctx.FillTextAsync(
            $"{lastDrawMs,5:F1} ms   {recentDraws.Count,3} draws/s   search {analysis} {nodes,7:N0}",
            8,
            layout.Height - 14);
    }

    /// <summary>
    /// Draws one card. A <paramref name="covered"/> card has another lying over it, so only
    /// the strip along its top is ever seen: it gets its border and its rank and nothing
    /// else. That is most of the cards on a dealt board, and skipping their faces is what
    /// makes a printed pip layout affordable to draw at all.
    /// </summary>
    /// <summary>
    /// Draws one card. A <paramref name="covered"/> card has another lying over it, so only
    /// the strip along its top is ever seen: it gets its border and its rank and nothing
    /// else. That is most of the cards on a dealt board, and skipping the rest is what makes
    /// a drawn-out pip layout affordable at all.
    /// </summary>
    private async ValueTask DrawCard(Card card, Rect rect, bool covered)
    {
        await RoundedPath(rect, rect.W * CornerRadius);
        await LineWidth(1);
        await Fill(Paper);
        await ctx.FillAsync();
        await Stroke(CardEdge);
        await ctx.StrokeAsync();

        if (!card.IsFaceUp)
        {
            await DrawBack(rect, covered);
            return;
        }

        string colour = card.IsRed ? SuitRed : SuitBlack;
        string rank = RankLabel(card.Rank);

        await Fill(colour);
        await Font(rank.Length > 1 ? tenFont : rankFont);
        await Align("center");

        double nearAxis = rect.X + rect.W * IndexAxis;
        await ctx.FillTextAsync(rank, nearAxis, rect.Y + rect.H * RankRow);

        if (covered)
        {
            return;
        }

        // The far index is the near one turned through half a turn, exactly as it is
        // printed, so the card reads the same whichever way up you look at it.
        double farAxis = rect.X + rect.W * (1 - IndexAxis);
        await DrawInverted(rank, farAxis, rect.Y + rect.H * (1 - RankRow));

        double cornerSize = rect.H * CornerSuitSize;
        await DrawSuit(card.Suit, nearAxis, rect.Y + rect.H * SuitRow, cornerSize, false);
        await DrawSuit(card.Suit, farAxis, rect.Y + rect.H * (1 - SuitRow), cornerSize, true);

        int value = (int)card.Rank;

        if (value >= 11)
        {
            await DrawCourt(rect, card.Suit, rank, colour);
            return;
        }

        double pipSize = rect.H * (value == 1 ? AceSize : PipSize);

        foreach (var pip in PipLayouts[value - 1])
        {
            await DrawSuit(
                card.Suit,
                rect.X + rect.W * pip.Col,
                rect.Y + rect.H * pip.Row,
                pipSize,
                pip.Row > 0.5);
        }
    }

    /// <summary>
    /// The face cards. A real deck prints a courtly figure here; a panel carrying the letter
    /// and the suit is the honest stand-in - it fills the same space and stays legible at the
    /// size a phone draws a card, which a shrunken portrait would not.
    /// </summary>
    private async ValueTask DrawCourt(Rect rect, Suit suit, string rank, string colour)
    {
        var panel = new Rect(
            rect.X + rect.W * 0.27,
            rect.Y + rect.H * 0.22,
            rect.W * 0.46,
            rect.H * 0.56);

        await RoundedPath(panel, rect.W * 0.05);
        await Fill(colour == SuitRed ? "#f8f1ef" : "#f2f2f0");
        await ctx.FillAsync();
        await Stroke(colour);
        await ctx.StrokeAsync();

        // A second hairline inside the first. One line reads as a box drawn round the letter;
        // two read as a border, which is what a court card actually has.
        var innerPanel = new Rect(
            panel.X + rect.W * 0.022,
            panel.Y + rect.W * 0.022,
            panel.W - rect.W * 0.044,
            panel.H - rect.W * 0.044);

        await RoundedPath(innerPanel, rect.W * 0.035);
        await ctx.StrokeAsync();

        await Fill(colour);
        await Font(courtFont);
        await ctx.FillTextAsync(rank, rect.X + rect.W / 2, rect.Y + rect.H / 2);

        double mark = rect.H * 0.062;
        await DrawSuit(suit, rect.X + rect.W * 0.355, rect.Y + rect.H * 0.305, mark, false);
        await DrawSuit(suit, rect.X + rect.W * 0.645, rect.Y + rect.H * 0.695, mark, true);
    }

    /// <summary>
    /// The pattern on the back of a card: a coloured field inside a paper margin, a lattice
    /// over it, and a medallion in the middle. A covered card gets only the field, which is
    /// all of it that shows.
    /// </summary>
    private async ValueTask DrawBack(Rect rect, bool covered)
    {
        double margin = rect.W * 0.07;
        var field = new Rect(
            rect.X + margin,
            rect.Y + margin,
            rect.W - margin * 2,
            rect.H - margin * 2);
        double fieldRadius = rect.W * 0.045;

        await RoundedPath(field, fieldRadius);
        await Fill(BackField);
        await ctx.FillAsync();

        if (covered)
        {
            return;
        }

        // Clipped to the field, so the lattice can be drawn as plain long diagonals and let
        // the clip do the work of stopping them at the border.
        await ctx.SaveAsync();
        await RoundedPath(field, fieldRadius);
        await ctx.ClipAsync();

        await Stroke(BackLine);
        await ctx.BeginPathAsync();

        double step = rect.W * 0.15;
        for (double offset = -rect.H; offset < rect.W + rect.H; offset += step)
        {
            await ctx.MoveToAsync(rect.X + offset, rect.Y);
            await ctx.LineToAsync(rect.X + offset + rect.H, rect.Y + rect.H);
            await ctx.MoveToAsync(rect.X + offset, rect.Y + rect.H);
            await ctx.LineToAsync(rect.X + offset + rect.H, rect.Y);
        }

        await ctx.StrokeAsync();
        await ctx.RestoreAsync();

        double cx = rect.X + rect.W / 2;
        double cy = rect.Y + rect.H / 2;

        await ctx.BeginPathAsync();
        await ctx.MoveToAsync(cx + rect.W * 0.17, cy);
        await ctx.ArcAsync(cx, cy, rect.W * 0.17, 0, Math.PI * 2, false);
        await Fill(Paper);
        await ctx.FillAsync();
        await Stroke(BackField);
        await ctx.StrokeAsync();

        await Fill(BackField);
        await DrawSuit(Suit.Spades, cx, cy, rect.H * 0.085, false);
    }

    /// <summary>
    /// Draws a suit as a filled shape rather than a text glyph.
    ///
    /// This is most of the difference between a deck that looks printed and one that looks
    /// typed. The four suit characters come from whatever font the browser falls back to, at
    /// four different sizes, sitting at four different heights inside their em boxes - so no
    /// amount of adjusting positions makes a heart and a club line up, and what lines up on
    /// one machine does not on the next. These shapes are defined about their own centre and
    /// scaled to equal ink area, so all four are interchangeable by construction.
    ///
    /// <paramref name="size"/> is the nominal size: the shape covers an area of size squared.
    /// </summary>
    private async ValueTask DrawSuit(Suit suit, double cx, double cy, double size, bool inverted)
    {
        double s = size;
        double flip = inverted ? -1 : 1;

        double X(double u) => cx + flip * u * s;
        double Y(double v) => cy + flip * v * s;

        await ctx.BeginPathAsync();

        switch (suit)
        {
            case Suit.Hearts:
                await ctx.MoveToAsync(X(0), Y(0.441));
                await ctx.BezierCurveToAsync(X(-0.7906), Y(-0.081), X(-0.6265), Y(-0.797), X(0), Y(-0.3645));
                await ctx.BezierCurveToAsync(X(0.6265), Y(-0.797), X(0.7906), Y(-0.081), X(0), Y(0.441));
                break;

            case Suit.Spades:
                await ctx.MoveToAsync(X(-0), Y(-0.5426));
                await ctx.BezierCurveToAsync(X(0.6925), Y(-0.0331), X(0.5488), Y(0.385), X(-0), Y(0.2021));
                await ctx.BezierCurveToAsync(X(-0.5488), Y(0.385), X(-0.6925), Y(-0.0331), X(-0), Y(-0.5426));
                await ctx.MoveToAsync(X(-0.2482), Y(0.5288));
                await ctx.QuadraticCurveToAsync(X(-0.0327), Y(0.385), X(-0), Y(0.1107));
                await ctx.QuadraticCurveToAsync(X(0.0327), Y(0.385), X(0.2482), Y(0.5288));
                break;

            case Suit.Diamonds:
                await ctx.MoveToAsync(X(0), Y(-0.5903));
                await ctx.LineToAsync(X(0.4235), Y(0));
                await ctx.LineToAsync(X(0), Y(0.5903));
                await ctx.LineToAsync(X(-0.4235), Y(0));
                break;

            case Suit.Clubs:
                await ctx.MoveToAsync(X(0.2311), Y(-0.2421));
                await ctx.ArcAsync(X(0), Y(-0.2421), 0.2311 * s, 0, Math.PI * 2, false);
                await ctx.MoveToAsync(X(-0.066), Y(0.1431));
                await ctx.ArcAsync(X(-0.2972), Y(0.1431), 0.2311 * s, 0, Math.PI * 2, false);
                await ctx.MoveToAsync(X(0.5283), Y(0.1431));
                await ctx.ArcAsync(X(0.2972), Y(0.1431), 0.2311 * s, 0, Math.PI * 2, false);
                await ctx.MoveToAsync(X(-0.2091), Y(0.4732));
                await ctx.QuadraticCurveToAsync(X(-0.0275), Y(0.3302), X(0), Y(0.055));
                await ctx.QuadraticCurveToAsync(X(0.0275), Y(0.3302), X(0.2091), Y(0.4732));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(suit), suit, null);
        }

        await ctx.ClosePathAsync();
        await ctx.FillAsync();
    }

    /// <summary>
    /// Draws text rotated half a turn about the given point. Costs a save and a restore, so
    /// it is used only where a real card is actually printed upside down.
    /// </summary>
    private async ValueTask DrawInverted(string text, double x, double y)
    {
        // Nothing but the transform is touched between the save and the restore, so the
        // context comes back holding exactly the styles the cache already claims it holds
        // and none of them need re-setting.
        await ctx.SaveAsync();
        await ctx.TranslateAsync(x, y);
        await ctx.RotateAsync((float)Math.PI);
        await ctx.FillTextAsync(text, 0, 0);
        await ctx.RestoreAsync();
    }

    private static string RankLabel(Rank rank) => (int)rank switch
    {
        1 => "A",
        11 => "J",
        12 => "Q",
        13 => "K",
        var n => n.ToString()
    };

    /// <summary>
    /// Outlines the stack a press here would pick up. One rounded rect over the whole run
    /// rather than one per card: the run moves as a unit, and a per-card outline would draw
    /// lines across the middle of something that is about to behave as a single object.
    /// </summary>
    private async ValueTask DrawHover(Board board, BoardLayout layout, Location loc, int index)
    {
        var pile = board.Pile(loc);
        if (index < 0 || index >= pile.Count)
        {
            return;
        }

        var first = layout.CardRect(loc, index);
        var last = layout.CardRect(loc, pile.Count - 1);
        var span = new Rect(first.X, first.Y, first.W, last.Y + last.H - first.Y);

        // Outline only. A translucent wash over the run was the other option and is
        // invisible where it matters - the cards it covers are already near-white.
        await RoundedPath(span, span.W * CornerRadius);
        await LineWidth(Math.Max(2, span.W * 0.03));
        await Stroke("#f0c05a");
        await ctx.StrokeAsync();
    }

    /// <summary>
    /// Lays down a rounded rectangle as the current path, ready to be filled or stroked or
    /// both. Not drawn here, because a card wants the same shape twice — once in white and
    /// once as its edge — and building it once is half the calls.
    /// </summary>
    private async ValueTask RoundedPath(Rect rect, double radius)
    {
        double r = Math.Min(radius, Math.Min(rect.W, rect.H) / 2);

        await ctx.BeginPathAsync();
        await ctx.MoveToAsync(rect.X + r, rect.Y);
        await ctx.ArcToAsync(rect.X + rect.W, rect.Y, rect.X + rect.W, rect.Y + rect.H, r);
        await ctx.ArcToAsync(rect.X + rect.W, rect.Y + rect.H, rect.X, rect.Y + rect.H, r);
        await ctx.ArcToAsync(rect.X, rect.Y + rect.H, rect.X, rect.Y, r);
        await ctx.ArcToAsync(rect.X, rect.Y, rect.X + rect.W, rect.Y, r);
        await ctx.ClosePathAsync();
    }

    private async ValueTask LineWidth(double value)
    {
        if (lineWidth == value)
        {
            return;
        }

        lineWidth = value;
        await ctx.SetLineWidthAsync((float)value);
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
