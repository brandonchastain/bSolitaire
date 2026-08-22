using BSolitaire.Game;
using Xunit;
using static BSolitaire.Tests.Positions;

namespace BSolitaire.Tests;

/// <summary>
/// The rules themselves, asked directly. Everything else in the game defers to these, so if
/// they are wrong nothing above them can be right.
/// </summary>
public class RulesTests
{
    [Fact]
    public void TableauTakesTheOppositeColourOneRankDown()
    {
        Assert.True(Rules.CanStack(Up(Suit.Hearts, Rank.Nine), Up(Suit.Spades, Rank.Ten)));
        Assert.True(Rules.CanStack(Up(Suit.Clubs, Rank.Nine), Up(Suit.Diamonds, Rank.Ten)));
    }

    [Fact]
    public void TableauRefusesTheSameColour()
    {
        Assert.False(Rules.CanStack(Up(Suit.Hearts, Rank.Nine), Up(Suit.Diamonds, Rank.Ten)));
        Assert.False(Rules.CanStack(Up(Suit.Clubs, Rank.Nine), Up(Suit.Spades, Rank.Ten)));
    }

    [Fact]
    public void TableauRefusesAnythingButOneRankDown()
    {
        Assert.False(Rules.CanStack(Up(Suit.Hearts, Rank.Eight), Up(Suit.Spades, Rank.Ten)));
        Assert.False(Rules.CanStack(Up(Suit.Hearts, Rank.Jack), Up(Suit.Spades, Rank.Ten)));
        Assert.False(Rules.CanStack(Up(Suit.Hearts, Rank.Ten), Up(Suit.Spades, Rank.Ten)));
    }

    [Fact]
    public void OnlyAKingStartsAnEmptyColumn()
    {
        Assert.True(Rules.CanStack(Up(Suit.Hearts, Rank.King), null));
        Assert.False(Rules.CanStack(Up(Suit.Hearts, Rank.Queen), null));
        Assert.False(Rules.CanStack(Up(Suit.Hearts, Rank.Ace), null));
    }

    [Fact]
    public void FoundationsTakeTheSameSuitOneRankUp()
    {
        Assert.True(Rules.CanFound(Up(Suit.Hearts, Rank.Eight), Up(Suit.Hearts, Rank.Seven)));
        Assert.False(Rules.CanFound(Up(Suit.Hearts, Rank.Eight), Up(Suit.Spades, Rank.Seven)));
        Assert.False(Rules.CanFound(Up(Suit.Hearts, Rank.Nine), Up(Suit.Hearts, Rank.Seven)));
        Assert.False(Rules.CanFound(Up(Suit.Hearts, Rank.Six), Up(Suit.Hearts, Rank.Seven)));
    }

    [Fact]
    public void OnlyAnAceStartsAFoundation()
    {
        Assert.True(Rules.CanFound(Up(Suit.Hearts, Rank.Ace), null));
        Assert.False(Rules.CanFound(Up(Suit.Hearts, Rank.Two), null));
    }

    [Fact]
    public void AFaceDownCardCannotBeMoved()
    {
        // The nine would stack on the ten perfectly well — it is face down that stops it.
        var board = Empty();
        board.Place(Tableau(0), Down(Suit.Hearts, Rank.Nine));
        board.Place(Tableau(1), Up(Suit.Spades, Rank.Ten));

        Assert.False(Rules.IsLegal(board, new Move(Tableau(0), Tableau(1), 1)));
    }

    [Fact]
    public void ARunLedByAFaceDownCardCannotBeMoved()
    {
        // What a tap used to select: a buried card and everything sitting on top of it.
        var board = Empty();
        board.Place(Tableau(0), Down(Suit.Hearts, Rank.Nine));
        board.Place(Tableau(0), Up(Suit.Clubs, Rank.Three));
        board.Place(Tableau(1), Up(Suit.Spades, Rank.Ten));

        Assert.False(Rules.IsLegal(board, new Move(Tableau(0), Tableau(1), 2)));
    }

    [Fact]
    public void TheStockIsAllowedToMoveTheCardItTurnsOver()
    {
        // The one move that carries a face-down card, since turning it over is its purpose.
        var board = Empty();
        board.Place(Stock, Down(Suit.Hearts, Rank.Nine));

        Assert.True(Rules.IsLegal(board, new Move(Stock, Waste, 1)));
    }

    [Fact]
    public void FoundationsTakeOneCardAtATime()
    {
        // Without this a whole run slides home whenever its last card happens to fit.
        var board = Empty();
        board.Place(Tableau(0), Up(Suit.Hearts, Rank.Two));
        board.Place(Tableau(0), Up(Suit.Hearts, Rank.Ace));

        Assert.False(Rules.IsLegal(board, new Move(Tableau(0), Foundation(0), 2)));
        Assert.True(Rules.IsLegal(board, new Move(Tableau(0), Foundation(0), 1)));
    }

