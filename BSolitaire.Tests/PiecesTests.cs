using System.Text.Json;
using BSolitaire.Game;
using Xunit;
using static BSolitaire.Tests.Positions;

namespace BSolitaire.Tests;

/// <summary>
/// The three pieces the session hands a slice of each frame to, asked directly. Going through
/// <see cref="Solitaire"/> covers how they fit together; this covers the corners that are hard
/// to reach from there — a stalled finish, a search told to stand down, a deal counted twice.
/// </summary>
public class PiecesTests
{
    // -- FastForward ------------------------------------------------------------------

    [Fact]
    public void AFinishRunsUntilTheBoardIsDone()
    {
        var board = FourKingsFromDone();
        var fastForward = new FastForward(board);
        fastForward.Start();

        for (int frame = 0; frame < 200 && fastForward.IsRunning; frame++)
        {
            fastForward.Tick();
        }

        Assert.False(fastForward.IsRunning);
        Assert.Equal(GameState.Won, board.State);
    }

    [Fact]
    public void AFinishSpendsMostFramesWaiting()
    {
        // Instant would be a worse answer than fast: the player asked to skip the clicking,
        // not to skip seeing the game finish.
        var board = FourKingsFromDone();
        var fastForward = new FastForward(board);
        fastForward.Start();

        int idle = 0;
        for (int frame = 0; frame < 12; frame++)
        {
            if (fastForward.Tick() == FastForwardTick.Idle)
            {
                idle++;
            }
        }

        Assert.True(idle > 0, "every frame moved a card");
    }

    [Fact]
    public void AFinishThatStopsMakingProgressGivesUp()
    {
        // Turning the stock over forever without finding anything to play. It cannot happen
        // from a position that offers the button, but a loop that never ends is a worse bug
        // than one card left unplayed.
        var board = Empty();
        board.Position.Place(Stock, Down(Suit.Hearts, Rank.Five));  // nothing it can ever go on
        board.Position.Place(Tableau(0), Up(Suit.Spades, Rank.King));
        board.Position.Place(Tableau(1), Up(Suit.Hearts, Rank.Queen)); // a move exists, so not stuck

        var fastForward = new FastForward(board);
        fastForward.Start();

        int frames = 0;
        while (fastForward.IsRunning && frames++ < 1000)
        {
            fastForward.Tick();
        }

        Assert.False(fastForward.IsRunning);
        Assert.True(frames < 1000, "the finish never gave up");
        Assert.Equal(GameState.Playing, board.State);
    }

    [Fact]
    public void AFinishThatIsStoppedStaysStopped()
    {
        var board = FourKingsFromDone();
        var fastForward = new FastForward(board);

        fastForward.Start();
        fastForward.Stop();

        Assert.False(fastForward.IsRunning);
    }

    // -- Analyzer ---------------------------------------------------------------------

    [Fact]
    public void TheSearchDoesNothingWhileItIsToldToStandDown()
    {
        var analyzer = new Analyzer(new Board());

        analyzer.Update(paused: true);

        Assert.Equal(0, analyzer.Nodes);
        Assert.Equal(SolveResult.Searching, analyzer.Result);
    }

    [Fact]
    public void TheSearchGetsGoingWhenItIsNot()
    {
        var analyzer = new Analyzer(new Board());

        analyzer.Update(paused: false);

        Assert.True(analyzer.Nodes > 0);
    }

    [Fact]
    public void TheSearchStartsOverWhenTheBoardChanges()
    {
        // A move can turn a lost position into a won one and back, so the honest thing is to
        // begin again rather than repair what the old search had found.
        var board = new Board();
        var analyzer = new Analyzer(board);

        analyzer.Update(paused: false);
        Assert.True(analyzer.Nodes > 0);

        board.Reset();
        analyzer.Update(paused: true); // a new search, given no time yet

        Assert.Equal(0, analyzer.Nodes);
    }

    [Fact]
    public void ThereIsNothingToSearchOnAFinishedBoard()
    {
        var board = FourKingsFromDone();

        while (board.State == GameState.Playing)
        {
            board.FastForwardStep();
        }

        var analyzer = new Analyzer(board);

        Assert.False(analyzer.Update(paused: false));
        Assert.Equal(0, analyzer.Nodes);
    }

    [Fact]
    public void ProvingADealDeadIsReportedToTheBoard()
    {
        // The one thing a search can change about the board it is watching. The position has
        // to still be in play for it to be worth searching at all — a board already known to
        // be stuck is not news anyone needs a proof of.
        var board = Empty();
        board.Position.Place(Tableau(0), Up(Suit.Spades, Rank.King));
        board.Position.Place(Tableau(1), Up(Suit.Hearts, Rank.Queen));
        board.Position.Place(Stock, Down(Suit.Diamonds, Rank.Ace)); // an ace can always go home
        board.DealFromStock();
        Assert.Equal(GameState.Playing, board.State);

        var analyzer = new Analyzer(board);
        bool changed = false;

        for (int frame = 0; frame < 100 && !changed; frame++)
        {
            changed = analyzer.Update(paused: false);
        }

        // Four cards cannot become fifty-two, so every line from here loses.
        Assert.True(changed);
        Assert.Equal(SolveResult.Unwinnable, analyzer.Result);
        Assert.Equal(GameState.Unwinnable, board.State);
    }

