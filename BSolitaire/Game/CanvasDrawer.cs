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
    private readonly Canvas2DContext heldCanvas;
    private readonly ElementReference heldElement;
    private readonly Canvas2DContext atlasCanvas;
    private readonly ElementReference atlasElement;
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

    /// <summary>
    /// Whether cards are being printed small. A card fifty pixels wide with a face scaled
    /// down to match is a card nobody can read: the pips become dots and the index becomes a
    /// smudge. So below that size the deck changes rather than shrinks — a jumbo index and
    /// one large suit, which is exactly what a real deck for a small hand does. Keyed to the
    /// card rather than to the screen, so a desktop window dragged narrow gets the same deck
    /// a phone does at the same card size. The layout decides; this only reads it.
    /// </summary>
    private bool compact;

    /// <summary>The game being drawn this frame. Held because the pile-level drawing has to
    /// ask which of its cards are currently in the air somewhere else.</summary>
    private Solitaire? current;

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

    // The same face, printed for a small card. The index grows by half and moves in off the
    // corner, the far index goes altogether — there is no room for two and the near one is
    // what a fanned column shows — and the pips become a single large suit, because ten
    // seven-per-cent marks on a fifty-pixel card is a texture rather than a count.
    private const double CompactIndexAxis = 0.21;
    private const double CompactRankRow = 0.15;
    private const double CompactRankText = 0.32;
    private const double CompactCornerSuitSize = 0.13;
    private const double CompactCentreSuit = 0.36;

    /// <summary>How much bigger a card is drawn while it is being carried.</summary>
    private const double DragScale = 1.06;

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

    // What the held stack was last drawn for. While these still describe what is being
    // carried, the picture of it is still right and only its position has changed — which
    // costs one call instead of one per card, and a run of seven used to cost seven times as
    // much to carry as one. The lowest card is compared by identity rather than by where it
    // came from: a pile and an index name a different card after every move, and reusing a
    // picture of the card that used to be there is exactly the bug that invites.
    private Card? heldBottom;
    private int heldCount = -1;
    private double heldCardWidth = -1;
    private double heldFan = -1;

    /// <summary>The piles the last drawn frame offered as drop targets, so the rings can be
    /// painted into the cached board and taken out again when the stack is put down.</summary>
    private readonly List<Location> cachedTargets = new();

    /// <summary>What the corner controls were last painted saying, so they are only painted
    /// again when one of them has something else to say.</summary>
    private bool cachedMuted;
    private bool cachedCanUndo;
    private string cachedScore = "";

    // ---- The card atlas -------------------------------------------------------------
    //
    // A printed face is a hundred-odd canvas calls, and every one of them is a hop into JS.
    // That is affordable when a card is drawn because the position changed, and ruinous when
    // it is drawn because the card is half a centimetre further along than it was last
    // frame: a deal has nine cards in the air at once, and was costing 155ms a frame.
    //
    // So each of the fifty-two faces, and the back, is drawn once into a grid of card-sized
    // cells and blitted from there. A moving card costs one call instead of a hundred, and
    // so does a settled one — repainting a column got cheaper for free.

    /// <summary>Faces plus the one back.</summary>
    private const int SlotCount = 53;

    /// <summary>The back's cell, after the fifty-two faces.</summary>
    private const int BackSlot = 52;

    /// <summary>Cells across the atlas. Thirteen ranks and the back make fourteen, which
    /// keeps the grid four rows deep and both dimensions well inside what a mobile browser
    /// will allocate — a single strip of fifty-three cards would be too tall for one.</summary>
    private const int AtlasColumns = 14;

    private const int AtlasRows = 4;

    /// <summary>
    /// Slack around each cell, in CSS pixels. A card's edge is stroked along its own
    /// boundary, so half the line lies outside the card — and a cell cut to the card exactly
    /// loses that half twice over: it falls outside the rectangle that gets blitted, and the
    /// neighbouring cell clears it away when its own face is drawn. Cards came out with no
    /// outline at all. The padding is blitted along with the face, so what lands on the board
    /// is what the card would have looked like drawn straight onto it.
    /// </summary>
    private const double AtlasPad = 2;

    /// <summary>
    /// How many faces are drawn into the atlas per frame. The whole deck at once is a
    /// visible stall on every resize, and nothing needs the whole deck at once — the cells
    /// that are not ready yet are drawn the old way, so the board is right from the first
    /// frame and merely gets cheaper over the next few.
    /// </summary>
    private const int WarmPerFrame = 6;

    private readonly bool[] atlasReady = new bool[SlotCount];

    /// <summary>Device pixels per CSS pixel. The one place the drawer needs it: a cell of
    /// the atlas is named in the atlas canvas's own backing pixels.</summary>
    private double pixelRatio = 1;

    /// <summary>False until the host has sized the atlas canvas for the current card. Until
    /// then there is nowhere to put a face.</summary>
    private bool atlasSized;

    /// <summary>One cell of the atlas in backing pixels. Recomputed each pass.</summary>
    private double cellWidth;
    private double cellHeight;

    /// <summary>The card the atlas was drawn for, in CSS pixels. A card is not always blitted
    /// at its own size — held cards are a shade larger, turning ones are squeezed — so the
    /// padding has to be scaled by however much the card it belongs to was.</summary>
    private double cardWidth = 1;
    private double cardHeight = 1;

    /// <summary>
    /// Whether there are still faces to draw into the atlas. A solitaire board is idle
    /// almost all the time and the host skips drawing entirely while it is — so without
    /// somewhere to ask, the atlas would only ever fill up during the very moments it is
    /// meant to be making cheaper. The host fills it in the gaps instead.
    /// </summary>
    public bool AtlasIncomplete
    {
        get
        {
            if (!atlasSized)
            {
                return false;
            }

            foreach (bool ready in atlasReady)
            {
                if (!ready)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Draws a few more faces into the atlas and nothing else. For frames the board
    /// had no use for.</summary>
    public ValueTask WarmUp(BoardLayout layout) => WarmAtlas(layout);

    /// <summary>What the atlas canvas needs to be, in CSS pixels, for this card size.</summary>
    public (double Width, double Height) AtlasSize(BoardLayout layout) =>
        ((layout.CardWidth + 2 * AtlasPad) * AtlasColumns,
         (layout.CardHeight + 2 * AtlasPad) * AtlasRows);

    /// <summary>
    /// The atlas canvas has been resized, so everything in it was drawn for a card that no
    /// longer exists. Thrown away rather than rescaled: a rescaled face is a blurred one,
    /// and it refills over the next few frames anyway.
    /// </summary>
    public void AtlasResized(double dpr)
    {
        pixelRatio = dpr;
        atlasSized = true;
        Array.Clear(atlasReady);

        // The held stack was drawn from the old faces too.
        heldBottom = null;
    }

    public CanvasDrawer(
        Canvas2DContext board,
        Canvas2DContext cache,
        ElementReference cacheElement,
        Canvas2DContext held,
        ElementReference heldElement,
        Canvas2DContext atlas,
        ElementReference atlasElement)
    {
        this.board = board;
        this.cache = cache;
        this.cacheElement = cacheElement;
        this.heldCanvas = held;
        this.heldElement = heldElement;
        this.atlasCanvas = atlas;
        this.atlasElement = atlasElement;
        ctx = board;
    }

    public async ValueTask Draw(Solitaire game)
    {
        Board board = game.Board;
        BoardLayout layout = game.Layout;
        DragState? drag = game.Drag;
        current = game;

        double startedAt = clock.Elapsed.TotalMilliseconds;

        // Before any pass, so nothing switches canvases mid-batch: a few more faces go into
        // the atlas, and everything drawn this frame can use whatever is in it.
        await WarmAtlas(layout);

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

        // The corner controls and the score line are as static as the felt is, and they were
        // being redrawn every frame — the speaker alone is two dozen strokes and arcs. They
        // go into the cached board with everything else, and are only redrawn when one of
        // them actually says something different.
        bool chromeChanged =
            cachedMuted != game.Muted ||
            cachedCanUndo != game.CanUndo ||
            cachedScore != game.Score.Summary;

        cachedMuted = game.Muted;
        cachedCanUndo = game.CanUndo;
        cachedScore = game.Score.Summary;

        repaint.Clear();

        if (!cacheValid || sizeChanged || board.AllDirty)
        {
            ctx = cache;
            await BeginPass(layout);
            await DrawStatic(board, layout, drag);
            await DrawChrome(layout, game);
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
            // The rings under a held stack are as static as the piles are: they are decided
            // when the stack leaves its pile and cannot change until it is put down. Painting
            // them into the cache means they cost two repaints per drag rather than eleven
            // rounded rectangles on every frame of it.
            if (!SameTargets(game.DropTargets))
            {
                foreach (var was in cachedTargets)
                {
                    if (!repaint.Contains(was))
                    {
                        repaint.Add(was);
                    }
                }

                foreach (var now in game.DropTargets)
                {
                    if (!repaint.Contains(now))
                    {
                        repaint.Add(now);
                    }
                }
            }

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

            if (repaint.Count > 0 || chromeChanged)
            {
                ctx = cache;
                await BeginPass(layout);

                foreach (var loc in repaint)
                {
                    await DrawPileRegion(board, layout, loc, drag);
                }

                // A tableau column runs to the bottom of the board, so repainting one can
                // take the corner controls with it. Cheaper to put them back every time the
                // cache is touched than to work out whether this particular column reached
                // them.
                await DrawChrome(layout, game);
                await ctx.EndBatchAsync();
            }
        }

        // Before anything is blitted: if the stack under the pointer is not the one already
        // drawn, draw it. This is the only frame of a drag that costs a card to draw.
        if (drag != null)
        {
            await RenderHeld(drag, layout);
        }

        cachedDragSource = drag?.From;

        cachedTargets.Clear();
        cachedTargets.AddRange(game.DropTargets);

        // The winning cascade is painted into the cached board rather than over it, so every
        // frame of it stays where it fell. That accumulation is the whole effect — a card
        // that leaves no trail is just a card falling off a table.
        if (game.Falling.Count > 0)
        {
            ctx = cache;
            await BeginPass(layout);

            foreach (var falling in game.Falling)
            {
                await DrawCard(
                    falling.Card,
                    new Rect(falling.X, falling.Y, layout.CardWidth, layout.CardHeight),
                    covered: false);
            }

            await ctx.EndBatchAsync();
        }

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

        // Cards between piles. Under the held stack, which is the one thing the player is
        // steering and so belongs on top of everything.
        foreach (var flight in game.InFlight)
        {
            await DrawCard(flight.Card, flight.Rect, covered: false, faceUp: flight.FaceUp);
        }

        if (drag != null)
        {
            // Lift the held stack off the felt so it reads as above the board.
            await ctx.SetShadowColorAsync("rgba(0, 0, 0, 0.45)");
            await ctx.SetShadowBlurAsync(12);
            await ctx.SetShadowOffsetYAsync(6);

            // A shade larger than the board it is being moved across, about its own centre.
            // Held things are nearer, and the drop is hit-tested from that same centre, so
            // growing the card cannot move where it lands.
            double grow = layout.CardWidth * (DragScale - 1) / 2;

            // One call, whatever the stack. The cards themselves were drawn into their own
            // canvas when they were picked up — see RenderHeld — and the whole of it is
            // blitted with the stack sitting at its top-left corner, so moving the stack is
            // a matter of where the image goes rather than of drawing the cards again.
            await ctx.DrawImageAsync(
                heldElement,
                drag.X - drag.OffsetX - grow,
                drag.Y - drag.OffsetY - grow,
                layout.Width * DragScale,
                layout.Height * DragScale);

            await ctx.SetShadowColorAsync("rgba(0, 0, 0, 0)");
            await ctx.SetShadowBlurAsync(0);
            await ctx.SetShadowOffsetYAsync(0);
        }

        if (game.ShowBanner)
        {
            // The panel dims the whole board, cached controls and all, so they are drawn
            // again on top of it — turning the fanfare off is exactly what a player wants to
            // do at the moment a game ends.
            await DrawBanner(layout, game.State);
            await DrawChrome(layout, game);
        }

        if (game.Error != null)
        {
            await Fill("#ff0000");
            await Font("bold 20px sans-serif");
            await ctx.FillTextAsync($"Error: {game.Error}", 24, 80);
        }

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
        compact = layout.SmallCards;

        // One cell of the atlas, in that canvas's own backing pixels — which is what a
        // source rectangle is measured in, unlike everything else the drawer says.
        cellWidth = (layout.CardWidth + 2 * AtlasPad) * pixelRatio;
        cellHeight = (layout.CardHeight + 2 * AtlasPad) * pixelRatio;
        cardWidth = layout.CardWidth;
        cardHeight = layout.CardHeight;
        double rankText = layout.CardHeight * (compact ? CompactRankText : RankText);
        rankFont = $"bold {rankText:F0}px sans-serif";
        tenFont = $"bold {rankText * TenScale:F0}px sans-serif";
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
        await DrawDropTargetIfOffered(board, layout, loc);
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
                var loc = new Location(kind, pileIndex);
                await DrawPile(board, layout, loc, drag);
                await DrawDropTargetIfOffered(board, layout, loc);
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

        // A card on its way here is painted in the air instead, so the pile stops short of
        // it. Same idea as the hole a drag leaves behind, and for the same reason: a card
        // has to be in exactly one place on screen.
        if (current is { } game)
        {
            visibleCount = Math.Min(visibleCount, game.HiddenFrom(location));
        }

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
    /// The corner controls and the score line: everything that is on the board without being
    /// part of the position. Painted into the cached board, so a frame that is only moving a
    /// card does not repaint a speaker.
    /// </summary>
    private async ValueTask DrawChrome(BoardLayout layout, Solitaire game)
    {
        await DrawScore(layout, game.Score);
        await DrawMute(layout, game.Muted);

        if (game.CanUndo)
        {
            await DrawUndo(layout);
        }
    }

    /// <summary>
    /// The mute toggle: a speaker in the bottom-right corner, above the score line.
    /// Drawn on the live pass rather than into the cached board, because it is a control
    /// rather than part of the position — and it has to survive the banner dimming the felt,
    /// which is exactly when a player wants to turn the fanfare off.
    /// </summary>
    private async ValueTask DrawMute(BoardLayout layout, bool muted)
    {
        var button = layout.MuteButton;
        double size = button.W;
        double cy = button.Y + size / 2;

        await RoundedPath(button, size * 0.22);
        await Fill("#12351f");
        await ctx.FillAsync();
        await LineWidth(1);
        await Stroke(muted ? "#6f8a78" : "#cfe0d2");
        await ctx.StrokeAsync();

        string ink = muted ? "#8fa697" : "#f4f8f4";

        // The cone, drawn as one path: the flat back, then out to the mouth and around.
        await ctx.BeginPathAsync();
        await ctx.MoveToAsync(button.X + size * 0.24, cy - size * 0.10);
        await ctx.LineToAsync(button.X + size * 0.36, cy - size * 0.10);
        await ctx.LineToAsync(button.X + size * 0.52, cy - size * 0.26);
        await ctx.LineToAsync(button.X + size * 0.52, cy + size * 0.26);
        await ctx.LineToAsync(button.X + size * 0.36, cy + size * 0.10);
        await ctx.LineToAsync(button.X + size * 0.24, cy + size * 0.10);
        await ctx.ClosePathAsync();
        await Fill(ink);
        await ctx.FillAsync();

        await LineWidth(Math.Max(1.5, size * 0.055));
        await Stroke(ink);

        if (muted)
        {
            // A slash rather than dropping the waves: an icon that only differs by what is
            // missing is one a player has to have seen the other state of to read.
            await ctx.BeginPathAsync();
            await ctx.MoveToAsync(button.X + size * 0.62, cy - size * 0.16);
            await ctx.LineToAsync(button.X + size * 0.82, cy + size * 0.16);
            await ctx.StrokeAsync();
            return;
        }

        foreach (double radius in new[] { size * 0.14, size * 0.24 })
        {
            await ctx.BeginPathAsync();
            await ctx.ArcAsync(button.X + size * 0.52, cy, radius, -0.9, 0.9);
            await ctx.StrokeAsync();
        }
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
    /// else. That is most of the cards on a dealt board, and skipping the rest is what makes
    /// a drawn-out pip layout affordable at all.
    /// </summary>
    /// <param name="faceUp">
    /// Which side to show, when that is not the side the card is actually lying. Only an
    /// animation asks for this: a card turning over has to be drawn from its old face for
    /// the first half of the turn, and the board has already recorded the new one.
    /// </param>
    private async ValueTask DrawCard(Card card, Rect rect, bool covered, bool? faceUp = null)
    {
        bool up = faceUp ?? card.IsFaceUp;
        int slot = up ? (int)card.Suit * 13 + (int)card.Rank - 1 : BackSlot;

        if (atlasReady[slot])
        {
            // One call. The cell holds the whole face, so a covered card is drawn complete
            // and then covered by the card above it rather than being drawn short.
            //
            // That is a visible change, and a deliberate one. Leaving most of a covered card
            // out was a saving that assumed a tight fan, where the strip on show is the rank
            // and nothing else. The fan is half a card now, so the strip reaches the suit
            // under the index and the lattice on a back — and drawing those was the whole
            // point of widening it. What used to be the cheap path is now the wrong picture.
            // The whole cell, padding and all, laid over the rectangle the card was asked
            // for and the same slack again around it.
            double across = rect.W / cardWidth;
            double down = rect.H / cardHeight;

            await ctx.DrawImageAsync(
                atlasElement,
                slot % AtlasColumns * cellWidth,
                slot / AtlasColumns * cellHeight,
                cellWidth,
                cellHeight,
                rect.X - AtlasPad * across,
                rect.Y - AtlasPad * down,
                rect.W + 2 * AtlasPad * across,
                rect.H + 2 * AtlasPad * down);

            return;
        }

        await DrawCardDirect(card, rect, covered, up);
    }

    /// <summary>
    /// Draws one card the long way, mark by mark. Used for the faces the atlas does not hold
    /// yet, and to fill the atlas itself.
    /// </summary>
    private async ValueTask DrawCardDirect(Card card, Rect rect, bool covered, bool up)
    {
        await RoundedPath(rect, rect.W * CornerRadius);
        await LineWidth(1);
        await Fill(Paper);
        await ctx.FillAsync();
        await Stroke(CardEdge);
        await ctx.StrokeAsync();

        if (!up)
        {
            await DrawBack(rect, covered);
            return;
        }

        string colour = card.IsRed ? SuitRed : SuitBlack;
        string rank = RankLabel(card.Rank);

        await Fill(colour);
        await Font(rank.Length > 1 ? tenFont : rankFont);
        await Align("center");

        double indexAxis = compact ? CompactIndexAxis : IndexAxis;
        double rankRow = compact ? CompactRankRow : RankRow;
        double nearAxis = rect.X + rect.W * indexAxis;
        await ctx.FillTextAsync(rank, nearAxis, rect.Y + rect.H * rankRow);

        if (covered)
        {
            return;
        }

        double cornerSize = rect.H * (compact ? CompactCornerSuitSize : CornerSuitSize);
        double farAxis = rect.X + rect.W * (1 - indexAxis);

        if (!compact)
        {
            // The far index is the near one turned through half a turn, exactly as it is
            // printed, so the card reads the same whichever way up you look at it. A small
            // card gives it up: two of anything is one too many across fifty pixels, and a
            // solitaire board is only ever read from one side anyway.
            await DrawInverted(rank, farAxis, rect.Y + rect.H * (1 - rankRow));
            await DrawSuit(card.Suit, farAxis, rect.Y + rect.H * (1 - SuitRow), cornerSize, true);
        }

        await DrawSuit(card.Suit, nearAxis, rect.Y + rect.H * (compact ? 0.34 : SuitRow), cornerSize, false);

        int value = (int)card.Rank;

        if (value >= 11)
        {
            await DrawCourt(rect, card.Suit, rank, colour);
            return;
        }

        if (compact && value > 1)
        {
            // One large suit rather than a count of small ones. The rank is already set in
            // the index at nearly a third of the card, so nothing is lost by not printing it
            // twice — and what is gained is a card whose suit is legible across the room.
            await DrawSuit(
                card.Suit,
                rect.X + rect.W * 0.58,
                rect.Y + rect.H * 0.62,
                rect.H * CompactCentreSuit,
                false);
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
    /// Puts a few more of the deck's faces into the atlas. Spread over frames rather than
    /// done in one go: the whole deck is five thousand canvas calls, which is a visible stall
    /// every time a window is resized — and nothing needs the whole deck at once, because a
    /// face the atlas does not hold yet is simply drawn the old way.
    /// </summary>
    private async ValueTask WarmAtlas(BoardLayout layout)
    {
        if (!atlasSized)
        {
            return;
        }

        int drawn = 0;
        var previous = ctx;
        bool started = false;

        for (int slot = 0; slot < SlotCount && drawn < WarmPerFrame; slot++)
        {
            if (atlasReady[slot])
            {
                continue;
            }

            if (!started)
            {
                ctx = atlasCanvas;
                await BeginPass(layout);
                started = true;
            }

            await RenderSlot(slot, layout);
            atlasReady[slot] = true;
            drawn++;
        }

        if (started)
        {
            await ctx.EndBatchAsync();
            ctx = previous;
        }
    }

    /// <summary>Draws one face into its cell. The cell is cleared first: a card is a rounded
    /// rectangle, so its corners are transparent and whatever was there would show.</summary>
    private async ValueTask RenderSlot(int slot, BoardLayout layout)
    {
        double width = layout.CardWidth + 2 * AtlasPad;
        double height = layout.CardHeight + 2 * AtlasPad;

        var cell = new Rect(
            slot % AtlasColumns * width,
            slot / AtlasColumns * height,
            width,
            height);

        await ctx.ClearRectAsync(cell.X, cell.Y, cell.W, cell.H);

        var card = slot == BackSlot
            ? new Card(Suit.Spades, Rank.Ace)
            : new Card((Suit)(slot / 13), (Rank)(slot % 13 + 1));

        // Inset by the padding, so the edge it strokes along its own boundary has somewhere
        // to go rather than falling off the cell.
        var face = new Rect(
            cell.X + AtlasPad,
            cell.Y + AtlasPad,
            layout.CardWidth,
            layout.CardHeight);

        await DrawCardDirect(card, face, covered: false, up: slot != BackSlot);
    }

    /// <summary>
    /// Draws the held stack into its own canvas, with the lowest card at the top-left corner
    /// and everything else transparent. Only when what is held has changed: a stack keeps the
    /// same cards, the same order, and the same size for the whole of a drag, so this runs
    /// once per pick-up and the frames in between are a single blit.
    /// </summary>
    private async ValueTask RenderHeld(DragState drag, BoardLayout layout)
    {
        if (ReferenceEquals(heldBottom, drag.Cards[0]) &&
            heldCount == drag.Cards.Count &&
            heldCardWidth == layout.CardWidth &&
            heldFan == layout.FanOffset)
        {
            return;
        }

        heldBottom = drag.Cards[0];
        heldCount = drag.Cards.Count;
        heldCardWidth = layout.CardWidth;
        heldFan = layout.FanOffset;

        var previous = ctx;
        ctx = heldCanvas;
        await BeginPass(layout);

        // Transparent, not felt-coloured: this image is laid over the board, and anything
        // opaque around the cards would take a bite out of it.
        await ctx.ClearRectAsync(0, 0, layout.Width, layout.Height);

        for (int i = 0; i < drag.Cards.Count; i++)
        {
            var rect = new Rect(0, i * layout.FanOffset, layout.CardWidth, layout.CardHeight);
            await DrawCard(drag.Cards[i], rect, i < drag.Cards.Count - 1);
        }

        await ctx.EndBatchAsync();
        ctx = previous;
    }

    /// <summary>Whether the offered drop targets are the ones already painted into the
    /// cached board. Order is decided by the same walk every time, so this can compare
    /// position by position.</summary>
    private bool SameTargets(IReadOnlyList<Location> targets)
    {
        if (targets.Count != cachedTargets.Count)
        {
            return false;
        }

        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] != cachedTargets[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Rings this pile if it is one of the ones being offered. Called as each pile
    /// is painted into the cache, so the ring lands on top of the pile's own cards.</summary>
    private async ValueTask DrawDropTargetIfOffered(Board board, BoardLayout layout, Location loc)
    {
        if (current is { } game && game.DropTargets.Contains(loc))
        {
            await DrawDropTarget(board, layout, loc);
        }
    }

    /// <summary>
    /// Rings a pile that would take the stack being carried. Drawn under the held cards, in
    /// the same gold the hover outline uses — it is the same statement, made about a
    /// destination rather than a source.
    /// </summary>
    private async ValueTask DrawDropTarget(Board board, BoardLayout layout, Location loc)
    {
        var pile = board.Pile(loc);
        var card = pile.Count > 0 ? layout.CardRect(loc, pile.Count - 1) : layout.EmptySlot(loc);
        double width = Math.Max(2, card.W * 0.035);

        // Set inside the card rather than along it. A stroke straddles the path it follows,
        // so a ring on the boundary puts half its width outside the card — and outside the
        // strip of board that gets cleared when this pile is repainted, which left a pair of
        // gold lines down the felt every time a stack was put down.
        var ring = new Rect(
            card.X + width / 2,
            card.Y + width / 2,
            card.W - width,
            card.H - width);

        await RoundedPath(ring, ring.W * CornerRadius);
        await Fill("rgba(240, 192, 90, 0.20)");
        await ctx.FillAsync();
        await LineWidth(width);
        await Stroke("#f0c05a");
        await ctx.StrokeAsync();
    }

    /// <summary>
    /// The undo button: an arrow curling back on itself, beside the mute toggle. Drawn only
    /// while there is something to undo, and hit-tested on the same condition — a button
    /// that is not there must not be pressable.
    /// </summary>
    private async ValueTask DrawUndo(BoardLayout layout)
    {
        var button = layout.UndoButton;
        double size = button.W;
        double cx = button.X + size / 2;
        double cy = button.Y + size / 2;

        await RoundedPath(button, size * 0.22);
        await Fill("#12351f");
        await ctx.FillAsync();
        await LineWidth(1);
        await Stroke("#cfe0d2");
        await ctx.StrokeAsync();

        // The glyph rather than a hand-drawn arrow. A circular arrow with a head on it is
        // half a dozen strokes and a triangle, and at the size a thumb wants the button to
        // be, every one of those is two or three pixels — it came out as a bare curve. The
        // finish button already leans on ▶▶ for the same reason.
        await Fill("#f4f8f4");
        await Font($"{size * 0.62:F0}px sans-serif");
        await Align("center");
        await ctx.FillTextAsync("↺", cx, cy + size * 0.02);
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
