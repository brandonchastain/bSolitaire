using BSolitaire.Game;
using Xunit;
using static BSolitaire.Tests.Positions;

namespace BSolitaire.Tests;

/// <summary>
/// Taking a move back. The thing worth pinning down is not that the cards return — that much
/// a list restore gets right by construction — but that everything a move quietly did on its
/// way past comes back with them: the card it turned over, the state it put the game into,
/// and the deal it did or did not add to the record.
/// </summary>
public class UndoTests
{
    [Fact]
    public void AFreshBoardHasNothingToTakeBack()
    {
        Assert.False(new Board().CanUndo);
        Assert.False(new Board().Undo());
    }

    [Fact]
    public void AMoveCanBeTakenBack()
    {
        var board = Empty();
        board.Place(Tableau(0), Up(Suit.Hearts, Rank.Nine));
        board.Place(Tableau(1), Up(Suit.Spades, Rank.Ten));

        Assert.True(board.MakeMove(new Move(Tableau(0), Tableau(1), 1)));
        Assert.True(board.CanUndo);
        Assert.True(board.Undo());

        Assert.Single(board.TableauPiles[0]);
        Assert.Single(board.TableauPiles[1]);
        Assert.Equal(Rank.Nine, board.TableauPiles[0][0].Rank);
    }

    [Fact]
    public void TakingAMoveBackPutsTheUncoveredCardFaceDownAgain()
    {
        // The reason undo is a snapshot rather than a move played backwards. Moving the nine
        // off turns the card beneath it up, and that flip is no part of the move itself.
        var board = Empty();
        board.Place(Tableau(0), Down(Suit.Clubs, Rank.Four));
        board.Place(Tableau(0), Up(Suit.Hearts, Rank.Nine));
        board.Place(Tableau(1), Up(Suit.Spades, Rank.Ten));

        board.MakeMove(new Move(Tableau(0), Tableau(1), 1));
        Assert.True(board.TableauPiles[0][0].IsFaceUp);

        board.Undo();
        Assert.False(board.TableauPiles[0][0].IsFaceUp);
    }

    [Fact]
    public void TurningTheWasteOverCanBeTakenBack()
    {
        var board = Empty();
        board.Place(Waste, Up(Suit.Hearts, Rank.Two));
        board.Place(Waste, Up(Suit.Spades, Rank.Three));

        Assert.True(board.RecycleWaste());
        Assert.Equal(2, board.FaceDownPile.Count);

        board.Undo();

        Assert.Empty(board.FaceDownPile);
        Assert.Equal(2, board.FaceUpPile.Count);
        Assert.True(board.FaceUpPile[^1].IsFaceUp);
        Assert.Equal(Rank.Three, board.FaceUpPile[^1].Rank);
    }

    [Fact]
    public void AWonDealStaysWon()
    {
        // The cards are already falling and the deal has been counted. Nothing about a
        // finished game is worth offering to take back.
        var board = FourKingsFromDone();

        for (int i = 0; i < 4; i++)
        {
            board.MakeMove(new Move(Tableau(i), Foundation(i), 1));
        }

        Assert.Equal(GameState.Won, board.State);
        Assert.False(board.CanUndo);
    }

    [Fact]
    public void ALostDealCanBeTakenBackAndIsStillTheSameDeal()
    {
        // Undoing off a dead board is exactly when a player wants it, and the record must
        // not count the deal a second time for dying twice.
        var game = new Solitaire();
        var board = game.Board;
        ClearBoard(board);

        // A board with one move on it, and that move ends the game.
        board.Place(Foundation(0), Up(Suit.Clubs, Rank.Ace));
        board.Place(Tableau(0), Up(Suit.Clubs, Rank.Two));

        int dealBefore = board.DealId;
        board.MakeMove(new Move(Tableau(0), Foundation(0), 1));
        Assert.NotEqual(GameState.Playing, board.State);

        game.Update(TimeSpan.FromMilliseconds(16));
        int gamesAfterFirstEnding = game.Score.Games;
        Assert.Equal(1, gamesAfterFirstEnding);

        Assert.True(board.CanUndo);
        board.Undo();
        Assert.Equal(GameState.Playing, board.State);
        Assert.Equal(dealBefore, board.DealId);

        // ...and getting stuck all over again is the same deal ending, not a second one.
        board.MakeMove(new Move(Tableau(0), Foundation(0), 1));
        game.Update(TimeSpan.FromMilliseconds(32));
        Assert.Equal(gamesAfterFirstEnding, game.Score.Games);
    }

    [Fact]
    public void DealingAgainForgetsTheGameBefore()
    {
        var board = Empty();
        board.Place(Tableau(0), Up(Suit.Hearts, Rank.Nine));
        board.Place(Tableau(1), Up(Suit.Spades, Rank.Ten));
        board.MakeMove(new Move(Tableau(0), Tableau(1), 1));

        board.Reset();

        Assert.False(board.CanUndo);
    }

    [Fact]
    public void ZTakesAMoveBack()
    {
        var game = new Solitaire();
        game.Resize(1200, 800);
        ClearBoard(game.Board);

        game.Board.Place(Tableau(0), Up(Suit.Hearts, Rank.Nine));
        game.Board.Place(Tableau(1), Up(Suit.Spades, Rank.Ten));
        game.Board.MakeMove(new Move(Tableau(0), Tableau(1), 1));

        Assert.True(game.CanUndo);
        game.OnKeyDown("KeyZ");

        Assert.Single(game.Board.TableauPiles[0]);
        Assert.False(game.CanUndo);
    }

    [Fact]
    public void TheUndoButtonTakesThePressBeforeTheFeltDoes()
    {
        var game = new Solitaire();
        game.Resize(1200, 800);
        ClearBoard(game.Board);

        game.Board.Place(Tableau(0), Up(Suit.Hearts, Rank.Nine));
        game.Board.Place(Tableau(1), Up(Suit.Spades, Rank.Ten));
        game.Board.MakeMove(new Move(Tableau(0), Tableau(1), 1));

        var button = game.Layout.UndoButton;
        game.OnPointerDown(button.X + button.W / 2, button.Y + button.H / 2);

        Assert.Single(game.Board.TableauPiles[0]);
    }

    [Fact]
    public void TheUndoButtonIsOnlyPressableWhileItIsOffered()
    {
        // Nothing to take back means nothing drawn there, and a button that is not drawn
        // must not swallow a press.
        var game = new Solitaire();
        game.Resize(1200, 800);

        Assert.False(game.CanUndo);

        var button = game.Layout.UndoButton;
        game.OnPointerDown(button.X + button.W / 2, button.Y + button.H / 2);
        game.OnPointerUp(button.X + button.W / 2, button.Y + button.H / 2);

        // The press did nothing at all, rather than being quietly eaten.
        Assert.False(game.CanUndo);
    }

    private static void ClearBoard(Board board)
    {
        board.Strip();
    }
}
