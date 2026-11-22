using System.Text.Json;
using BSolitaire.Game;
using Xunit;
using static BSolitaire.Tests.Positions;

namespace BSolitaire.Tests;

/// <summary>
/// The pieces the session hands a slice of each frame to, asked directly. Going through
/// <see cref="Solitaire"/> covers how they fit together; this covers the corners that are hard
/// to reach from there — a stalled finish, a search told to stand down, a deal counted twice,
/// a deal thrown away before anyone saw it.
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

        // And the board is left alone. The verdict is about the position, not a thing done
        // to the game: the player can still move, and the deal is not over until the board
        // says it is.
        Assert.Equal(GameState.Playing, board.State);
    }

    [Fact]
    public void TheSearchRemembersTheLastPositionItCouldStillWin()
    {
        // Somewhere to send the player back to, once they have played themselves out of it.
        // A board four moves from done rather than a fresh deal: most openings the search
        // runs out of budget on, and an anchor is only ever a position it *proved*.
        var board = FourKingsFromDone();
        var analyzer = new Analyzer(board);

        Settle(analyzer);

        Assert.Equal(SolveResult.Winnable, analyzer.Result);
        Assert.Equal(0, analyzer.WinnableDepth);

        board.FastForwardStep();
        Settle(analyzer);

        Assert.Equal(1, analyzer.WinnableDepth);
        Assert.Equal(board.UndoDepth, analyzer.WinnableDepth);
    }

    [Fact]
    public void AnAnchorIsNeverAheadOfWhereTheDealHasGotTo()
    {
        // Backing up is a loop that undoes until it reaches the anchor. An anchor deeper
        // than the board has got to is a loop with nothing to stop it.
        var board = FourKingsFromDone();
        var analyzer = new Analyzer(board);

        board.FastForwardStep();
        board.FastForwardStep();
        Settle(analyzer);

        Assert.Equal(2, analyzer.WinnableDepth);

        board.Undo();
        board.Undo();
        Settle(analyzer);

        Assert.True(
            analyzer.WinnableDepth <= board.UndoDepth,
            $"the anchor is at {analyzer.WinnableDepth} and the deal is at {board.UndoDepth}");
    }

    [Fact]
    public void AFreshDealKeepsNoneOfTheLastOnesAnchor()
    {
        var board = FourKingsFromDone();
        var analyzer = new Analyzer(board);

        board.FastForwardStep();
        board.FastForwardStep();
        board.FastForwardStep();
        Settle(analyzer);

        Assert.Equal(3, analyzer.WinnableDepth);

        board.Reset();
        analyzer.Update(paused: false);

        // Nobody has moved anything on the new deal, so there is nothing behind the player
        // to go back to, whatever was true of the last one.
        Assert.True(analyzer.WinnableDepth <= 0);
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

    /// <summary>
    /// The one that matters: a field that is written and not read back is worse than one that
    /// was never saved, and that is exactly what happened to ShowStats. Round-tripping every
    /// field through the same pair the host uses catches the next one for free.
    /// </summary>
    [Fact]
    public void EveryPartOfTheRecordSurvivesBeingStoredAndLoaded()
    {
        var written = new ScoreKeeper(new Board());
        written.Score.Nickname = "Brandon";
        written.Score.Games = 7;
        written.Score.Wins = 3;
        written.Score.Muted = true;
        written.Score.ShowStats = true;

        var read = new ScoreKeeper(new Board());
        Assert.True(read.Load(written.ToJson()));

        Assert.Equal("Brandon", read.Score.Nickname);
        Assert.Equal(7, read.Score.Games);
        Assert.Equal(3, read.Score.Wins);
        Assert.True(read.Score.Muted);
        Assert.True(read.Score.ShowStats);
    }

    [Fact]
    public void AnUnreadableRecordIsStartedOverRatherThanThrown()
    {
        var scores = new ScoreKeeper(new Board());
        scores.Score.Games = 4;

        Assert.False(scores.Load("{ not json at all"));
        Assert.Equal(4, scores.Score.Games);
    }

    [Fact]
    public void AnAverageOfNothingIsNotZero()
    {
        // A player with no wins has no average win. Zero there would read as instant ones.
        var fresh = new PlayerScore();

        Assert.Null(fresh.AverageGameSeconds);
        Assert.Null(fresh.AverageWinSeconds);

        var played = new PlayerScore
        {
            TimedGames = 4,
            TotalGameSeconds = 200,
            TimedWins = 1,
            TotalWinSeconds = 80,
        };

        Assert.Equal(50, played.AverageGameSeconds);
        Assert.Equal(80, played.AverageWinSeconds);
    }

    [Fact]
    public void EveryTimeInTheRecordSurvivesBeingStoredAndLoaded()
    {
        // CopyFrom is field by field, and this change adds four fields to it.
        var scores = new ScoreKeeper(new Board());
        scores.Score.TimedGames = 7;
        scores.Score.TotalGameSeconds = 700;
        scores.Score.TimedWins = 3;
        scores.Score.TotalWinSeconds = 360;

        var reloaded = new ScoreKeeper(new Board());

        Assert.True(reloaded.Load(scores.ToJson()));
        Assert.Equal(7, reloaded.Score.TimedGames);
        Assert.Equal(700, reloaded.Score.TotalGameSeconds);
        Assert.Equal(3, reloaded.Score.TimedWins);
        Assert.Equal(360, reloaded.Score.TotalWinSeconds);
    }

    [Fact]
    public void ARecordFromBeforeAnythingWasTimedLoadsWithNoTimes()
    {
        // A thousand games already played have no times against them, and must not be
        // averaged as though they took no time at all.
        var scores = new ScoreKeeper(new Board());

        Assert.True(scores.Load("{\"Games\":1000,\"Wins\":150}"));

        Assert.Equal(1000, scores.Score.Games);
        Assert.Equal(0, scores.Score.TimedGames);
        Assert.Null(scores.Score.AverageGameSeconds);
    }

    [Fact]
    public void TheRecordCanBeShownWithoutTheName()
    {
        // The name is the longest part of the line and the part the player already knows,
        // so it is what goes when the bottom of the board runs out of room.
        var score = new PlayerScore { Nickname = "Brandon", Games = 1204, Wins = 187 };

        Assert.Contains("Brandon", score.Summary);
        Assert.DoesNotContain("Brandon", score.Tallies);
        Assert.Contains("Games: 1204", score.Tallies);
        Assert.Contains("Wins: 187", score.Tallies);
        Assert.EndsWith(score.Tallies, score.Summary);

        // Neither is part of the record itself.
        string json = JsonSerializer.Serialize(score);
        Assert.DoesNotContain("Summary", json);
        Assert.DoesNotContain("Tallies", json);
    }

    [Fact]
    public void ARecordFromAnOlderVersionKeepsWhatItHas()
    {
        var scores = new ScoreKeeper(new Board());

        Assert.True(scores.Load("{\"Nickname\":\"Ada\",\"Games\":2}"));

        Assert.Equal("Ada", scores.Score.Nickname);
        Assert.Equal(2, scores.Score.Games);
        Assert.False(scores.Score.ShowStats);
    }

    [Fact]
    public void TheDrawSurvivesBeingStoredAndLoaded()
    {
        // CopyFrom is field by field and its own comment records that a field was forgotten
        // there once. This is the field after that one.
        var scores = new ScoreKeeper(new Board());
        scores.ToggleDrawMode();

        var reloaded = new ScoreKeeper(new Board());

        Assert.True(reloaded.Load(scores.ToJson()));
        Assert.Equal(DrawMode.DrawOne, reloaded.Score.DrawMode);
    }

    [Fact]
    public void ARecordFromBeforeTheSettingExistedDealsThree()
    {
        // Three is how the game plays now, so a record saved before there was a choice gets
        // the same game as a record saved after it.
        var scores = new ScoreKeeper(new Board());

        Assert.True(scores.Load("{\"Nickname\":\"Ada\",\"Games\":2}"));
        Assert.Equal(DrawMode.DrawThree, scores.Score.DrawMode);
    }

    [Fact]
    public void AWonDealIsTimedAndSoIsTheWin()
    {
        var board = FourKingsFromDone();
        var scores = new ScoreKeeper(board);
        double now = 0;

        scores.Tick(now);
        board.FastForwardStep();
        Play(scores, ref now, seconds: 1);

        while (board.State == GameState.Playing)
        {
            board.FastForwardStep();
        }

        scores.Tick(now += 16);

        Assert.True(scores.Update());
        Assert.Equal(1, scores.Score.TimedGames);
        Assert.Equal(1, scores.Score.TimedWins);
        Assert.Equal(1, scores.Score.TotalGameSeconds);
        Assert.Equal(1, scores.Score.TotalWinSeconds);
    }

    [Fact]
    public void ALostDealIsTimedButIsNoWin()
    {
        // Two aces to send home, and two kings that can only swap which column is empty.
        // The deal is alive until the second ace goes, which is what the clock has to
        // measure — the time before the move that ended it, not after.
        var board = Empty();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.King));
        board.Position.Place(Tableau(1), Up(Suit.Spades, Rank.King));
        board.Position.Place(Tableau(2), Up(Suit.Diamonds, Rank.Ace));
        board.Position.Place(Tableau(3), Up(Suit.Clubs, Rank.Ace));
        board.Settle();

        var scores = new ScoreKeeper(board);
        double now = 0;

        scores.Tick(now);
        Assert.True(board.MakeMove(new Move(Tableau(2), Foundation(0), 1)));
        Assert.Equal(GameState.Playing, board.State);

        Play(scores, ref now, seconds: 2);

        Assert.True(board.MakeMove(new Move(Tableau(3), Foundation(1), 1)));
        scores.Tick(now += 16);

        Assert.Equal(GameState.Stuck, board.State);
        Assert.True(scores.Update());

        Assert.Equal(1, scores.Score.TimedGames);
        Assert.Equal(0, scores.Score.TimedWins);
        Assert.Equal(2, scores.Score.TotalGameSeconds);
    }

    [Fact]
    public void ADealWalkedAwayFromPartPlayedStillCounts()
    {
        // The games people give up on are the slow ones. Leaving them out would make every
        // average flatter than the play that earned it.
        var board = new Board();
        var scores = new ScoreKeeper(board);
        double now = 0;

        scores.Tick(now);
        board.DealFromStock();
        Play(scores, ref now, seconds: 3);

        Assert.True(scores.Abandon());

        Assert.Equal(1, scores.Score.Games);
        Assert.Equal(0, scores.Score.Wins);
        Assert.Equal(1, scores.Score.TimedGames);
        Assert.Equal(3, scores.Score.TotalGameSeconds);

        // And once only, however it is asked.
        Assert.False(scores.Abandon());
        Assert.Equal(1, scores.Score.Games);
    }

    [Fact]
    public void ADealNobodyTouchedIsNotAGameTheyLost()
    {
        var board = new Board();
        var scores = new ScoreKeeper(board);

        scores.Tick(0);
        scores.Tick(16);

        Assert.False(scores.Abandon());
        Assert.Equal(0, scores.Score.Games);
        Assert.Equal(0, scores.Score.TimedGames);
    }

    [Fact]
    public void TimeSpentAwayFromTheTabDoesNotCount()
    {
        // The frame loop stops while the tab is hidden; the clock it reports does not. Come
        // back after a minute and the next frame arrives a minute after the last one.
        var board = new Board();
        var scores = new ScoreKeeper(board);
        double now = 0;

        scores.Tick(now);
        board.DealFromStock();
        Play(scores, ref now, seconds: 1);

        now += 60_000;
        scores.Tick(now);

        Play(scores, ref now, seconds: 1);

        Assert.True(scores.Abandon());
        Assert.Equal(2, scores.Score.TotalGameSeconds);
    }

    [Fact]
    public void AFinishedDealIsTimedOnceHoweverManyFramesPass()
    {
        var board = FourKingsFromDone();
        var scores = new ScoreKeeper(board);

        scores.Tick(0);

        while (board.State == GameState.Playing)
        {
            board.FastForwardStep();
        }

        Assert.True(scores.Update());
        Assert.False(scores.Update());
        Assert.False(scores.Update());

        Assert.Equal(1, scores.Score.TimedGames);
        Assert.Equal(1, scores.Score.TimedWins);
    }

    [Fact]
    public void TheRunningClockCountsOnlyTimePlayed()
    {
        var board = new Board();
        var scores = new ScoreKeeper(board);
        double now = 0;

        scores.Tick(now);
        Assert.Equal(0, scores.ElapsedSeconds);

        // A board nobody has touched is not a board being played.
        Play(scores, ref now, seconds: 2);
        Assert.Equal(0, scores.ElapsedSeconds);

        board.DealFromStock();
        Play(scores, ref now, seconds: 2);
        Assert.Equal(2, scores.ElapsedSeconds);

        // A minute with the tab elsewhere arrives as one enormous gap and counts for none
        // of it.
        now += 60_000;
        scores.Tick(now);
        Assert.Equal(2, scores.ElapsedSeconds);

        Play(scores, ref now, seconds: 1);
        Assert.Equal(3, scores.ElapsedSeconds);
    }

    [Fact]
    public void TheRunningClockStopsWhenTheDealDoes()
    {
        var board = FourKingsFromDone();
        var scores = new ScoreKeeper(board);
        double now = 0;

        scores.Tick(now);
        board.FastForwardStep();
        Play(scores, ref now, seconds: 1);

        while (board.State == GameState.Playing)
        {
            board.FastForwardStep();
        }

        long atTheEnd = scores.ElapsedSeconds;
        Play(scores, ref now, seconds: 3);

        Assert.Equal(atTheEnd, scores.ElapsedSeconds);
    }

    [Fact]
    public void TheClockOnTheBoardAgreesWithTheOneInTheRecord()
    {
        // Both floor. A deal the player watched reach 2:05 must not be recorded as 2:06.
        var board = new Board();
        var scores = new ScoreKeeper(board);
        double now = 0;

        scores.Tick(now);
        board.DealFromStock();
        Play(scores, ref now, seconds: 2);

        // Most of another second, which neither of them should round up.
        for (int frame = 0; frame < 115; frame++)
        {
            now += 8;
            scores.Tick(now);
        }

        long onTheBoard = scores.ElapsedSeconds;

        Assert.True(scores.Abandon());
        Assert.Equal(onTheBoard, scores.Score.TotalGameSeconds);
    }

    // -- Hinter -----------------------------------------------------------------------

    [Fact]
    public void AHintNamesAMoveTheSearchCanVouchFor()
    {
        // Four kings from home: every one of them going home keeps the deal winnable, so
        // the first candidate tried is certifiable and the hint is a foundation move.
        var board = FourKingsFromDone();
        var hinter = new Hinter(board);

        hinter.Ask();

        for (int frame = 0; frame < 2000 && hinter.State == HintState.Looking; frame++)
        {
            hinter.Update(paused: false);
        }

        Assert.Equal(HintState.Found, hinter.State);
        Assert.NotNull(hinter.Hint);
        Assert.Equal(PileKind.Foundation, hinter.Hint!.Value.To.Kind);

        // And it is a move the board will actually take.
        Assert.True(Rules.IsLegal(board.Position, hinter.Hint.Value));
    }

    [Fact]
    public void ALostPositionGetsNoHint()
    {
        // Nothing here can be vouched for, because nothing here wins. The answer is that
        // none could be certified — which is a different sentence from "there is no move",
        // and the one the game is allowed to say.
        var board = Empty();
        board.Position.Place(Tableau(0), Up(Suit.Spades, Rank.King));
        board.Position.Place(Tableau(1), Up(Suit.Hearts, Rank.Queen));
        board.Settle();

        var hinter = new Hinter(board);
        hinter.Ask();

        for (int frame = 0; frame < 4000 && hinter.State == HintState.Looking; frame++)
        {
            hinter.Update(paused: false);
        }

        Assert.Equal(HintState.NoneCertain, hinter.State);
        Assert.Null(hinter.Hint);
    }

    [Fact]
    public void AHintIsForgottenOnceTheBoardMoves()
    {
        var board = FourKingsFromDone();
        var hinter = new Hinter(board);

        hinter.Ask();

        for (int frame = 0; frame < 2000 && hinter.State == HintState.Looking; frame++)
        {
            hinter.Update(paused: false);
        }

        Assert.Equal(HintState.Found, hinter.State);

        board.FastForwardStep();
        hinter.Update(paused: false);

        Assert.Equal(HintState.Idle, hinter.State);
        Assert.Null(hinter.Hint);
    }

    [Fact]
    public void AHintWaitsWhileTheBoardIsBusy()
    {
        var board = FourKingsFromDone();
        var hinter = new Hinter(board);

        hinter.Ask();

        for (int frame = 0; frame < 200; frame++)
        {
            hinter.Update(paused: true);
        }

        Assert.Equal(HintState.Looking, hinter.State);
        Assert.Null(hinter.Hint);
    }

    [Fact]
    public void AskingTwicePutsTheHintAway()
    {
        var board = FourKingsFromDone();
        var hinter = new Hinter(board);

        hinter.Ask();

        for (int frame = 0; frame < 2000 && hinter.State == HintState.Looking; frame++)
        {
            hinter.Update(paused: false);
        }

        Assert.Equal(HintState.Found, hinter.State);

        hinter.Ask();

        Assert.Equal(HintState.Idle, hinter.State);
        Assert.Null(hinter.Hint);
    }

    [Fact]
    public void LookingForAHintLeavesTheRealBoardAlone()
    {
        // Candidates are played on a board of its own. If they were played on this one the
        // player would watch the cards move while they waited for advice about them.
        var board = FourKingsFromDone();
        var hinter = new Hinter(board);
        int version = board.Version;
        var before = board.Position.FoundationPiles.Select(pile => pile.Count).ToArray();

        hinter.Ask();

        for (int frame = 0; frame < 2000 && hinter.State == HintState.Looking; frame++)
        {
            hinter.Update(paused: false);
        }

        Assert.Equal(version, board.Version);
        Assert.Equal(before, board.Position.FoundationPiles.Select(pile => pile.Count).ToArray());
    }

    // -- DealVetter -------------------------------------------------------------------

    [Fact]
    public void AVettedDealIsOneWithAWinInIt()
    {
        // The whole promise of the thing: what it hands over has been proved winnable, not
        // merely not-yet-proved-lost.
        var vetter = new DealVetter();

        for (int frame = 0; frame < 4000 && !vetter.HasDeal; frame++)
        {
            vetter.Update(paused: false);
        }

        Assert.True(vetter.HasDeal, "no deal was vetted in four thousand frames");

        var deal = vetter.TakeDeal();
        Assert.NotNull(deal);

        var board = new Board();
        board.Reset(deal);

        Assert.Equal(SolveResult.Winnable, Solve(board));
    }

    [Fact]
    public void ADealAskedForBeforeOneIsReadyIsStillAWinnableOne()
    {
        // The first deal of a session: nothing has been vetted in the background, so the
        // search runs there and then. The wait is capped, so on slow hardware a single ask
        // can come back empty — asking again is what a player pressing New Game would do.
        var vetter = new DealVetter();
        IReadOnlyList<(Suit Suit, Rank Rank)>? deal = null;

        for (int attempt = 0; attempt < 10 && deal is null; attempt++)
        {
            deal = vetter.TakeDeal();
        }

        Assert.NotNull(deal);
        Assert.Equal(52, deal.Count);

        var board = new Board();
        board.Reset(deal);

        Assert.Equal(SolveResult.Winnable, Solve(board));
    }

    [Fact]
    public void ADealHandedOutWithoutBeingVettedIsCounted()
    {
        // Rare by construction, and silent until now. If it ever moves in real play, the
        // attempt count or the wait is wrong for that machine, and that is worth seeing.
        var vetter = new DealVetter();

        Assert.Equal(0, vetter.Unvetted);

        // Nothing has been prepared, so this searches there and then — and either finds
        // one inside its ceiling or gives up and says so.
        var deal = vetter.TakeDeal();

        if (deal is null)
        {
            Assert.Equal(1, vetter.Unvetted);
        }
        else
        {
            Assert.Equal(0, vetter.Unvetted);
        }
    }

    [Fact]
    public void AVettedDealIsNeverCountedAsUnvetted()
    {
        var vetter = new DealVetter();

        for (int frame = 0; frame < 4000 && !vetter.HasDeal; frame++)
        {
            vetter.Update(paused: false);
        }

        Assert.True(vetter.HasDeal);
        Assert.NotNull(vetter.TakeDeal());
        Assert.Equal(0, vetter.Unvetted);
    }

    [Fact]
    public void AVetterToldToStandDownDoesNothing()
    {
        var vetter = new DealVetter();

        for (int frame = 0; frame < 100; frame++)
        {
            vetter.Update(paused: true);
        }

        Assert.False(vetter.HasDeal);
        Assert.Equal(0, vetter.Discarded);
    }

    [Fact]
    public void ADealIsHandedOverOnceAndThenLookedForAgain()
    {
        var vetter = new DealVetter();

        for (int frame = 0; frame < 4000 && !vetter.HasDeal; frame++)
        {
            vetter.Update(paused: false);
        }

        Assert.True(vetter.HasDeal);
        Assert.NotNull(vetter.TakeDeal());
        Assert.False(vetter.HasDeal);
    }

    /// <summary>Frames at sixty hertz, enough of them to add up to the seconds asked for.
    /// One long jump instead would be thrown away as time spent off the tab.</summary>
    private static void Play(ScoreKeeper scores, ref double now, int seconds)
    {
        for (int frame = 0; frame < seconds * 125; frame++)
        {
            now += 8;
            scores.Tick(now);
        }
    }

    /// <summary>Gives the search frames until it has an answer about the position it is on.
    /// Keyed on the result and not on Done, which is true before the first frame because
    /// there is no search yet.</summary>
    private static void Settle(Analyzer analyzer)
    {
        // One frame first, unconditionally: until it runs, the result on offer is still the
        // last position's, and a loop that reads it would decide it had nothing to do.
        analyzer.Update(paused: false);

        for (int frame = 0; frame < 2000 && analyzer.Result == SolveResult.Searching; frame++)
        {
            analyzer.Update(paused: false);
        }
    }

    /// <summary>Runs a search to the end. Generous, because this is not a frame-budget test.</summary>
    private static SolveResult Solve(Board board)
    {
        var solver = new Solver(board.Position);

        while (!solver.Done)
        {
            solver.Step(1_000_000, TimeSpan.FromSeconds(10));
        }

        return solver.Result;
    }
}
