using System.Text;
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

        Assert.Equal(24, board.Position.FaceDownPile.Count);
        Assert.Empty(board.Position.FaceUpPile);

        for (int i = 0; i < 7; i++)
        {
            Assert.Equal(i + 1, board.Position.TableauPiles[i].Count);
        }

        Assert.All(board.Position.FoundationPiles, Assert.Empty);
    }

    [Fact]
    public void ADealShowsTheTopCardOfEveryColumnAndNoOthers()
    {
        var board = new Board();

        foreach (var pile in board.Position.TableauPiles)
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
        Assert.Equal(23, board.Position.FaceDownPile.Count);
        Assert.Single(board.Position.FaceUpPile);
        Assert.True(board.Position.FaceUpPile[^1].IsFaceUp);
    }

    [Fact]
    public void AnEmptyStockRecyclesTheWasteFaceDown()
    {
        var board = new Board();

        while (board.DealFromStock())
        {
        }

        Assert.Empty(board.Position.FaceDownPile);
        Assert.Equal(24, board.Position.FaceUpPile.Count);

        Assert.True(board.RecycleWaste());
        Assert.Equal(24, board.Position.FaceDownPile.Count);
        Assert.Empty(board.Position.FaceUpPile);
        Assert.All(board.Position.FaceDownPile, card => Assert.False(card.IsFaceUp));
    }

    [Fact]
    public void AnIllegalMoveChangesNothingAndSaysSo()
    {
        var board = Empty();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Nine));
        board.Position.Place(Tableau(1), Up(Suit.Diamonds, Rank.Ten)); // same colour

        Assert.False(board.MakeMove(new Move(Tableau(0), Tableau(1), 1)));
        Assert.Single(board.Position.TableauPiles[0]);
        Assert.Single(board.Position.TableauPiles[1]);
        Assert.Contains(Sound.Invalid, board.Sounds);
    }

    [Fact]
    public void UncoveringATableauCardTurnsItOver()
    {
        var board = Empty();
        board.Position.Place(Tableau(0), Down(Suit.Clubs, Rank.Four));
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Nine));
        board.Position.Place(Tableau(1), Up(Suit.Spades, Rank.Ten));

        Assert.True(board.MakeMove(new Move(Tableau(0), Tableau(1), 1)));
        Assert.True(board.Position.TableauPiles[0][^1].IsFaceUp);
        Assert.Contains(Sound.Flip, board.Sounds);
    }

    [Fact]
    public void OnlyTheNewlyUncoveredCardTurnsOver()
    {
        // Two face-down cards under the run. Lifting it exposes the upper one and no more —
        // the one below it is still covered and stays hidden.
        var board = Empty();
        board.Position.Place(Tableau(0), Down(Suit.Clubs, Rank.Four));
        board.Position.Place(Tableau(0), Down(Suit.Clubs, Rank.Three));
        board.Position.Place(Tableau(0), Up(Suit.Spades, Rank.Ten));
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Nine));
        board.Position.Place(Tableau(1), Up(Suit.Diamonds, Rank.Jack));

        Assert.True(board.MakeMove(new Move(Tableau(0), Tableau(1), 2)));
        Assert.Equal(2, board.Position.TableauPiles[0].Count);
        Assert.True(board.Position.TableauPiles[0][1].IsFaceUp);
        Assert.False(board.Position.TableauPiles[0][0].IsFaceUp);
    }

    [Fact]
    public void AnAcePrefersTheFoundationMatchingItsSuit()
    {
        var board = Empty();

        var home = Rules.FoundationFor(board.Position, Up(Suit.Hearts, Rank.Ace));

        Assert.NotNull(home);
        Assert.Equal((int)Suit.Hearts, home!.Value.PileIndex);
    }

    [Fact]
    public void AnAceSettlesForAnyEmptyFoundation()
    {
        var board = Empty();
        board.Position.Place(Foundation((int)Suit.Hearts), Up(Suit.Spades, Rank.Ace));

        var home = Rules.FoundationFor(board.Position, Up(Suit.Hearts, Rank.Ace));

        Assert.NotNull(home);
        Assert.Empty(board.Position.FoundationPiles[home!.Value.PileIndex]);
    }

    [Fact]
    public void ACardWithNowhereToGoHasNoFoundation()
    {
        var board = Empty();

        Assert.Null(Rules.FoundationFor(board.Position, Up(Suit.Hearts, Rank.Five)));
    }

    [Fact]
    public void AMoveBumpsTheVersionAndAnIllegalOneDoesNot()
    {
        var board = Empty();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Ace));
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
        Assert.Equal(52, board.Position.FoundationTotal);
        Assert.Contains(Sound.Win, board.Sounds);
    }

    [Fact]
    public void APositionWithNothingLeftToDoIsStuck()
    {
        var board = Empty();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.King));
        board.Position.Place(Tableau(1), Up(Suit.Spades, Rank.Queen));
        board.Position.Place(Tableau(2), Up(Suit.Hearts, Rank.Two));

        // Any move at all, to make the board work out where it stands.
        board.MakeMove(new Move(Tableau(1), Tableau(0), 1));

        Assert.Equal(GameState.Stuck, board.State);
    }

    [Fact]
    public void TheBoardOffersToFinishOnceNothingIsFaceDown()
    {
        var board = FourKingsFromDone();
        board.Settle();

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
        Assert.Equal(52, board.Position.FoundationTotal);
    }

    [Fact]
    public void FastForwardTurnsTheStockOverToReachACard()
    {
        // Nothing on the tableau to play, and the card that can go home is under the stock.
        var board = Empty();
        board.Position.Place(Stock, Down(Suit.Hearts, Rank.Ace));
        board.Position.Place(Tableau(0), Up(Suit.Spades, Rank.King));

        Assert.True(board.FastForwardStep());
        Assert.Single(board.Position.FaceUpPile);

        Assert.True(board.FastForwardStep());
        Assert.Equal(1, board.Position.FoundationTotal);
    }

    [Fact]
    public void ResettingDealsAFreshGame()
    {
        var board = FourKingsFromDone();
        board.Reset();

        Assert.Equal(24, board.Position.FaceDownPile.Count);
        Assert.Equal(GameState.Playing, board.State);
        Assert.Equal(0, board.Position.FoundationTotal);
    }

    [Fact]
    public void AMoveMarksBothPilesForRedraw()
    {
        var board = Empty();
        board.Position.Place(Tableau(0), Up(Suit.Hearts, Rank.Ace));
        board.ClearDirty();

        board.MakeMove(new Move(Tableau(0), Foundation(0), 1));

        Assert.Contains(Tableau(0), board.DirtyPiles);
        Assert.Contains(Foundation(0), board.DirtyPiles);
    }


    [Fact]
    public void ADrawOfThreeTurnsThreeCardsOverTogether()
    {
        var board = Empty();
        board.Reset(null, 3);
        board.Position.Strip();
        board.Position.Place(Stock, Down(Suit.Hearts, Rank.Two));
        board.Position.Place(Stock, Down(Suit.Spades, Rank.Three));
        board.Position.Place(Stock, Down(Suit.Clubs, Rank.Four));

        Assert.True(board.DealFromStock());

        Assert.Empty(board.Position.FaceDownPile);
        Assert.Equal(3, board.Position.FaceUpPile.Count);

        // All three, not just the one on top: the other two are shown in the waste's fan.
        Assert.All(board.Position.FaceUpPile, card => Assert.True(card.IsFaceUp));

        // The packet is turned over on the way, so the card showing is the one that was
        // deepest in it — the two off the top of the stock end up behind it.
        Assert.Equal(Rank.Two, board.Position.FaceUpPile[^1].Rank);
    }

    [Fact]
    public void ADrawOfThreeTakesWhatIsLeftWhenTheStockIsShort()
    {
        var board = Empty();
        board.Reset(null, 3);
        board.Position.Strip();
        board.Position.Place(Stock, Down(Suit.Hearts, Rank.Two));
        board.Position.Place(Stock, Down(Suit.Spades, Rank.Three));

        Assert.True(board.DealFromStock());

        Assert.Empty(board.Position.FaceDownPile);
        Assert.Equal(2, board.Position.FaceUpPile.Count);
        Assert.All(board.Position.FaceUpPile, card => Assert.True(card.IsFaceUp));
    }

    [Fact]
    public void ADealStillTurnsOneCardWhenThatIsTheGameBeingPlayed()
    {
        var board = Empty();
        board.Reset(null, 1);
        board.Position.Strip();
        board.Position.Place(Stock, Down(Suit.Hearts, Rank.Two));
        board.Position.Place(Stock, Down(Suit.Spades, Rank.Three));

        Assert.True(board.DealFromStock());

        Assert.Single(board.Position.FaceUpPile);
        Assert.Equal(Rank.Three, board.Position.FaceUpPile[0].Rank);
    }

    [Fact]
    public void TurningTheWasteBackOverDealsTheSameGroupsAgain()
    {
        var board = Empty();
        board.Reset(null, 3);
        board.Position.Strip();

        foreach (var rank in new[] { Rank.Two, Rank.Three, Rank.Four, Rank.Five, Rank.Six, Rank.Seven })
        {
            board.Position.Place(Stock, Down(Suit.Hearts, rank));
        }

        var firstPass = new List<Rank>();

        while (board.DealFromStock())
        {
            firstPass.Add(board.Position.FaceUpPile[^1].Rank);
        }

        Assert.True(board.RecycleWaste());

        var secondPass = new List<Rank>();

        while (board.DealFromStock())
        {
            secondPass.Add(board.Position.FaceUpPile[^1].Rank);
        }

        Assert.Equal(firstPass, secondPass);
    }
    [Fact]
    public void AShuffleIsAWholeDeckWithNothingRepeated()
    {
        var order = Dealer.Shuffle();

        Assert.Equal(52, order.Length);
        Assert.Equal(52, new HashSet<(Suit Suit, Rank Rank)>(order).Count);
    }

    [Fact]
    public void TheSameDeckAlwaysDealsTheSamePosition()
    {
        // What lets a deal be decided on before it is played. The vetter proves a win in a
        // deck order and hands over the order rather than the position, so dealing that
        // order again has to produce exactly the position the win was proved in.
        var order = Dealer.Shuffle();

        var first = new Board();
        first.Reset(order);

        var second = new Board();
        second.Reset(order);

        Assert.Equal(Spell(first), Spell(second));
    }

    [Fact]
    public void TwoDealsOfOneDeckShareNoCards()
    {
        // Cards are mutable and a deal turns seven of them over. Were the two deals handed
        // the same fifty-two objects, turning a card up in one would turn it up in the other.
        var order = Dealer.Shuffle();

        var first = new Board();
        first.Reset(order);

        var second = new Board();
        second.Reset(order);

        Assert.NotSame(first.Position.TableauPiles[0][0], second.Position.TableauPiles[0][0]);
    }

    /// <summary>A whole position as one string, so two of them can be compared at a glance —
    /// every pile in order, every card's suit, rank, and which way up it is.</summary>
    private static string Spell(Board board)
    {
        var text = new StringBuilder();

        foreach (var pile in AllPiles(board))
        {
            foreach (var card in pile)
            {
                text.Append(card.Suit).Append(card.Rank).Append(card.IsFaceUp ? '^' : 'v').Append(' ');
            }

            text.Append('|');
        }

        return text.ToString();
    }

    private static IEnumerable<IReadOnlyList<Card>> AllPiles(Board board)
    {
        yield return board.Position.FaceDownPile;
        yield return board.Position.FaceUpPile;

        foreach (var pile in board.Position.FoundationPiles)
        {
            yield return pile;
        }

        foreach (var pile in board.Position.TableauPiles)
        {
            yield return pile;
        }
    }
}
