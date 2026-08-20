using BSolitaire.Game;
using Xunit;
using static BSolitaire.Tests.Positions;

namespace BSolitaire.Tests;

/// <summary>
/// The geometry. Nothing here draws anything — it works out where the cards are, and the hit
/// test has to agree with it, or the player presses one card and picks up another.
/// </summary>
public class LayoutTests
{
    public static TheoryData<double, double> Viewports => new()
    {
        { 1920, 1080 },  // desktop
        { 1280, 800 },
        { 834, 1112 },   // tablet, portrait
        { 390, 844 },    // phone, portrait
        { 320, 480 },    // about as small as a phone gets
        { 1200, 300 },   // a short, wide window
    };

    [Theory]
    [MemberData(nameof(Viewports))]
    public void EveryCardCanBeFoundWhereTheLayoutPutIt(double width, double height)
    {
        // The round trip that matters: ask where a card is, press there, and get that same
        // card back. Run over a whole dealt board, so every pile kind and depth is covered.
        var board = new Board();
        var layout = new BoardLayout(width, height);

        foreach (var kind in Board.AllKinds)
        {
            for (int pileIndex = 0; pileIndex < board.PileCountOf(kind); pileIndex++)
            {
                var loc = new Location(kind, pileIndex);
                var pile = board.Pile(loc);

                if (pile.Count == 0)
                {
                    continue;
                }

                // The top card is the one a press can always reach; the ones below it are
                // covered by whatever is fanned over them.
                var rect = layout.CardRect(loc, pile.Count - 1);
                double x = rect.X + rect.W / 2;
                double y = rect.Y + rect.H / 2;

                Assert.True(layout.TryHitTest(board, x, y, out var hit, out int index),
                    $"nothing at the centre of {kind} {pileIndex}");
                Assert.Equal(loc, hit);
                Assert.Equal(pile.Count - 1, index);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Viewports))]
    public void TheBoardFitsInsideTheWindow(double width, double height)
    {
        var board = new Board();
        var layout = new BoardLayout(width, height);

        foreach (var kind in Board.AllKinds)
        {
            for (int pileIndex = 0; pileIndex < board.PileCountOf(kind); pileIndex++)
            {
                var loc = new Location(kind, pileIndex);
                var rect = layout.CardRect(loc, 0);

                Assert.True(rect.X >= 0, $"{kind} {pileIndex} runs off the left");
                Assert.True(rect.X + rect.W <= width + 0.001, $"{kind} {pileIndex} runs off the right");
                Assert.True(rect.Y >= 0, $"{kind} {pileIndex} runs off the top");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Viewports))]
    public void CardsAreNeverSizedAwayToNothing(double width, double height)
    {
        var layout = new BoardLayout(width, height);

        Assert.True(layout.CardWidth > 0);
        Assert.True(layout.CardHeight > 0);
        Assert.True(layout.FanOffset > 0);
    }

    [Theory]
    [MemberData(nameof(Viewports))]
    public void AFannedColumnLeavesEveryCardShowing(double width, double height)
    {
        // A card underneath has to keep a strip of itself visible, or it stops being
        // grabbable — especially with a fingertip. A compact board fans wider: it is short
        // of width and long on height, so the spare room goes into the strip.
        var layout = new BoardLayout(width, height);

        Assert.True(layout.FanOffset >= layout.CardHeight * 0.12);
        Assert.True(layout.FanOffset <= layout.CardHeight * 0.5 + 0.001);
    }

    [Theory]
    [MemberData(nameof(Viewports))]
    public void TheUndoButtonNeverSitsOnACard(double width, double height)
    {
        // Same rule as the mute toggle beside it: it takes the press before the felt does,
        // so anything underneath it could not be picked up at all.
        var board = new Board();
        var layout = new BoardLayout(width, height);

        foreach (var (x, y) in Corners(layout.UndoButton))
        {
            Assert.False(layout.TryHitTest(board, x, y, out _, out int index) && index >= 0,
                $"the undo button covers a card at {width}x{height}");
        }
    }

    [Theory]
    [MemberData(nameof(Viewports))]
    public void TheTwoCornerButtonsDoNotOverlap(double width, double height)
    {
        var layout = new BoardLayout(width, height);
        var undo = layout.UndoButton;
        var mute = layout.MuteButton;

        Assert.True(undo.X + undo.W <= mute.X + 0.001, $"undo runs into mute at {width}x{height}");
        Assert.True(undo.X >= 0, $"undo runs off the left at {width}x{height}");
    }

