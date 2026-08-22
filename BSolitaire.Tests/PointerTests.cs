using BSolitaire.Game;
using Xunit;
using static BSolitaire.Tests.Positions;

namespace BSolitaire.Tests;

/// <summary>
/// The pointer, driven through a real layout at a real size. This is the largest piece of the
/// game the player touches directly and the one the face-down bug lived in, so it is worth
/// exercising as gestures rather than as method calls: press, travel, release.
/// </summary>
public class PointerTests
{
    private const double Width = 1200;
    private const double Height = 800;

    [Fact]
    public void DraggingACardOntoALegalPileMovesIt()
    {
        var (board, layout, input) = Table();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Nine));
        board.Position.Place(Tableau(1), Up(Suit.Spades, Rank.Ten));

        DragCard(input, Grab(layout, Tableau(0), 0), Grab(layout, Tableau(1), 0));

        Assert.Empty(board.TableauPiles[0]);
        Assert.Equal(2, board.TableauPiles[1].Count);
    }

    [Fact]
    public void DraggingOntoAnIllegalPileSnapsBack()
    {
        var (board, layout, input) = Table();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Nine));
        board.Position.Place(Tableau(1), Up(Suit.Diamonds, Rank.Ten)); // same colour

        DragCard(input, Grab(layout, Tableau(0), 0), Grab(layout, Tableau(1), 0));

        Assert.Single(board.TableauPiles[0]);
        Assert.Single(board.TableauPiles[1]);
        Assert.Null(input.Drag);
    }

    [Fact]
    public void AWholeRunComesUpTogether()
    {
        var (board, layout, input) = Table();
        board.Position.Place(Tableau(0), Up(Suit.Spades, Rank.Ten));
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Nine));
        board.Position.Place(Tableau(1), Up(Suit.Diamonds, Rank.Jack));

        DragCard(input, Grab(layout, Tableau(0), 0), Grab(layout, Tableau(1), 0));

        Assert.Empty(board.TableauPiles[0]);
        Assert.Equal(3, board.TableauPiles[1].Count);
    }

    [Fact]
    public void AFaceDownCardIsNotPickedUpAtAll()
    {
        var (board, layout, input) = Table();
        board.Position.Place(Tableau(0), Down(Suit.Hearts, Rank.Nine));
        board.Position.Place(Tableau(0), Up(Suit.Clubs, Rank.Three));
        board.Position.Place(Tableau(1), Up(Suit.Spades, Rank.Ten));

        input.Down(Grab(layout, Tableau(0), 0).X, Grab(layout, Tableau(0), 0).Y);
        input.Move(Grab(layout, Tableau(1), 0).X, Grab(layout, Tableau(1), 0).Y);

        Assert.Null(input.Drag);
    }

    [Fact]
    public void APressThatBarelyMovesIsATapRatherThanADrag()
    {
        var (board, layout, input) = Table();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Nine));
        var (x, y) = Grab(layout, Tableau(0), 0);

        input.Down(x, y);
        input.Move(x + 2, y + 2); // inside the threshold

        Assert.Null(input.Drag);
    }

    [Fact]
    public void ACancelledDragLeavesTheCardsWhereTheyWere()
    {
        var (board, layout, input) = Table();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Nine));
        board.Position.Place(Tableau(1), Up(Suit.Spades, Rank.Ten));
        var (x, y) = Grab(layout, Tableau(0), 0);

        input.Down(x, y);
        input.Move(x + 200, y + 40);
        input.Cancel();

        Assert.Null(input.Drag);
        Assert.Single(board.TableauPiles[0]);
        Assert.Single(board.TableauPiles[1]);
    }

    [Fact]
    public void TappingTheStockTurnsACardOver()
    {
        var (board, layout, input) = Table();
        board.Position.Place(Stock, Down(Suit.Hearts, Rank.Nine));

        Tap(input, Grab(layout, Stock, 0));

        Assert.Empty(board.FaceDownPile);
        Assert.Single(board.FaceUpPile);
    }

    [Fact]
    public void TappingAnEmptyStockTurnsTheWasteBackOver()
    {
        var (board, layout, input) = Table();
        board.Position.Place(Waste, Up(Suit.Hearts, Rank.Nine));

        Tap(input, Centre(layout.EmptySlot(Stock)));

        Assert.Single(board.FaceDownPile);
        Assert.Empty(board.FaceUpPile);
        Assert.False(board.FaceDownPile[0].IsFaceUp);
    }

    [Fact]
    public void TapToSelectThenTapToMove()
    {
        // The touch-friendly second mode: pick a card, then pick where it goes.
        var (board, layout, input) = Table();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Nine));
        board.Position.Place(Tableau(1), Up(Suit.Spades, Rank.Ten));

        Tap(input, Grab(layout, Tableau(0), 0));
        Tap(input, Grab(layout, Tableau(1), 0));

        Assert.Empty(board.TableauPiles[0]);
        Assert.Equal(2, board.TableauPiles[1].Count);
    }

    [Fact]
    public void ATapCannotSelectABuriedCard()
    {
        // The bug this suite exists for, reached the way a player would reach it: the nine is
        // face down and would stack on the ten, and tapping it used to pick it up regardless.
        var (board, layout, input) = Table();
        board.Position.Place(Tableau(0), Down(Suit.Hearts, Rank.Nine));
        board.Position.Place(Tableau(0), Up(Suit.Clubs, Rank.Three));
        board.Position.Place(Tableau(1), Up(Suit.Spades, Rank.Ten));

        Tap(input, Grab(layout, Tableau(0), 0));
        Tap(input, Grab(layout, Tableau(1), 0));

        Assert.Equal(2, board.TableauPiles[0].Count);
        Assert.Single(board.TableauPiles[1]);
    }

    [Fact]
    public void ATapOnBareFeltCancelsASelection()
    {
        var (board, layout, input) = Table();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Nine));
        board.Position.Place(Tableau(1), Up(Suit.Spades, Rank.Ten));

        Tap(input, Grab(layout, Tableau(0), 0));
        Tap(input, (Width - 4, Height / 2)); // nothing out there
        Tap(input, Grab(layout, Tableau(1), 0));

        Assert.Single(board.TableauPiles[0]);
        Assert.Single(board.TableauPiles[1]);
    }

    [Fact]
    public void TappingTheSelectionAgainPutsItDown()
    {
        var (board, layout, input) = Table();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Nine));
        board.Position.Place(Tableau(1), Up(Suit.Spades, Rank.Ten));

        Tap(input, Grab(layout, Tableau(0), 0));
        Tap(input, Grab(layout, Tableau(0), 0)); // deselects rather than refusing a self-move
        Tap(input, Grab(layout, Tableau(1), 0)); // so this selects the ten instead of moving

        Assert.Single(board.TableauPiles[0]);
        Assert.Single(board.TableauPiles[1]);
        Assert.DoesNotContain(Sound.Invalid, board.Sounds);
    }

    [Fact]
    public void TapToMoveOntoAnEmptyColumnStillWorks()
    {
        var (board, layout, input) = Table();
        board.Position.Place(Tableau(0), Up(Suit.Spades, Rank.King));

        Tap(input, Grab(layout, Tableau(0), 0));
        Tap(input, Centre(layout.EmptySlot(Tableau(3))));

        Assert.Empty(board.TableauPiles[0]);
        Assert.Single(board.TableauPiles[3]);
    }

    [Fact]
    public void TapToMoveSendsACardToAFoundation()
    {
        var (board, layout, input) = Table();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Ace));

        Tap(input, Grab(layout, Tableau(0), 0));
        Tap(input, Centre(layout.EmptySlot(Foundation(2))));

        Assert.Empty(board.TableauPiles[0]);
        Assert.Equal(1, board.FoundationTotal);
    }

    [Fact]
    public void ADragDoesNotLeaveASelectionBehind()
    {
        var (board, layout, input) = Table();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Nine));
        board.Position.Place(Tableau(1), Up(Suit.Spades, Rank.Ten));
        board.Position.Place(Tableau(2), Up(Suit.Clubs, Rank.King));

        Tap(input, Grab(layout, Tableau(0), 0));                                  // select the nine
        DragCard(input, Grab(layout, Tableau(2), 0), Centre(layout.EmptySlot(Tableau(5)))); // drag the king
        Tap(input, Grab(layout, Tableau(1), 0));                                  // now select the ten

        // If the drag had left the nine selected, this last tap would have moved it.
        Assert.Single(board.TableauPiles[0]);
        Assert.Single(board.TableauPiles[1]);
    }

    [Fact]
    public void TappingACardTwiceSendsItHome()
    {
        var (board, layout, input) = Table();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Ace));
        var at = Grab(layout, Tableau(0), 0);

        Tap(input, at);
        Tap(input, at);

        Assert.Equal(1, board.FoundationTotal);
    }

    [Fact]
    public void TappingACardWithNoFoundationTwiceDoesNothing()
    {
        var (board, layout, input) = Table();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Five));
        var at = Grab(layout, Tableau(0), 0);

        Tap(input, at);
        Tap(input, at);

        Assert.Equal(0, board.FoundationTotal);
        Assert.Single(board.TableauPiles[0]);
    }

    [Fact]
    public void TappingABuriedCardTwiceDoesNothing()
    {
        // Foundations take one card at a time, so a run has nowhere to go — and the second
        // tap must not quietly send the top of it instead.
        var (board, layout, input) = Table();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Ace));
        board.Position.Place(Tableau(0), Up(Suit.Clubs, Rank.Three));
        var at = Grab(layout, Tableau(0), 0);

        Tap(input, at);
        Tap(input, at);

        Assert.Equal(0, board.FoundationTotal);
        Assert.Equal(2, board.TableauPiles[0].Count);
    }

    [Fact]
    public void ADoubleTapSendsExactlyOneCardHome()
    {
        // A double tap is two taps, and the two of them play the card home between them.
        // There used to be a dblclick handler as well, which the browser fires *after* that
        // pair — by which time the card that was underneath is sitting on top, one rank
        // behind the foundation, and perfectly playable. Two cards went home for one tap.
        //
        // Taps are the only route now. Four of them in a row are two separate moves, which
        // is what four taps are, and never three cards for two.
        var (board, layout, input) = Table();
        board.Position.Place(Foundation(0), Up(Suit.Hearts, Rank.Ace));
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Three));
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Two));

        Tap(input, Grab(layout, Tableau(0), 1));
        Tap(input, Grab(layout, Tableau(0), 1));

        Assert.Single(board.TableauPiles[0]);
        Assert.Equal(Rank.Three, board.TableauPiles[0][0].Rank);
        Assert.Equal(2, board.FoundationPiles[0].Count);
    }

    [Fact]
    public void HoveringReportsWhatWouldActuallyMove()
    {
        // The highlight shows the grab point rather than the card under the cursor, because
        // what the player wants to know is what is about to come up.
        var (board, layout, input) = Table();
        board.Position.Place(Tableau(0), Up(Suit.Spades, Rank.Ten));
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Nine));

        var (x, y) = Grab(layout, Tableau(0), 0);
        Assert.True(input.Move(x, y));

        Assert.Equal(Tableau(0), input.HoverPile);
        Assert.Equal(0, input.HoverIndex);
    }

    [Fact]
    public void HoveringOverSomethingUngrabbableHighlightsNothing()
    {
        var (board, layout, input) = Table();
        board.Position.Place(Stock, Down(Suit.Hearts, Rank.Nine));

        var (x, y) = Grab(layout, Stock, 0);
        input.Move(x, y);

        Assert.Null(input.HoverPile);
    }

    [Fact]
    public void HoveringWithinTheSameCardIsNotWorthARedraw()
    {
        var (board, layout, input) = Table();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Nine));
        var (x, y) = Grab(layout, Tableau(0), 0);

        Assert.True(input.Move(x, y));    // crossed into it
        Assert.False(input.Move(x + 1, y)); // still inside it
    }

    [Fact]
    public void TheBannerSwallowsWhateverIsUnderIt()
    {
        // The panel is on top once the game is over, so nothing beneath it is grabbable.
        var (board, layout, input) = Table();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.King));
        board.Position.Place(Tableau(1), Up(Suit.Spades, Rank.Queen));
        board.MakeMove(new Move(Tableau(1), Tableau(0), 1));
        Assert.Equal(GameState.Stuck, board.State);

        var (x, y) = Centre(layout.Banner);
        input.Down(x, y);
        input.Move(x + 100, y + 100);

        Assert.Null(input.Drag);
    }

    [Fact]
    public void TheBannersButtonDealsAgain()
    {
        var (board, layout, input) = Table();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.King));
        board.Position.Place(Tableau(1), Up(Suit.Spades, Rank.Queen));
        board.MakeMove(new Move(Tableau(1), Tableau(0), 1));

        Tap(input, Centre(layout.NewGameButton));

        Assert.Equal(GameState.Playing, board.State);
        Assert.Equal(24, board.FaceDownPile.Count);
    }

    /// <summary>A press and release without travel.</summary>
    private static void Tap(PointerInput input, (double X, double Y) at)
    {
        input.Down(at.X, at.Y);
        input.Up(at.X, at.Y);
    }

    private static (Board Board, BoardLayout Layout, PointerInput Input) Table()
    {
        var board = Empty();
        var layout = new BoardLayout(Width, Height);
        return (board, layout, new PointerInput(board, layout));
    }

    private static (double X, double Y) Centre(Rect rect) => (rect.X + rect.W / 2, rect.Y + rect.H / 2);

    /// <summary>
    /// Where a player would actually press to take this card. A tableau column is fanned, so
    /// every card but the last is mostly hidden under the next one — the centre of a buried
    /// card belongs to the card on top of it, on screen and in the hit test alike. The strip
    /// still showing is the only part of it anyone can point at.
    /// </summary>
    private static (double X, double Y) Grab(BoardLayout layout, Location loc, int index)
    {
        var rect = layout.CardRect(loc, index);
        return (rect.X + rect.W / 2, rect.Y + Math.Min(rect.H / 2, layout.FanOffset / 2));
    }

    /// <summary>A press, a real amount of travel, and a release — the gesture, not the events.</summary>
    private static void DragCard(PointerInput input, (double X, double Y) from, (double X, double Y) to)
    {
        input.Down(from.X, from.Y);
        input.Move(to.X, to.Y);
        input.Up(to.X, to.Y);
    }
}
