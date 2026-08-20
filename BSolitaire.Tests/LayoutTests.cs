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
        // grabbable — especially with a fingertip.
        var layout = new BoardLayout(width, height);

        Assert.True(layout.FanOffset >= layout.CardHeight * 0.12);
        Assert.True(layout.FanOffset <= layout.CardHeight * 0.28);
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