    [Fact]
    public void ACardAlreadyHomeDoesNotSlideToAnotherFoundation()
    {
        var board = Empty();
        board.Place(Foundation(0), Up(Suit.Hearts, Rank.Ace));

        Assert.False(Rules.IsLegal(board, new Move(Foundation(0), Foundation(1), 1)));
    }

    [Fact]
    public void NothingMovesToTheStock()
    {
        var board = Empty();
        board.Place(Tableau(0), Up(Suit.Hearts, Rank.Nine));

        Assert.False(Rules.IsLegal(board, new Move(Tableau(0), Stock, 1)));
    }

    [Fact]
    public void OnlyTheStockFeedsTheWaste()
    {
        var board = Empty();
        board.Place(Tableau(0), Up(Suit.Hearts, Rank.Nine));

        Assert.False(Rules.IsLegal(board, new Move(Tableau(0), Waste, 1)));
    }

    [Fact]
    public void APileDoesNotMoveToItself()
    {
        var board = Empty();
        board.Place(Tableau(0), Up(Suit.Hearts, Rank.Nine));

        Assert.False(Rules.IsLegal(board, new Move(Tableau(0), Tableau(0), 1)));
    }

    [Fact]
    public void MoreCardsThanThePileHoldsIsNotAMove()
    {
        var board = Empty();
        board.Place(Tableau(0), Up(Suit.Hearts, Rank.Nine));

        Assert.False(Rules.IsLegal(board, new Move(Tableau(0), Tableau(1), 2)));
        Assert.False(Rules.IsLegal(board, new Move(Tableau(0), Tableau(1), 0)));
    }

    [Fact]
    public void AFaceUpRunCanBeLiftedFromAnyCardInIt()
    {
        var board = Empty();
        board.Place(Tableau(0), Up(Suit.Spades, Rank.Ten));
        board.Place(Tableau(0), Up(Suit.Hearts, Rank.Nine));

        Assert.True(Rules.CanLift(board, Tableau(0), 0));
        Assert.True(Rules.CanLift(board, Tableau(0), 1));
    }

    [Fact]
    public void ABuriedCardCannotBeLifted()
    {
        var board = Empty();
        board.Place(Tableau(0), Down(Suit.Spades, Rank.Ten));
        board.Place(Tableau(0), Up(Suit.Hearts, Rank.Nine));

        Assert.False(Rules.CanLift(board, Tableau(0), 0));
        Assert.True(Rules.CanLift(board, Tableau(0), 1));
    }

    [Fact]
    public void OnlyTheTopOfTheWasteAndOfAFoundationIsPlayable()
    {
        var board = Empty();
        board.Place(Waste, Up(Suit.Spades, Rank.Ten));
        board.Place(Waste, Up(Suit.Hearts, Rank.Nine));
        board.Place(Foundation(0), Up(Suit.Hearts, Rank.Ace));
        board.Place(Foundation(0), Up(Suit.Hearts, Rank.Two));

        Assert.False(Rules.CanLift(board, Waste, 0));
        Assert.True(Rules.CanLift(board, Waste, 1));
        Assert.False(Rules.CanLift(board, Foundation(0), 0));
        Assert.True(Rules.CanLift(board, Foundation(0), 1));
    }

    [Fact]
    public void TheStockIsDealtFromRatherThanDragged()
    {
        var board = Empty();
        board.Place(Stock, Down(Suit.Hearts, Rank.Nine));

        Assert.False(Rules.CanLift(board, Stock, 0));
    }

    [Fact]
    public void AnEmptySlotHoldsNothingToLift()
    {
        var board = Empty();

        Assert.False(Rules.CanLift(board, Tableau(0), -1));
        Assert.False(Rules.CanLift(board, Tableau(0), 0));
    }

    [Fact]
    public void EveryGeneratedMoveIsLegal()
    {
        // LegalMoves filters its candidates through IsLegal, so this is really asking that
        // the generator has not grown a shortcut around the rules.
        var board = new Board();

        foreach (var move in Rules.LegalMoves(board))
        {
            Assert.True(Rules.IsLegal(board, move), $"{move.From} -> {move.To} x{move.Count}");
        }
    }

    [Fact]
    public void AFreshDealIsNotStuck()
    {
        Assert.False(Rules.IsStuck(new Board()));
    }

    [Fact]
    public void ABoardWithNothingToDoIsStuck()
    {
        // Two kings, no stock, no waste: every move only swaps which column is empty.
        var board = Empty();
        board.Place(Tableau(0), Up(Suit.Hearts, Rank.King));
        board.Place(Tableau(1), Up(Suit.Spades, Rank.King));

        Assert.True(Rules.IsStuck(board));
    }

    [Fact]
    public void ABoardWithACardStillToPlayIsNotStuck()
    {
        var board = Empty();
        board.Place(Tableau(0), Up(Suit.Hearts, Rank.King));
        board.Place(Tableau(1), Up(Suit.Spades, Rank.Ace));

        Assert.False(Rules.IsStuck(board));
    }
}