    [Fact]
    public void APhoneGetsABiggerCardThanTheDesktopSpacingWouldAllow()
    {
        // The whole of the mobile card-size problem in one assertion: seven columns and
        // their gutters have to fit across the window, so on a phone the gutters are what
        // the card is competing with.
        var phone = new BoardLayout(390, 844);

        Assert.True(phone.Compact);
        Assert.True(phone.CardWidth > 390 / (7 + 8 * 0.14),
            $"a compact board is no roomier than a desktop one: {phone.CardWidth:F1}px");
    }

    [Fact]
    public void AWindowGetsTheDesktopLayout()
    {
        Assert.False(new BoardLayout(1280, 800).Compact);
        Assert.False(new BoardLayout(834, 1112).Compact);
    }

    [Fact]
    public void AWiderWindowNeverMeansASmallerCard()
    {
        // The bug this is here for: a phone board spends almost nothing on gutters and a
        // desktop board spends an eighth of its width on them, so switching between the two
        // at a threshold made the card jump *down* nine per cent as the window got wider.
        // Dragging a window wider must never take card away.
        var layout = new BoardLayout(200, 900);
        double previous = layout.CardWidth;

        for (double width = 210; width <= 1600; width += 5)
        {
            layout.Resize(width, 900);

            Assert.True(layout.CardWidth >= previous - 0.001,
                $"widening to {width} shrank the card from {previous:F2} to {layout.CardWidth:F2}");

            previous = layout.CardWidth;
        }
    }

    [Fact]
    public void TheFanNeverJumpsAsAWindowIsResized()
    {
        // Same argument, about the other thing that used to switch at a threshold. A step
        // here is a board that visibly reflows in the middle of a drag.
        var layout = new BoardLayout(300, 900);
        double previous = layout.FanOffset;

        for (double width = 305; width <= 1600; width += 5)
        {
            layout.Resize(width, 900);

            Assert.True(Math.Abs(layout.FanOffset - previous) < layout.CardHeight * 0.05,
                $"the fan jumped from {previous:F2} to {layout.FanOffset:F2} at width {width}");

            previous = layout.FanOffset;
        }
    }

    [Fact]
    public void ASmallCardGetsTheSmallCardFaceWhereverItCameFrom()
    {
        // The face follows the card, not the viewport. A desktop window dragged narrow has
        // exactly the same problem a phone does, and used to get the opposite answer.
        Assert.True(new BoardLayout(390, 844).SmallCards);
        Assert.True(new BoardLayout(320, 480).SmallCards);
        Assert.False(new BoardLayout(1280, 800).SmallCards);
        Assert.False(new BoardLayout(834, 1112).SmallCards);

        // ...and it is genuinely a question about the card.
        var layout = new BoardLayout(1280, 800);
        Assert.Equal(layout.CardWidth < 64, layout.SmallCards);
    }

    [Theory]
    [MemberData(nameof(Viewports))]
    public void TheMuteButtonNeverSitsOnACard(double width, double height)
    {
        // The button takes a press before the felt does, so anything under it is unpressable.
        // A drop still lands correctly — that is hit-tested separately — but a card beneath
        // this could not be picked up at all.
        var board = new Board();
        var layout = new BoardLayout(width, height);
        var button = layout.MuteButton;

        foreach (var (x, y) in Corners(button))
        {
            Assert.False(layout.TryHitTest(board, x, y, out var loc, out int index) && index >= 0,
                $"the mute button covers a card at {width}x{height}");
        }
    }

    [Theory]
    [MemberData(nameof(Viewports))]
    public void TheFastForwardButtonNeverSitsOnACard(double width, double height)
    {
        var board = new Board();
        var layout = new BoardLayout(width, height);

        foreach (var (x, y) in Corners(layout.FastForwardButton))
        {
            Assert.False(layout.TryHitTest(board, x, y, out _, out int index) && index >= 0,
                $"the finish button covers a card at {width}x{height}");
        }
    }