    // -- ScoreKeeper ------------------------------------------------------------------

    [Fact]
    public void AGameStillBeingPlayedIsNotCounted()
    {
        var scores = new ScoreKeeper(new Board());

        Assert.False(scores.Update());
        Assert.Equal(0, scores.Score.Games);
    }

    [Fact]
    public void AFinishedDealIsCountedOnceHoweverManyFramesPass()
    {
        var board = FourKingsFromDone();

        while (board.State == GameState.Playing)
        {
            board.FastForwardStep();
        }

        var scores = new ScoreKeeper(board);

        Assert.True(scores.Update());   // the frame that noticed
        Assert.False(scores.Update());  // and every frame after it
        Assert.False(scores.Update());
        Assert.Equal(1, scores.Score.Games);
        Assert.Equal(1, scores.Score.Wins);
    }

    [Fact]
    public void ADealDealtAgainIsCountedAgain()
    {
        // Keyed to the board's version rather than a flag, because a new deal does not always
        // come through the game's own reset — the banner's button resets the board itself.
        var board = FourKingsFromDone();
        var scores = new ScoreKeeper(board);

        while (board.State == GameState.Playing)
        {
            board.FastForwardStep();
        }

        scores.Update();
        board.Reset();
        scores.Update(); // playing again, so nothing to count

        var second = FourKingsFromDone();
        foreach (var kind in new[] { PileKind.Foundation, PileKind.Tableau })
        {
            for (int i = 0; i < board.Position.PileCountOf(kind); i++)
            {
                var loc = new Location(kind, i);
                board.Position.Strip(loc);
                board.Position.Place(loc, second.Position.Pile(loc));
            }
        }

        board.Position.Strip(Stock);
        board.Position.Strip(Waste);

        while (board.State == GameState.Playing)
        {
            board.FastForwardStep();
        }

        Assert.True(scores.Update());
        Assert.Equal(2, scores.Score.Games);
        Assert.Equal(2, scores.Score.Wins);
    }

    [Fact]
    public void ALostDealCountsAsAGameOnly()
    {
        var board = Empty();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.King));
        board.Position.Place(Tableau(1), Up(Suit.Spades, Rank.Queen));
        board.MakeMove(new Move(Tableau(1), Tableau(0), 1));

        var scores = new ScoreKeeper(board);

        Assert.True(scores.Update());
        Assert.Equal(1, scores.Score.Games);
        Assert.Equal(0, scores.Score.Wins);
    }

    [Fact]
    public void RenamingThePlayerIsWorthSaving()
    {
        var scores = new ScoreKeeper(new Board());
        int saves = 0;
        scores.Changed += () => saves++;

        scores.SetNickname("  Brandon  ");

        Assert.Equal("Brandon", scores.Score.Nickname);
        Assert.Equal(1, saves);
    }

    [Fact]
    public void SilencingTheBoardIsWorthSavingToo()
    {
        var scores = new ScoreKeeper(new Board());
        int saves = 0;
        scores.Changed += () => saves++;

        scores.ToggleMute();
        Assert.True(scores.Score.Muted);

        scores.ToggleMute();
        Assert.False(scores.Score.Muted);
        Assert.Equal(2, saves);
    }

    // -- PlayerScore ------------------------------------------------------------------

    [Fact]
    public void ARecordSurvivesBeingWrittenOutAndReadBack()
    {
        // The host round-trips this through localStorage, so the shape of the JSON is a real
        // contract rather than an implementation detail.
        var score = new PlayerScore { Nickname = "Brandon", Games = 12, Wins = 3, Muted = true };

        var restored = JsonSerializer.Deserialize<PlayerScore>(JsonSerializer.Serialize(score));

        Assert.NotNull(restored);
        Assert.Equal("Brandon", restored!.Nickname);
        Assert.Equal(12, restored.Games);
        Assert.Equal(3, restored.Wins);
        Assert.True(restored.Muted);
    }

    [Fact]
    public void TheLineOnTheBoardIsNotPartOfTheRecord()
    {
        var json = JsonSerializer.Serialize(new PlayerScore { Nickname = "Brandon" });

        Assert.DoesNotContain("Summary", json);
    }

    [Fact]
    public void APlayerWithNoNicknameIsStillAPlayer()
    {
        Assert.Contains("Player", new PlayerScore().Summary);
    }
}
