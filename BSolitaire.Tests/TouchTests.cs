using BSolitaire.Game;
using Xunit;
using static BSolitaire.Tests.Positions;

namespace BSolitaire.Tests;

/// <summary>
/// The gestures a finger makes, which are not the gestures a mouse makes. A pointer is a few
/// pixels of arrow with a hover state; a fingertip is a centimetre of skin with none, and it
/// sits on top of the thing it is trying to aim. Everything here is about that difference.
/// </summary>
public class TouchTests
{
    private const double Width = 390;
    private const double Height = 844;

    [Fact]
    public void ATouchedStackRidesAboveTheFinger()
    {
        // The card is drawn clear of the fingertip so it can be seen, and the drop is probed
        // from where the card is rather than from where the finger is — otherwise the player
        // aims the card and the board reads the finger.
        //
        // Aimed at a foundation rather than at a column, because a column is the one target
        // that cannot tell the difference: it takes a drop anywhere down to the bottom edge
        // of the board, so it accepts the card whether the lift is a quarter of a card, half
        // of one, or nothing at all. A foundation covers its own slot and no more.
        var (board, layout, input) = Table();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Ace));

        var grab = layout.CardRect(Tableau(0), 0);
        var slot = layout.EmptySlot(Foundation(0));

        // A finger a fifth of a card below the foundation still puts the card in it, because
        // the card it is carrying is riding higher than that.
        double x = slot.X + slot.W / 2;
        double y = slot.Y + slot.H / 2 + layout.CardHeight * 0.2;

        input.Down(grab.X + 5, grab.Y + 5, touch: true);
        input.Move(x, y);
        input.Up(x, y);

        Assert.Empty(board.Position.TableauPiles[0]);
        Assert.Single(board.Position.FoundationPiles[0]);
    }

    [Fact]
    public void TheLiftIsNotSoTallThatEveryMoveGetsLonger()
    {
        // The other edge of it. The lift is paid for on every drop — placing a card on a pile
        // means carrying the finger that far below it — so a card that rides too high costs
        // travel on every move the player makes.
        //
        // Measured at the top edge of the slot rather than its middle, because the middle
        // cannot tell: a card riding half a card high still lands in the slot from there. A
        // finger on the slot's top edge is carrying a card that has overshot it entirely once
        // the lift passes a quarter of a card.
        var (board, layout, input) = Table();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Ace));

        var grab = layout.CardRect(Tableau(0), 0);
        var slot = layout.EmptySlot(Foundation(0));

        double x = slot.X + slot.W / 2;
        double y = slot.Y;

        input.Down(grab.X + 5, grab.Y + 5, touch: true);
        input.Move(x, y);
        input.Up(x, y);

        Assert.Single(board.Position.FoundationPiles[0]);
    }

    [Fact]
    public void AMousePutsTheCardUnderThePointer()
    {
        // The same drag with a mouse: no lift, so aiming half a card low misses.
        var (board, layout, input) = Table();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Nine));
        board.Position.Place(Tableau(1), Up(Suit.Spades, Rank.Ten));

        var from = Centre(layout.CardRect(Tableau(0), 0));
        var target = Centre(layout.CardRect(Tableau(1), 0));

        input.Down(from.X, from.Y);
        input.Move(target.X, target.Y);
        input.Up(target.X, target.Y);

        Assert.Empty(board.Position.TableauPiles[0]);
        Assert.Equal(2, board.Position.TableauPiles[1].Count);
    }

    [Fact]
    public void APickedUpStackSaysWhereItCouldGo()
    {
        // A touch screen has no hover, so this is the only way the board can answer "does
        // this go here?" before the player has committed to an answer of their own.
        var (board, layout, input) = Table();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Nine));
        board.Position.Place(Tableau(1), Up(Suit.Spades, Rank.Ten));
        board.Position.Place(Tableau(2), Up(Suit.Clubs, Rank.Ten));
        board.Position.Place(Tableau(3), Up(Suit.Diamonds, Rank.Four));

        var from = Centre(layout.CardRect(Tableau(0), 0));
        input.Down(from.X, from.Y, touch: true);

        // Nothing is offered until the press has actually become a drag.
        Assert.Empty(input.DropTargets);

        input.Move(from.X + 60, from.Y + 60);

        // The red nine goes on either black ten, and nowhere else on this board.
        Assert.Contains(Tableau(1), input.DropTargets);
        Assert.Contains(Tableau(2), input.DropTargets);
        Assert.DoesNotContain(Tableau(3), input.DropTargets);
        Assert.DoesNotContain(Tableau(0), input.DropTargets);
    }

    [Fact]
    public void EveryOfferedTargetWouldReallyTakeTheCard()
    {
        // The offer and the drop have to be the same question, or the board lights up a
        // column and then refuses the card put on it.
        var (board, layout, input) = Table();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Ace));
        board.Position.Place(Foundation(1), Up(Suit.Hearts, Rank.Two));

        var from = Centre(layout.CardRect(Tableau(0), 0));
        input.Down(from.X, from.Y, touch: true);
        input.Move(from.X + 60, from.Y + 60);

        foreach (var target in input.DropTargets)
        {
            Assert.True(Rules.IsLegal(board.Position, new Move(Tableau(0), target, 1)),
                $"{target} was offered but would refuse the card");
        }

        // An ace has a home on any empty foundation, and none on the two of its own suit.
        Assert.Contains(Foundation(0), input.DropTargets);
        Assert.DoesNotContain(Foundation(1), input.DropTargets);
    }

    [Fact]
    public void TheOfferGoesAwayWhenTheStackIsPutDown()
    {
        var (board, layout, input) = Table();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Nine));
        board.Position.Place(Tableau(1), Up(Suit.Spades, Rank.Ten));

        var from = Centre(layout.CardRect(Tableau(0), 0));
        input.Down(from.X, from.Y, touch: true);
        input.Move(from.X + 60, from.Y + 60);
        Assert.NotEmpty(input.DropTargets);

        input.Up(from.X + 60, from.Y + 60);
        Assert.Empty(input.DropTargets);
    }

    [Fact]
    public void TappingACardTwiceSendsItHome()
    {
        // The double-click shortcut, spelt as two separate taps. Mobile browsers synthesise
        // dblclick unreliably over a board that has claimed the touch gesture for dragging,
        // and a player who has already tapped a card once has exactly the right idea of what
        // tapping it again should mean.
        var (board, layout, input) = Table();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Ace));

        var at = Centre(layout.CardRect(Tableau(0), 0));

        input.Down(at.X, at.Y, touch: true);
        input.Up(at.X, at.Y);
        Assert.Single(board.Position.TableauPiles[0]); // selected, not moved

        input.Down(at.X, at.Y, touch: true);
        input.Up(at.X, at.Y);

        Assert.Empty(board.Position.TableauPiles[0]);
        Assert.Equal(Rank.Ace, board.Position.FoundationPiles[(int)Suit.Hearts][0].Rank);
    }

    [Fact]
    public void TappingACardWithNoHomeTwiceJustPutsItDown()
    {
        var (board, layout, input) = Table();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Nine));
        board.Position.Place(Tableau(1), Up(Suit.Spades, Rank.Ten));

        var at = Centre(layout.CardRect(Tableau(0), 0));

        input.Down(at.X, at.Y, touch: true);
        input.Up(at.X, at.Y);
        input.Down(at.X, at.Y, touch: true);
        input.Up(at.X, at.Y);

        Assert.Single(board.Position.TableauPiles[0]);

        // Put down, not still held: a tap on the ten now selects the ten rather than moving
        // the nine onto it.
        var ten = Centre(layout.CardRect(Tableau(1), 0));
        input.Down(ten.X, ten.Y, touch: true);
        input.Up(ten.X, ten.Y);

        Assert.Single(board.Position.TableauPiles[0]);
        Assert.Single(board.Position.TableauPiles[1]);
    }

    [Fact]
    public void TappingARunTwiceDoesNotSendItHome()
    {
        // Foundations take one card at a time, so a run of two has nowhere to go — and the
        // second tap must not quietly send the top card of it instead.
        var (board, layout, input) = Table();
        board.Position.Place(Tableau(0), Up(Suit.Spades, Rank.Two));
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Ace));

        var at = layout.CardRect(Tableau(0), 0);
        double x = at.X + at.W / 2;
        double y = at.Y + Math.Min(at.H / 2, layout.FanOffset / 2);

        input.Down(x, y, touch: true);
        input.Up(x, y);
        input.Down(x, y, touch: true);
        input.Up(x, y);

        Assert.Equal(2, board.Position.TableauPiles[0].Count);
    }

    [Fact]
    public void TwoTapsStillMoveAStackToAnotherPile()
    {
        // The shortcut must not have cost the board the two-tap move it is built on.
        var (board, layout, input) = Table();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Nine));
        board.Position.Place(Tableau(1), Up(Suit.Spades, Rank.Ten));

        var nine = Centre(layout.CardRect(Tableau(0), 0));
        var ten = Centre(layout.CardRect(Tableau(1), 0));

        input.Down(nine.X, nine.Y, touch: true);
        input.Up(nine.X, nine.Y);
        input.Down(ten.X, ten.Y, touch: true);
        input.Up(ten.X, ten.Y);

        Assert.Empty(board.Position.TableauPiles[0]);
        Assert.Equal(2, board.Position.TableauPiles[1].Count);
    }

    private static (Board Board, BoardLayout Layout, PointerInput Input) Table()
    {
        var board = Empty();
        var layout = new BoardLayout(Width, Height);
        return (board, layout, new PointerInput(board, layout));
    }

    private static (double X, double Y) Centre(Rect rect) => (rect.X + rect.W / 2, rect.Y + rect.H / 2);
}
