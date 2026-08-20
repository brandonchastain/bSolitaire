using BSolitaire.Game;
using Xunit;
using static BSolitaire.Tests.Positions;

namespace BSolitaire.Tests;

/// <summary>
/// The session and the pieces around it: what a key or a button means, what gets counted, and
/// what the host is told. This is the part the refactor moved, so it is the part most worth
/// pinning down.
/// </summary>
public class SessionTests
{
    /// <summary>A game sized like a real viewport, so the buttons are where the layout puts
    /// them rather than at the placeholder size.</summary>
    private static Solitaire Sized()
    {
        var game = new Solitaire();
        game.Resize(1200, 800);
        game.MarkClean();
        return game;
    }

    private static (double X, double Y) Centre(Rect rect) => (rect.X + rect.W / 2, rect.Y + rect.H / 2);

    [Fact]
    public void AFreshGameWantsDrawingAndIsPlaying()
    {
        var game = new Solitaire();

        Assert.True(game.NeedsRedraw);
        Assert.Equal(GameState.Playing, game.State);
        Assert.Null(game.Error);
    }

    [Fact]
    public void DrawingTheBoardSettlesIt()
    {
        var game = Sized();

        Assert.False(game.NeedsRedraw);
    }

    [Fact]
    public void HoveringAloneDoesNotForceARedraw()
    {
        // The pointer crosses a card once and then spends dozens of frames inside it, so
        // moving without holding anything must not defeat the redraw check.
        var game = Sized();
        game.OnPointerMove(5, 5);
        game.MarkClean();

        game.OnPointerMove(6, 6);

        Assert.False(game.NeedsRedraw);
    }

    [Fact]
    public void FFlipsTheStatsOverlay()
    {
        var game = Sized();

        game.OnKeyDown("KeyF");
        Assert.True(game.ShowStats);
        Assert.True(game.NeedsRedraw);

        game.OnKeyDown("KeyF");
        Assert.False(game.ShowStats);
    }

    [Fact]
    public void MSilencesTheBoardAndSaysItIsWorthSaving()
    {
        var game = Sized();
        int saves = 0;
        game.ScoreChanged += () => saves++;

        game.OnKeyDown("KeyM");

        Assert.True(game.Muted);
        Assert.Equal(1, saves);
    }

    [Fact]
    public void RDealsAgain()
    {
        var game = Sized();
        int before = game.Board.Version;
        game.Board.DealFromStock();

        game.OnKeyDown("KeyR");

        Assert.Equal(24, game.Board.FaceDownPile.Count);
        Assert.Empty(game.Board.FaceUpPile);
        Assert.NotEqual(before, game.Board.Version);
    }

    [Fact]
    public void AKeyTheBoardHasNoUseForDoesNothing()
    {
        var game = Sized();

        game.OnKeyDown("KeyQ");

        Assert.False(game.ShowStats);
        Assert.False(game.Muted);
        Assert.Null(game.Error);
    }

    [Fact]
    public void TheMuteButtonTakesThePressBeforeTheFeltDoes()
    {
        var game = Sized();
        var (x, y) = Centre(game.Layout.MuteButton);

        game.OnPointerDown(x, y);

        Assert.True(game.Muted);
    }

    [Fact]
    public void TheReleaseAfterAButtonPressIsSwallowed()
    {
        // PointerInput never saw the press, and left to itself it would read the release as
        // a tap at wherever the last real press happened to be.
        var game = Sized();
        var (stockX, stockY) = Centre(game.Layout.CardRect(Stock, 0));

        game.OnPointerDown(stockX, stockY);
        game.OnPointerUp(stockX, stockY);
        Assert.Single(game.Board.FaceUpPile); // an ordinary tap deals

        var (muteX, muteY) = Centre(game.Layout.MuteButton);
        game.OnPointerDown(muteX, muteY);
        game.OnPointerUp(muteX, muteY);

        Assert.Single(game.Board.FaceUpPile); // the swallowed release dealt nothing more
    }

    [Fact]
    public void TheFastForwardButtonIsOnlyPressableWhileItIsOffered()
    {
        var game = Sized();
        var (x, y) = Centre(game.Layout.FastForwardButton);

        Assert.False(game.CanFastForward); // a fresh deal still has cards face down
        game.OnPointerDown(x, y);
        game.OnPointerUp(x, y);
        game.Update(TimeSpan.FromMilliseconds(16));

        Assert.Equal(0, game.Board.FoundationTotal);
    }

    [Fact]
    public void SpaceStartsTheFinishAndItPlaysTheBoardHome()
    {
        var game = Sized();
        LoadFourKings(game);
        Assert.True(game.CanFastForward);

        game.OnKeyDown("Space");

        // A card every few frames rather than all at once, so give it plenty of frames.
        for (int frame = 0; frame < 200 && game.State == GameState.Playing; frame++)
        {
            game.Update(TimeSpan.FromMilliseconds(frame * 16));
        }

        Assert.Equal(GameState.Won, game.State);
        Assert.False(game.CanFastForward);
    }

    [Fact]
    public void TheFinishIsPacedRatherThanInstant()
    {
        // The player asked to skip the clicking, not to skip seeing the game finish.
        var game = Sized();
        LoadFourKings(game);

        game.OnKeyDown("Space");
        game.Update(TimeSpan.FromMilliseconds(16));

        Assert.Equal(GameState.Playing, game.State);
        Assert.True(game.Board.FoundationTotal < 52, "the whole finish happened inside one frame");
    }

