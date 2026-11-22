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
    public void TheBannersThreeActionsFitSideBySide(double width, double height)
    {
        // Three ways out of a stuck deal, at every size the board is ever drawn at. They
        // are laid out the same whatever is on offer, because geometry that moved with the
        // state of the game is geometry the hit test and the drawer could disagree about.
        var layout = new BoardLayout(width, height);

        var buttons = new[] { layout.BackUpBannerButton, layout.ReplayBannerButton, layout.NewGameButton };

        for (int i = 0; i < buttons.Length; i++)
        {
            Assert.True(buttons[i].W > 0 && buttons[i].H > 0, $"button {i} has no size");

            Assert.True(
                buttons[i].X >= layout.Banner.X - 0.001 &&
                buttons[i].X + buttons[i].W <= layout.Banner.X + layout.Banner.W + 0.001,
                $"button {i} runs outside the panel");

            Assert.True(
                buttons[i].Y >= layout.Banner.Y &&
                buttons[i].Y + buttons[i].H <= layout.Banner.Y + layout.Banner.H + 0.001,
                $"button {i} runs outside the panel");

            if (i > 0)
            {
                Assert.True(
                    buttons[i - 1].X + buttons[i - 1].W <= buttons[i].X + 0.001,
                    $"buttons {i - 1} and {i} overlap");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Viewports))]
    public void TheBottomRowGivesEachThingItsOwnZone(double width, double height)
    {
        // The clock, the diagnostics line and the player's record all want the same strip.
        // They used to be given it independently, from opposite corners, and ran through
        // each other on anything narrower than a laptop.
        var layout = new BoardLayout(width, height);
        var row = layout.BottomRowFor(4, 24);

        double clockEnds = row.ClockX + (4 * layout.GlyphWidth);
        double scoreStarts = row.ScoreRightX - (24 * layout.GlyphWidth);

        Assert.True(row.ClockX >= 0, "the clock starts off the left of the board");
        Assert.True(row.ScoreRightX <= width, "the record runs off the right of the board");
        Assert.True(clockEnds <= row.StatsX + 0.001, "the clock runs into the diagnostics");
        Assert.True(clockEnds <= scoreStarts, "the clock runs into the record");

        if (row.StatsRoom > 0)
        {
            Assert.True(
                row.StatsX + row.StatsRoom <= scoreStarts + 0.001,
                "the diagnostics run into the record");
        }
    }

    [Theory]
    [MemberData(nameof(Viewports))]
    public void TheBottomTextSitsInsideItsOwnBand(double width, double height)
    {
        var layout = new BoardLayout(width, height);
        var row = layout.BottomRowFor(4, 24);

        Assert.True(row.TextY - 7 >= layout.BottomBand.Y, "the text rides above its band");
        Assert.True(row.TextY + 7 <= layout.BottomBand.Y + layout.BottomBand.H + 0.001);
    }

    [Theory]
    [MemberData(nameof(Viewports))]
    public void TheCornerRowSitsOnTopOfTheTextBand(double width, double height)
    {
        var layout = new BoardLayout(width, height);

        foreach (var button in new[] { layout.MuteButton, layout.UndoButton, layout.DrawModeButton })
        {
            Assert.True(
                button.Y + button.H <= layout.BottomBand.Y + 0.001,
                $"a corner button reaches {button.Y + button.H:F1} into a band starting at {layout.BottomBand.Y:F1}");
        }
    }

    [Theory]
    [MemberData(nameof(Viewports))]
    public void TheFinishOfferClearsTheTextAndTheCornerButtons(double width, double height)
    {
        // It was painted straight across the bottom text on a phone, and across the corner
        // of the draw-mode button as well — and Controls.Down tests this rect first, so
        // that corner of the toggle could not be pressed while the offer stood.
        var layout = new BoardLayout(width, height);
        var finish = layout.FastForwardButton;

        Assert.True(
            finish.Y + finish.H <= layout.BottomBand.Y + 0.001,
            $"the finish offer reaches {finish.Y + finish.H:F1} into a band starting at {layout.BottomBand.Y:F1}");

        foreach (var button in new[] { layout.MuteButton, layout.UndoButton, layout.DrawModeButton })
        {
            bool overlaps = finish.X < button.X + button.W &&
                            button.X < finish.X + finish.W &&
                            finish.Y < button.Y + button.H &&
                            button.Y < finish.Y + finish.H;

            Assert.False(overlaps, "the finish offer covers a corner button");
        }
    }

    [Fact]
    public void APhoneHasNoRoomForTheDiagnosticsAndADesktopDoes()
    {
        // Stated as a fact so that nobody later "fixes" a line that is missing on purpose.
        // The overlay is a developer's switch and a phone has no key to press it with.
        foreach (var (width, height) in new[] { (320.0, 480.0), (390.0, 844.0) })
        {
            var phone = new BoardLayout(width, height);
            Assert.True(
                phone.BottomRowFor(4, 24).StatsRoom < 40 * phone.GlyphWidth,
                $"{width}x{height} was given room the diagnostics could use");
        }

        var desktop = new BoardLayout(1280, 800);
        Assert.True(desktop.BottomRowFor(4, 27).StatsRoom >= 56 * desktop.GlyphWidth);
    }

    [Fact]
    public void ANewLayoutFansThree()
    {
        // The default the waste theories are written against.
        Assert.Equal(3, new BoardLayout(1200, 800).WasteFanCards);
    }

    [Theory]
    [MemberData(nameof(Viewports))]
    public void ADrawOneWasteStacksInOneSlot(double width, double height)
    {
        // Three cards that arrived one at a time never came over together, so fanning them
        // says something about the deal that is not true.
        var layout = new BoardLayout(width, height);
        layout.SetWasteFan(1);

        var slot = layout.EmptySlot(Waste);

        for (int index = 0; index < 5; index++)
        {
            Assert.Equal(slot.X, layout.CardRect(Waste, index, 5).X, 3);
        }

        Assert.Equal(0, layout.WasteFanOffset, 3);
    }

    [Theory]
    [MemberData(nameof(Viewports))]
    public void ADrawOneWasteCardIsFoundWhereItIsDrawn(double width, double height)
    {
        var board = Empty();
        var layout = new BoardLayout(width, height);
        layout.SetWasteFan(1);

        board.Position.Place(Waste, Up(Suit.Hearts, Rank.Two));
        board.Position.Place(Waste, Up(Suit.Spades, Rank.Three));
        board.Position.Place(Waste, Up(Suit.Clubs, Rank.Four));

        var top = layout.CardRect(Waste, 2, 3);

        Assert.True(layout.TryHitTest(
            board.Position, top.X + top.W / 2, top.Y + top.H / 2, out var loc, out int index));

        Assert.Equal(Waste, loc);
        Assert.Equal(2, index);
    }

    [Fact]
    public void TheFanIsClampedToWhatTheBoardCanShow()
    {
        var layout = new BoardLayout(1200, 800);

        layout.SetWasteFan(5);
        Assert.Equal(3, layout.WasteFanCards);

        // A fan of one place has no offset, and working one out would divide by zero.
        layout.SetWasteFan(0);
        Assert.Equal(1, layout.WasteFanCards);
        Assert.True(double.IsFinite(layout.WasteFanOffset));
        Assert.Equal(0, layout.WasteFanOffset, 3);
    }

    [Fact]
    public void TheFanSurvivesAResize()
    {
        var layout = new BoardLayout(1200, 800);
        layout.SetWasteFan(1);

        layout.Resize(390, 844);

        Assert.Equal(1, layout.WasteFanCards);
        Assert.Equal(layout.EmptySlot(Waste).X, layout.CardRect(Waste, 2, 3).X, 3);
    }

    [Theory]
    [MemberData(nameof(Viewports))]
    public void TheWasteFansItsLastThreeCards(double width, double height)
    {
        var layout = new BoardLayout(width, height);

        var first = layout.CardRect(Waste, 2, 5);
        var second = layout.CardRect(Waste, 3, 5);
        var third = layout.CardRect(Waste, 4, 5);

        // Three places, left to right, the newest card last.
        Assert.True(second.X > first.X, "the second card does not step right of the first");
        Assert.True(third.X > second.X, "the third card does not step right of the second");
        Assert.Equal(first.Y, third.Y, 3);

        // Anything buried deeper than the fan shows shares the first place; it is never drawn.
        Assert.Equal(first.X, layout.CardRect(Waste, 0, 5).X, 3);
        Assert.Equal(first.X, layout.CardRect(Waste, 1, 5).X, 3);
    }

    [Theory]
    [MemberData(nameof(Viewports))]
    public void AShortWasteStartsAtItsOwnSlot(double width, double height)
    {
        // One card sits where the empty slot is rather than out at the end of a fan that is
        // not there, and the slot is where a card in flight is heading.
        var layout = new BoardLayout(width, height);

        Assert.Equal(layout.EmptySlot(Waste).X, layout.CardRect(Waste, 0, 1).X, 3);
        Assert.Equal(layout.EmptySlot(Waste).X, layout.CardRect(Waste, 0, 2).X, 3);
    }

    [Theory]
    [MemberData(nameof(Viewports))]
    public void TheWasteFanNeverReachesTheFoundations(double width, double height)
    {
        // The fan grows into the gap the top row leaves at column 2. It may use that gap; it
        // may not take a bite out of the first foundation, at any card size.
        var layout = new BoardLayout(width, height);

        var furthest = layout.CardRect(Waste, 2, 3);
        var foundation = layout.CardRect(Foundation(0), 0);

        Assert.True(
            furthest.X + furthest.W <= foundation.X + 0.001,
            $"the waste fan reaches {furthest.X + furthest.W:F1}, the first foundation starts at {foundation.X:F1}");
    }

    [Theory]
    [MemberData(nameof(Viewports))]
    public void AFannedWasteCardIsWhereTheLayoutPutIt(double width, double height)
    {
        // The same round trip as a tableau column: only the top card is liftable, but the
        // hit test still has to agree with the drawer about where that card is.
        var board = Empty();
        var layout = new BoardLayout(width, height);

        board.Position.Place(Waste, Up(Suit.Hearts, Rank.Two));
        board.Position.Place(Waste, Up(Suit.Spades, Rank.Three));
        board.Position.Place(Waste, Up(Suit.Clubs, Rank.Four));

        var top = layout.CardRect(Waste, 2, 3);

        Assert.True(
            layout.TryHitTest(board.Position, top.X + top.W / 2, top.Y + top.H / 2, out var loc, out int index),
            "nothing was found at the top waste card");

        Assert.Equal(Waste, loc);
        Assert.Equal(2, index);
    }

    [Theory]
    [MemberData(nameof(Viewports))]
    public void TheWasteRegionCoversItsWholeFan(double width, double height)
    {
        // The region is cleared and repainted on its own. One that stopped at the slot would
        // wipe the top card and leave the two behind it stranded on the felt.
        var layout = new BoardLayout(width, height);
        var region = layout.PileRegion(Waste);

        for (int index = 0; index < 3; index++)
        {
            var card = layout.CardRect(Waste, index, 3);

            Assert.True(card.X >= region.X - 0.001, $"card {index} starts left of its region");
            Assert.True(
                card.X + card.W <= region.X + region.W + 0.001,
                $"card {index} runs past the right of its region");
        }
    }

    [Fact]
    public void TheWasteFanNeverJumpsAsTheWindowChanges()
    {
        var layout = new BoardLayout(300, 900);
        double previous = layout.WasteFanOffset;

        for (double width = 305; width <= 1600; width += 5)
        {
            layout.Resize(width, 900);

            Assert.True(
                Math.Abs(layout.WasteFanOffset - previous) < layout.CardWidth * 0.05,
                $"the waste fan jumped from {previous:F2} to {layout.WasteFanOffset:F2} at width {width}");

            previous = layout.WasteFanOffset;
        }
    }

    [Theory]
    [MemberData(nameof(Viewports))]
    public void TheThreeCornerButtonsDoNotOverlapOrLeaveTheBoard(double width, double height)
    {
        var layout = new BoardLayout(width, height);

        Assert.True(layout.DrawModeButton.X + layout.DrawModeButton.W <= layout.UndoButton.X + 0.001);
        Assert.True(layout.UndoButton.X + layout.UndoButton.W <= layout.MuteButton.X + 0.001);
        Assert.True(layout.DrawModeButton.X >= 0, $"the draw toggle starts at {layout.DrawModeButton.X:F1}");
    }

    [Theory]
    [MemberData(nameof(Viewports))]
    public void EveryCardCanBeFoundWhereTheLayoutPutIt(double width, double height)
    {
        // The round trip that matters: ask where a card is, press there, and get that same
        // card back. Run over a whole dealt board, so every pile kind and depth is covered.
        var board = new Board();
        var layout = new BoardLayout(width, height);

        foreach (var kind in Position.AllKinds)
        {
            for (int pileIndex = 0; pileIndex < board.Position.PileCountOf(kind); pileIndex++)
            {
                var loc = new Location(kind, pileIndex);
                var pile = board.Position.Pile(loc);

                if (pile.Count == 0)
                {
                    continue;
                }

                // The top card is the one a press can always reach; the ones below it are
                // covered by whatever is fanned over them.
                var rect = layout.CardRect(loc, pile.Count - 1);
                double x = rect.X + rect.W / 2;
                double y = rect.Y + rect.H / 2;

                Assert.True(layout.TryHitTest(board.Position, x, y, out var hit, out int index),
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

        foreach (var kind in Position.AllKinds)
        {
            for (int pileIndex = 0; pileIndex < board.Position.PileCountOf(kind); pileIndex++)
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
            Assert.False(layout.TryHitTest(board.Position, x, y, out _, out int index) && index >= 0,
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
            Assert.False(layout.TryHitTest(board.Position, x, y, out var loc, out int index) && index >= 0,
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
            Assert.False(layout.TryHitTest(board.Position, x, y, out _, out int index) && index >= 0,
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

        Assert.True(layout.TryHitTest(board.Position, slot.X + slot.W / 2, slot.Y + slot.H / 2,
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

        foreach (var kind in Position.AllKinds)
        {
            for (int pileIndex = 0; pileIndex < board.Position.PileCountOf(kind); pileIndex++)
            {
                var loc = new Location(kind, pileIndex);
                var region = layout.PileRegion(loc);
                var pile = board.Position.Pile(loc);

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

    [Fact]
    public void APressOnBareFeltHitsNothing()
    {
        var board = new Board();
        var layout = new BoardLayout(1200, 800);

        Assert.False(layout.TryHitTest(board.Position, 1199, 1, out _, out _));
    }

    private static IEnumerable<(double X, double Y)> Corners(Rect rect)
    {
        yield return (rect.X + 0.5, rect.Y + 0.5);
        yield return (rect.X + rect.W - 0.5, rect.Y + 0.5);
        yield return (rect.X + 0.5, rect.Y + rect.H - 0.5);
        yield return (rect.X + rect.W - 0.5, rect.Y + rect.H - 0.5);
        yield return (rect.X + rect.W / 2, rect.Y + rect.H / 2);
    }
}
