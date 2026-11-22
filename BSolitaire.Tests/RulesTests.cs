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
        board.Position.Place(Tableau(0), Down(Suit.Hearts, Rank.Nine));
        board.Position.Place(Tableau(1), Up(Suit.Spades, Rank.Ten));

        Assert.False(Rules.IsLegal(board.Position, new Move(Tableau(0), Tableau(1), 1)));
    }

    [Fact]
    public void ARunLedByAFaceDownCardCannotBeMoved()
    {
        // What a tap used to select: a buried card and everything sitting on top of it.
        var board = Empty();
        board.Position.Place(Tableau(0), Down(Suit.Hearts, Rank.Nine));
        board.Position.Place(Tableau(0), Up(Suit.Clubs, Rank.Three));
        board.Position.Place(Tableau(1), Up(Suit.Spades, Rank.Ten));

        Assert.False(Rules.IsLegal(board.Position, new Move(Tableau(0), Tableau(1), 2)));
    }

    [Fact]
    public void TheStockIsAllowedToMoveTheCardItTurnsOver()
    {
        // The one move that carries a face-down card, since turning it over is its purpose.
        var board = Empty();
        board.Position.Place(Stock, Down(Suit.Hearts, Rank.Nine));

        Assert.True(Rules.IsLegal(board.Position, new Move(Stock, Waste, 1)));
    }

    [Fact]
    public void FoundationsTakeOneCardAtATime()
    {
        // Without this a whole run slides home whenever its last card happens to fit.
        var board = Empty();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Two));
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Ace));

        Assert.False(Rules.IsLegal(board.Position, new Move(Tableau(0), Foundation(0), 2)));
        Assert.True(Rules.IsLegal(board.Position, new Move(Tableau(0), Foundation(0), 1)));
    }

    [Fact]
    public void ACardAlreadyHomeDoesNotSlideToAnotherFoundation()
    {
        var board = Empty();
        board.Position.Place(Foundation(0), Up(Suit.Hearts, Rank.Ace));

        Assert.False(Rules.IsLegal(board.Position, new Move(Foundation(0), Foundation(1), 1)));
    }

    [Fact]
    public void NothingMovesToTheStock()
    {
        var board = Empty();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Nine));

        Assert.False(Rules.IsLegal(board.Position, new Move(Tableau(0), Stock, 1)));
    }

    [Fact]
    public void OnlyTheStockFeedsTheWaste()
    {
        var board = Empty();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Nine));

        Assert.False(Rules.IsLegal(board.Position, new Move(Tableau(0), Waste, 1)));
    }

    [Fact]
    public void APileDoesNotMoveToItself()
    {
        var board = Empty();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Nine));

        Assert.False(Rules.IsLegal(board.Position, new Move(Tableau(0), Tableau(0), 1)));
    }

    [Fact]
    public void MoreCardsThanThePileHoldsIsNotAMove()
    {
        var board = Empty();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Nine));

        Assert.False(Rules.IsLegal(board.Position, new Move(Tableau(0), Tableau(1), 2)));
        Assert.False(Rules.IsLegal(board.Position, new Move(Tableau(0), Tableau(1), 0)));
    }

    [Fact]
    public void AFaceUpRunCanBeLiftedFromAnyCardInIt()
    {
        var board = Empty();
        board.Position.Place(Tableau(0), Up(Suit.Spades, Rank.Ten));
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Nine));

        Assert.True(Rules.CanLift(board.Position, Tableau(0), 0));
        Assert.True(Rules.CanLift(board.Position, Tableau(0), 1));
    }

    [Fact]
    public void ABuriedCardCannotBeLifted()
    {
        var board = Empty();
        board.Position.Place(Tableau(0), Down(Suit.Spades, Rank.Ten));
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Nine));

        Assert.False(Rules.CanLift(board.Position, Tableau(0), 0));
        Assert.True(Rules.CanLift(board.Position, Tableau(0), 1));
    }

    [Fact]
    public void OnlyTheTopOfTheWasteAndOfAFoundationIsPlayable()
    {
        var board = Empty();
        board.Position.Place(Waste, Up(Suit.Spades, Rank.Ten));
        board.Position.Place(Waste, Up(Suit.Hearts, Rank.Nine));
        board.Position.Place(Foundation(0), Up(Suit.Hearts, Rank.Ace));
        board.Position.Place(Foundation(0), Up(Suit.Hearts, Rank.Two));

        Assert.False(Rules.CanLift(board.Position, Waste, 0));
        Assert.True(Rules.CanLift(board.Position, Waste, 1));
        Assert.False(Rules.CanLift(board.Position, Foundation(0), 0));
        Assert.True(Rules.CanLift(board.Position, Foundation(0), 1));
    }

    [Fact]
    public void TheStockIsDealtFromRatherThanDragged()
    {
        var board = Empty();
        board.Position.Place(Stock, Down(Suit.Hearts, Rank.Nine));

        Assert.False(Rules.CanLift(board.Position, Stock, 0));
    }

    [Fact]
    public void AnEmptySlotHoldsNothingToLift()
    {
        var board = Empty();

        Assert.False(Rules.CanLift(board.Position, Tableau(0), -1));
        Assert.False(Rules.CanLift(board.Position, Tableau(0), 0));
    }

    [Fact]
    public void EveryGeneratedMoveIsLegal()
    {
        // LegalMoves filters its candidates through IsLegal, so this is really asking that
        // the generator has not grown a shortcut around the rules.
        var board = new Board();

        foreach (var move in Rules.LegalMoves(board.Position))
        {
            Assert.True(Rules.IsLegal(board.Position, move), $"{move.From} -> {move.To} x{move.Count}");
        }
    }

    [Fact]
    public void AFreshDealIsNotStuck()
    {
        Assert.False(Rules.IsStuck(new Board().Position));
    }

    [Fact]
    public void ABoardWithNothingToDoIsStuck()
    {
        // Two kings, no stock, no waste: every move only swaps which column is empty.
        var board = Empty();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.King));
        board.Position.Place(Tableau(1), Up(Suit.Spades, Rank.King));

        Assert.True(Rules.IsStuck(board.Position));
    }

    [Fact]
    public void ABoardWithACardStillToPlayIsNotStuck()
    {
        var board = Empty();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.King));
        board.Position.Place(Tableau(1), Up(Suit.Spades, Rank.Ace));

        Assert.False(Rules.IsStuck(board.Position));
    }

    [Fact]
    public void ADeckCardThatUnlocksNothingDoesNotKeepADealAlive()
    {
        // The deal this is all for. The ace in the stock has a home, so the old answer was
        // that the game was still on — and the player had to cycle the deck, play it, and
        // find out there was nothing underneath. There is one thing to do and it leads
        // nowhere, so the deal is over now rather than three moves from now.
        var board = Empty();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.King));
        board.Position.Place(Tableau(1), Up(Suit.Spades, Rank.King));
        board.Position.Place(Stock, Down(Suit.Diamonds, Rank.Ace));

        Assert.True(Rules.IsStuck(board.Position));
    }

    [Fact]
    public void AForcedPlayThatOpensSomethingUpKeepsTheDealAlive()
    {
        // The same shape, except that sending the ace home gives the two a foundation to
        // follow it onto. Following the line is the only way to tell these two apart.
        var board = Empty();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.King));
        board.Position.Place(Tableau(1), Up(Suit.Diamonds, Rank.Two));
        board.Position.Place(Stock, Down(Suit.Diamonds, Rank.Ace));

        Assert.False(Rules.IsStuck(board.Position));
    }

    [Fact]
    public void ADeckCardWithTwoPlacesToGoIsLeftToThePlayer()
    {
        // The red ace can go home or onto either black two. Both lines happen to die, but
        // which is taken is the player's to decide, and a check that picks for them is one
        // that will eventually pick wrong and end a game somebody could have won.
        var board = Empty();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.King));
        board.Position.Place(Tableau(1), Up(Suit.Spades, Rank.Two));
        board.Position.Place(Tableau(2), Up(Suit.Clubs, Rank.Two));
        board.Position.Place(Stock, Down(Suit.Hearts, Rank.Ace));

        Assert.False(Rules.IsStuck(board.Position));
    }

    [Fact]
    public void TwoPlayableDeckCardsAreLeftToThePlayerToo()
    {
        // Two aces, each with one place to go, but the order is a choice and the order can
        // matter. Neither line is forced, so neither is followed.
        var board = Empty();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.King));
        board.Position.Place(Stock, Down(Suit.Diamonds, Rank.Ace));
        board.Position.Place(Stock, Down(Suit.Clubs, Rank.Ace));

        Assert.False(Rules.IsStuck(board.Position));
    }

    [Fact]
    public void FollowingTheLineNeverMovesTheRealCards()
    {
        // The check runs after every move, on the position the board is about to be drawn
        // from. It works on copies; if it ever worked on the originals the player would
        // watch cards leave the stock on their own.
        var board = Empty();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.King));
        board.Position.Place(Tableau(1), Up(Suit.Spades, Rank.King));
        board.Position.Place(Stock, Down(Suit.Diamonds, Rank.Ace));

        Rules.IsStuck(board.Position);

        Assert.Single(board.Position.FaceDownPile);
        Assert.Equal(Rank.Ace, board.Position.FaceDownPile[0].Rank);
        Assert.False(board.Position.FaceDownPile[0].IsFaceUp);
        Assert.Empty(board.Position.FoundationPiles[0]);
    }

    [Fact]
    public void TheStockTurnsOverAsManyCardsAsTheDealIsPlayedWith()
    {
        // Nothing passes three yet — draw-three is the next change — but the stuck check and
        // the fast-forward both read their moves from here, so the count has to arrive as
        // part of the question rather than as a constant.
        var board = new Board();

        Assert.Contains(
            Rules.LegalMoves(board.Position, 3),
            move => move.From.Kind == PileKind.FaceDown && move.Count == 3);

        Assert.Contains(
            Rules.LegalMoves(board.Position),
            move => move.From.Kind == PileKind.FaceDown && move.Count == 1);
    }

    [Fact]
    public void AStockShorterThanTheDrawTurnsOverWhatIsLeft()
    {
        var board = Empty();
        board.Position.Place(Stock, Down(Suit.Hearts, Rank.Nine));
        board.Position.Place(Stock, Down(Suit.Spades, Rank.Ten));

        Assert.Contains(
            Rules.LegalMoves(board.Position, 3),
            move => move.From.Kind == PileKind.FaceDown && move.Count == 2);
    }
}