    [Theory]
    [MemberData(nameof(Viewports))]
    public void TheBannersButtonIsInsideTheBanner(double width, double height)
    {
        var layout = new BoardLayout(width, height);
        var banner = layout.Banner;
        var button = layout.NewGameButton;

        Assert.True(button.X >= banner.X);
        Assert.True(button.Y >= banner.Y);
        Assert.True(button.X + button.W <= banner.X + banner.W + 0.001);
        Assert.True(button.Y + button.H <= banner.Y + banner.H + 0.001);
    }

    [Fact]
    public void ResizingMovesTheCardsWithTheWindow()
    {
        var layout = new BoardLayout(1200, 800);
        var before = layout.CardRect(Tableau(3), 0);

        layout.Resize(600, 800);
        var after = layout.CardRect(Tableau(3), 0);

        Assert.NotEqual(before.X, after.X);
        Assert.True(after.X + after.W <= 600.001);
    }

    [Fact]
    public void AnEmptySlotIsWhereTheFirstCardWouldGo()
    {
        var layout = new BoardLayout(1200, 800);

        var slot = layout.EmptySlot(Tableau(2));
        var first = layout.CardRect(Tableau(2), 0);

        Assert.Equal(first.X, slot.X, 3);
        Assert.Equal(first.Y, slot.Y, 3);
    }

    [Fact]
    public void AnEmptyPileStillTakesAPress()
    {
        // Dropping a king on an empty column means pressing where no card is.
        var board = Empty();
        var layout = new BoardLayout(1200, 800);
        var slot = layout.EmptySlot(Tableau(4));

        Assert.True(layout.TryHitTest(board, slot.X + slot.W / 2, slot.Y + slot.H / 2,
            out var loc, out int index));
        Assert.Equal(Tableau(4), loc);
        Assert.Equal(-1, index); // nothing there to pick up
    }

    [Theory]
    [MemberData(nameof(Viewports))]
    public void APileRegionCoversEveryCardInIt(double width, double height)
    {
        // The region is the strip of board that gets cleared before a pile is drawn again.
        // Anything the pile put on screen that falls outside it survives the repaint — which
        // is how a drop-target ring left a pair of gold lines down the felt.
        var board = new Board();
        var layout = new BoardLayout(width, height);

        foreach (var kind in Board.AllKinds)
        {
            for (int pileIndex = 0; pileIndex < board.PileCountOf(kind); pileIndex++)
            {
                var loc = new Location(kind, pileIndex);
                var region = layout.PileRegion(loc);
                var pile = board.Pile(loc);

                for (int card = 0; card < Math.Max(1, pile.Count); card++)
                {
                    var rect = layout.CardRect(loc, card);

                    Assert.True(rect.X >= region.X, $"{kind} {pileIndex} card {card} juts out to the left");
                    Assert.True(rect.Y >= region.Y, $"{kind} {pileIndex} card {card} juts out above");
                    Assert.True(rect.X + rect.W <= region.X + region.W + 0.001,
                        $"{kind} {pileIndex} card {card} juts out to the right");
                    Assert.True(rect.Y + rect.H <= region.Y + region.H + 0.001,
                        $"{kind} {pileIndex} card {card} juts out below");
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Viewports))]
    public void ATableauRegionNeverReachesItsNeighbour(double width, double height)
    {
        // The other half of the same rule. The region reaches a little past the slot so that
        // a card's edge is cleared with it — but a region that reached the next column would
        // clear part of a pile nobody is about to draw again, and take a bite out of it.
        var layout = new BoardLayout(width, height);

        for (int column = 0; column + 1 < 7; column++)
        {
            var left = layout.PileRegion(Tableau(column));
            var right = layout.PileRegion(Tableau(column + 1));

            Assert.True(left.X + left.W <= right.X + 0.001,
                $"columns {column} and {column + 1} share board at {width}x{height}");
        }
    }

    private static IEnumerable<(double X, double Y)> Corners(Rect rect)
    {
        yield return (rect.X + 0.5, rect.Y + 0.5);
        yield return (rect.X + rect.W - 0.5, rect.Y + 0.5);
        yield return (rect.X + 0.5, rect.Y + rect.H - 0.5);
        yield return (rect.X + rect.W - 0.5, rect.Y + rect.H - 0.5);
        yield return (rect.X + rect.W / 2, rect.Y + rect.H / 2);
    }

    [Fact]
    public void APressOnBareFeltHitsNothing()
    {
        var board = new Board();
        var layout = new BoardLayout(1200, 800);

        Assert.False(layout.TryHitTest(board, 1199, 1, out _, out _));
    }
}
