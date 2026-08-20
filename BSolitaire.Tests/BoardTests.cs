using BSolitaire.Game;
using Xunit;
using static BSolitaire.Tests.Positions;

namespace BSolitaire.Tests;

/// <summary>
/// The board: what a deal looks like, what a move does besides moving cards, and how a game
/// ends. Rules says whether a move is allowed; this is what happens when it is.
/// </summary>
public class BoardTests
{
    [Fact]
    public void ADealPutsTwentyEightCardsOnTheTableauAndTheRestInTheStock()
    {
        var board = new Board();

        Assert.Equal(24, board.FaceDownPile.Count);
        Assert.Empty(board.FaceUpPile);

        for (int i = 0; i < 7; i++)
        {
            Assert.Equal(i + 1, board.TableauPiles[i].Count);
        }

        Assert.All(board.FoundationPiles, Assert.Empty);
    }

    [Fact]
    public void ADealShowsTheTopCardOfEveryColumnAndNoOthers()
    {
        var board = new Board();

        foreach (var pile in board.TableauPiles)
        {
            Assert.True(pile[^1].IsFaceUp);

            for (int i = 0; i < pile.Count - 1; i++)
            {
                Assert.False(pile[i].IsFaceUp);
            }
        }
    }

    [Fact]
    public void ADealUsesAWholeDeckWithNoCardTwice()
    {
        var board = new Board();
        var seen = new HashSet<(Suit, Rank)>();

        foreach (var pile in AllPiles(board))
        {
            foreach (var card in pile)
            {
                Assert.True(seen.Add((card.Suit, card.Rank)), $"{card.Rank} of {card.Suit} twice");
            }
        }

        Assert.Equal(52, seen.Count);
    }

    [Fact]
    public void DealingFromTheStockTurnsACardFaceUpOntoTheWaste()
    {
        var board = new Board();

        Assert.True(board.DealFromStock());
        Assert.Equal(23, board.FaceDownPile.Count);
        Assert.Single(board.FaceUpPile);
        Assert.True(board.FaceUpPile[^1].IsFaceUp);
    }

    [Fact]
    public void AnEmptyStockRecyclesTheWasteFaceDown()
    {
        var board = new Board();

        while (board.DealFromStock())
        {
        }

        Assert.Empty(board.FaceDownPile);
        Assert.Equal(24, board.FaceUpPile.Count);

        Assert.True(board.RecycleWaste());
        Assert.Equal(24, board.FaceDownPile.Count);
        Assert.Empty(board.FaceUpPile);
        Assert.All(board.FaceDownPile, card => Assert.False(card.IsFaceUp));
    }

    [Fact]
    public void AnIllegalMoveChangesNothingAndSaysSo()
    {
        var board = Empty();
        board.TableauPiles[0].Add(Up(Suit.Hearts, Rank.Nine));
        board.TableauPiles[1].Add(Up(Suit.Diamonds, Rank.Ten)); // same colour

        Assert.False(board.MakeMove(new Move(Tableau(0), Tableau(1), 1)));
        Assert.Single(board.TableauPiles[0]);
        Assert.Single(board.TableauPiles[1]);
        Assert.Contains(Sound.Invalid, board.Sounds);
    }

    [Fact]
    public void UncoveringATableauCardTurnsItOver()
    {
        var board = Empty();
        board.TableauPiles[0].Add(Down(Suit.Clubs, Rank.Four));
        board.TableauPiles[0].Add(Up(Suit.Hearts, Rank.Nine));
        board.TableauPiles[1].Add(Up(Suit.Spades, Rank.Ten));

        Assert.True(board.MakeMove(new Move(Tableau(0), Tableau(1), 1)));
        Assert.True(board.TableauPiles[0][^1].IsFaceUp);
        Assert.Contains(Sound.Flip, board.Sounds);
    }

    [Fact]
    public void OnlyTheNewlyUncoveredCardTurnsOver()
    {
        // Two face-down cards under the run. Lifting it exposes the upper one and no more —
        // the one below it is still covered and stays hidden.
        var board = Empty();
        board.TableauPiles[0].Add(Down(Suit.Clubs, Rank.Four));
        board.TableauPiles[0].Add(Down(Suit.Clubs, Rank.Three));
        board.TableauPiles[0].Add(Up(Suit.Spades, Rank.Ten));
        board.TableauPiles[0].Add(Up(Suit.Hearts, Rank.Nine));
        board.TableauPiles[1].Add(Up(Suit.Diamonds, Rank.Jack));

        Assert.True(board.MakeMove(new Move(Tableau(0), Tableau(1), 2)));
        Assert.Equal(2, board.TableauPiles[0].Count);
        Assert.True(board.TableauPiles[0][1].IsFaceUp);
        Assert.False(board.TableauPiles[0][0].IsFaceUp);
    }