    [Fact]
    public void NothingIsGrabbableWhileTheBoardPlaysItselfOut()
    {
        var game = Sized();
        LoadFourKings(game);
        var (x, y) = Centre(game.Layout.CardRect(Tableau(1), 0));

        game.OnPointerMove(x, y);
        Assert.NotNull(game.HoverPile);

        game.OnKeyDown("Space");
        game.Update(TimeSpan.FromMilliseconds(16));

        Assert.Null(game.HoverPile);
    }

    [Fact]
    public void AFinishedDealIsCountedOnceAndOnlyOnce()
    {
        var game = Sized();
        LoadFourKings(game);
        int saves = 0;
        game.ScoreChanged += () => saves++;

        game.OnKeyDown("Space");

        for (int frame = 0; frame < 200; frame++)
        {
            game.Update(TimeSpan.FromMilliseconds(frame * 16));
        }

        Assert.Equal(GameState.Won, game.State);
        Assert.Equal(1, game.Score.Games);
        Assert.Equal(1, game.Score.Wins);
        Assert.Equal(1, saves); // dozens of frames pass with the board already won
    }

    [Fact]
    public void ANewDealIsCountedSeparately()
    {
        var game = Sized();
        LoadFourKings(game);

        game.OnKeyDown("Space");

        for (int frame = 0; frame < 200; frame++)
        {
            game.Update(TimeSpan.FromMilliseconds(frame * 16));
        }

        game.OnKeyDown("KeyR");
        LoadFourKings(game);
        game.OnKeyDown("Space");

        for (int frame = 0; frame < 200; frame++)
        {
            game.Update(TimeSpan.FromMilliseconds(frame * 16));
        }

        Assert.Equal(2, game.Score.Games);
        Assert.Equal(2, game.Score.Wins);
    }

    [Fact]
    public void ALostDealCountsAsAGameButNotAWin()
    {
        var game = Sized();
        var board = game.Board;
        ClearBoard(board);
        board.TableauPiles[0].Add(Up(Suit.Hearts, Rank.King));
        board.TableauPiles[1].Add(Up(Suit.Spades, Rank.Queen));
        board.MakeMove(new Move(Tableau(1), Tableau(0), 1));

        game.Update(TimeSpan.FromMilliseconds(16));

        Assert.Equal(GameState.Stuck, game.State);
        Assert.Equal(1, game.Score.Games);
        Assert.Equal(0, game.Score.Wins);
    }

    [Fact]
    public void ANicknameIsTrimmedAndReportedForSaving()
    {
        var game = Sized();
        int saves = 0;
        game.ScoreChanged += () => saves++;

        game.SetNickname("  Brandon  ");

        Assert.Equal("Brandon", game.Score.Nickname);
        Assert.Equal(1, saves);
        Assert.True(game.NeedsRedraw);
    }

    [Fact]
    public void TheSearchEventuallyAnswersOnAFreshDeal()
    {
        var game = Sized();

        // A slice per frame, so it takes however many frames it takes.
        for (int frame = 0; frame < 3000 && game.Analysis == SolveResult.Searching; frame++)
        {
            game.Update(TimeSpan.FromMilliseconds(frame * 16));
        }

        Assert.NotEqual(SolveResult.Searching, game.Analysis);
        Assert.True(game.AnalysisNodes > 0);
    }

    [Fact]
    public void TheSearchStandsAsideWhileAStackIsHeld()
    {
        // Everything is on one thread, so a slice spent mid-drag comes straight out of the
        // only motion the player can see.
        var game = Sized();
        var (x, y) = Centre(game.Layout.CardRect(Tableau(6), 6));

        game.OnPointerDown(x, y);
        game.OnPointerMove(x + 60, y + 60);
        Assert.NotNull(game.Drag);

        game.Update(TimeSpan.FromMilliseconds(16));
        int duringDrag = game.AnalysisNodes;

        game.OnPointerUp(x + 60, y + 60);
        game.Update(TimeSpan.FromMilliseconds(32));

        Assert.Equal(0, duringDrag);
        Assert.True(game.AnalysisNodes > 0);
    }

    [Fact]
    public void TheBoardIsNotOfferedAFinishWhileAStackIsHeld()
    {
        var game = Sized();
        LoadFourKings(game);
        var (x, y) = Centre(game.Layout.CardRect(Tableau(1), 0));

        game.OnPointerDown(x, y);
        game.OnPointerMove(x + 60, y + 60);

        Assert.False(game.CanFastForward);
    }

    private static void ClearBoard(Board board)
    {
        board.FaceDownPile.Clear();
        board.FaceUpPile.Clear();

        foreach (var pile in board.FoundationPiles)
        {
            pile.Clear();
        }

        foreach (var pile in board.TableauPiles)
        {
            pile.Clear();
        }
    }

    /// <summary>Puts the session's own board one move per king from finished.</summary>
    private static void LoadFourKings(Solitaire game)
    {
        var board = game.Board;
        ClearBoard(board);

        for (int i = 0; i < 4; i++)
        {
            var suit = (Suit)i;

            for (int rank = (int)Rank.Ace; rank <= (int)Rank.Queen; rank++)
            {
                board.FoundationPiles[i].Add(Up(suit, (Rank)rank));
            }

            board.TableauPiles[i].Add(Up(suit, Rank.King));
        }

        // One real move, so the board works out that it is now finishable.
        board.MakeMove(new Move(Tableau(0), Foundation(0), 1));
    }
}
