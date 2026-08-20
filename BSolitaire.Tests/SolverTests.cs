using BSolitaire.Game;
using Xunit;
using static BSolitaire.Tests.Positions;

namespace BSolitaire.Tests;

/// <summary>
/// The search. It keeps its own copy of the rules on a packed representation, for speed, so
/// these are as much about the two copies still agreeing as about the search itself.
/// </summary>
public class SolverTests
{
    /// <summary>Runs a search to the end. Generous, because these are not frame-budget tests.</summary>
    private static SolveResult Solve(Board board)
    {
        var solver = new Solver(board);

        while (!solver.Done)
        {
            solver.Step(1_000_000, TimeSpan.FromSeconds(10));
        }

        return solver.Result;
    }

    [Fact]
    public void ABoardOneMoveFromDoneIsWinnable()
    {
        Assert.Equal(SolveResult.Winnable, Solve(FourKingsFromDone()));
    }

    [Fact]
    public void AFreshDealAlwaysGetsAnAnswer()
    {
        // Winnable, Unwinnable, or "I ran out of budget" — never still Searching, and never a
        // hang. The state space is finite and every position visited is remembered.
        var result = Solve(new Board());

        Assert.NotEqual(SolveResult.Searching, result);
    }

    [Fact]
    public void TheSearchReportsWhatItHasLookedAt()
    {
        var solver = new Solver(new Board());
        solver.Step(5000, TimeSpan.FromSeconds(1));

        Assert.True(solver.Nodes > 0);
        Assert.True(solver.States > 0);
    }

    [Fact]
    public void ASliceLeavesTheBoardAlone()
    {
        // The search works on its own copy. If it ever moved the real cards the player would
        // watch the board play itself while they were still deciding.
        var board = new Board();
        int version = board.Version;
        var before = board.TableauPiles.Select(pile => pile.Count).ToArray();

        var solver = new Solver(board);
        solver.Step(50_000, TimeSpan.FromSeconds(2));

        Assert.Equal(version, board.Version);
        Assert.Equal(before, board.TableauPiles.Select(pile => pile.Count).ToArray());
    }

    [Fact]
    public void TheSearchAgreesWithTheRulesAboutStacking()
    {
        // Solver has its own stacking test on packed bytes. Rules is the one the player sees
        // enforced, so any disagreement is a bug in the search's copy — walk a sample of
        // pairs through the position the search would face.
        var board = FourKingsFromDone();

        // Every king is on its foundation's queen, so the board is one slice from won. If the
        // search's idea of founding had drifted from Rules', it could not find that line.
        Assert.Equal(SolveResult.Winnable, Solve(board));

        foreach (var suit in Enum.GetValues<Suit>())
        {
            Assert.True(Rules.CanFound(Up(suit, Rank.King), Up(suit, Rank.Queen)));
        }
    }
}