    [Fact]
    public void AnAcePrefersTheFoundationMatchingItsSuit()
    {
        var board = Empty();

        var home = board.FoundationFor(Up(Suit.Hearts, Rank.Ace));

        Assert.NotNull(home);
        Assert.Equal((int)Suit.Hearts, home!.Value.PileIndex);
    }

    [Fact]
    public void AnAceSettlesForAnyEmptyFoundation()
    {
        var board = Empty();
        board.FoundationPiles[(int)Suit.Hearts].Add(Up(Suit.Spades, Rank.Ace));

        var home = board.FoundationFor(Up(Suit.Hearts, Rank.Ace));

        Assert.NotNull(home);
        Assert.Empty(board.FoundationPiles[home!.Value.PileIndex]);
    }

    [Fact]
    public void ACardWithNowhereToGoHasNoFoundation()
    {
        var board = Empty();

        Assert.Null(board.FoundationFor(Up(Suit.Hearts, Rank.Five)));
    }

    [Fact]
    public void AMoveBumpsTheVersionAndAnIllegalOneDoesNot()
    {
        var board = Empty();
        board.TableauPiles[0].Add(Up(Suit.Hearts, Rank.Ace));
        int before = board.Version;

        Assert.False(board.MakeMove(new Move(Tableau(0), Tableau(1), 1))); // an ace is not a king
        Assert.Equal(before, board.Version);

        Assert.True(board.MakeMove(new Move(Tableau(0), Foundation(0), 1)));
        Assert.NotEqual(before, board.Version);
    }

    [Fact]
    public void TheLastCardHomeWinsTheGame()
    {
        var board = FourKingsFromDone();

        for (int i = 0; i < 4; i++)
        {
            Assert.True(board.MakeMove(new Move(Tableau(i), Foundation(i), 1)));
        }

        Assert.Equal(GameState.Won, board.State);
        Assert.Equal(52, board.FoundationTotal);
        Assert.Contains(Sound.Win, board.Sounds);
    }

    [Fact]
    public void APositionWithNothingLeftToDoIsStuck()
    {
        var board = Empty();
        board.TableauPiles[0].Add(Up(Suit.Hearts, Rank.King));
        board.TableauPiles[1].Add(Up(Suit.Spades, Rank.Queen));
        board.TableauPiles[2].Add(Up(Suit.Hearts, Rank.Two));

        // Any move at all, to make the board work out where it stands.
        board.MakeMove(new Move(Tableau(1), Tableau(0), 1));

        Assert.Equal(GameState.Stuck, board.State);
    }

    [Fact]
    public void TheBoardOffersToFinishOnceNothingIsFaceDown()
    {
        var board = FourKingsFromDone();
        board.MakeMove(new Move(Tableau(0), Foundation(0), 1));

        Assert.True(board.CanFastForward);
    }

    [Fact]
    public void TheBoardDoesNotOfferToFinishWhileCardsAreStillHidden()
    {
        var board = new Board();
        board.DealFromStock();

        Assert.False(board.CanFastForward);
    }

    [Fact]
    public void AFinishableBoardPlaysItselfHome()
    {
        var board = FourKingsFromDone();

        int guard = 0;
        while (board.State == GameState.Playing && guard++ < 100)
        {
            board.FastForwardStep();
        }

        Assert.Equal(GameState.Won, board.State);
        Assert.Equal(52, board.FoundationTotal);
    }

    [Fact]
    public void FastForwardTurnsTheStockOverToReachACard()
    {
        // Nothing on the tableau to play, and the card that can go home is under the stock.
        var board = Empty();
        board.FaceDownPile.Add(Down(Suit.Hearts, Rank.Ace));
        board.TableauPiles[0].Add(Up(Suit.Spades, Rank.King));

        Assert.True(board.FastForwardStep());
        Assert.Single(board.FaceUpPile);

        Assert.True(board.FastForwardStep());
        Assert.Equal(1, board.FoundationTotal);
    }

    [Fact]
    public void ResettingDealsAFreshGame()
    {
        var board = FourKingsFromDone();
        board.Reset();

        Assert.Equal(24, board.FaceDownPile.Count);
        Assert.Equal(GameState.Playing, board.State);
        Assert.Equal(0, board.FoundationTotal);
    }

    [Fact]
    public void AMoveMarksBothPilesForRedraw()
    {
        var board = Empty();
        board.TableauPiles[0].Add(Up(Suit.Hearts, Rank.Ace));
        board.ClearDirty();

        board.MakeMove(new Move(Tableau(0), Foundation(0), 1));

        Assert.Contains(Tableau(0), board.DirtyPiles);
        Assert.Contains(Foundation(0), board.DirtyPiles);
    }

    private static IEnumerable<List<Card>> AllPiles(Board board)
    {
        yield return board.FaceDownPile;
        yield return board.FaceUpPile;

        foreach (var pile in board.FoundationPiles)
        {
            yield return pile;
        }

        foreach (var pile in board.TableauPiles)
        {
            yield return pile;
        }
    }
}
