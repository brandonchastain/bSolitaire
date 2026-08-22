using BSolitaire.Game;
using Xunit;
using static BSolitaire.Tests.Positions;

namespace BSolitaire.Tests;

/// <summary>
/// The moving picture. The position itself never animates — a move lands instantly, which is
/// what keeps the rules and the solver simple — so what is being pinned down here is that the
/// picture running a step behind it stays honest: a card in the air is drawn once, not twice,
/// and it is back in its pile the moment it arrives.
/// </summary>
public class AnimationTests
{
    private const double Width = 1200;
    private const double Height = 800;

    private static (Board Board, BoardLayout Layout, Animator Animator) Table()
    {
        var board = Empty();
        board.ClearMotions();
        var layout = new BoardLayout(Width, Height);
        return (board, layout, new Animator(board, layout));
    }

    [Fact]
    public void AMoveIsReportedAsACardTravelling()
    {
        var (board, _, animator) = Table();
        board.Mutable(Tableau(0)).Add(Up(Suit.Hearts, Rank.Nine));
        board.Mutable(Tableau(1)).Add(Up(Suit.Spades, Rank.Ten));

        board.MakeMove(new Move(Tableau(0), Tableau(1), 1));
        animator.Capture(0);

        Assert.True(animator.Busy);
        var flying = Assert.Single(animator.InFlight);
        Assert.Equal(Rank.Nine, flying.Card.Rank);
    }

    [Fact]
    public void ACardInTheAirIsHeldOutOfThePileItIsFlyingTo()
    {
        // Otherwise it is drawn twice: once where it now belongs, and once in mid-air.
        var (board, _, animator) = Table();
        board.Mutable(Tableau(0)).Add(Up(Suit.Hearts, Rank.Nine));
        board.Mutable(Tableau(1)).Add(Up(Suit.Spades, Rank.Ten));

        board.MakeMove(new Move(Tableau(0), Tableau(1), 1));
        animator.Capture(0);

        // The nine is the second card of column 1 now, and column 1 must be drawn without it.
        Assert.Equal(1, animator.HiddenFrom(Tableau(1)));
        Assert.Equal(int.MaxValue, animator.HiddenFrom(Tableau(0)));
    }

    [Fact]
    public void ACardThatHasArrivedIsGivenBackToItsPile()
    {
        var (board, _, animator) = Table();
        board.Mutable(Tableau(0)).Add(Up(Suit.Hearts, Rank.Nine));
        board.Mutable(Tableau(1)).Add(Up(Suit.Spades, Rank.Ten));

        board.MakeMove(new Move(Tableau(0), Tableau(1), 1));
        animator.Capture(0);
        board.ClearDirty();

        animator.Tick(1000);

        Assert.False(animator.Busy);
        Assert.Empty(animator.InFlight);
        Assert.Equal(int.MaxValue, animator.HiddenFrom(Tableau(1)));

        // ...and the pile has been asked for once more, because it was drawn short of a card
        // for the whole of the flight.
        Assert.Contains(Tableau(1), board.DirtyPiles);
    }

    [Fact]
    public void ACardTravelsFromWhereItWasToWhereItIsGoing()
    {
        var (board, layout, animator) = Table();
        board.Mutable(Tableau(0)).Add(Up(Suit.Hearts, Rank.Nine));
        board.Mutable(Tableau(1)).Add(Up(Suit.Spades, Rank.Ten));

        var start = layout.CardRect(Tableau(0), 0);
        var end = layout.CardRect(Tableau(1), 1);

        board.MakeMove(new Move(Tableau(0), Tableau(1), 1));
        animator.Capture(0);
        Assert.Equal(start.X, animator.InFlight[0].Rect.X, 1);

        // Part way: somewhere strictly between the two columns.
        animator.Tick(60);
        double midX = animator.InFlight[0].Rect.X;
        Assert.True(midX > Math.Min(start.X, end.X) && midX < Math.Max(start.X, end.X),
            $"a card half way across is at {midX}, outside {start.X}..{end.X}");
    }

    [Fact]
    public void ACardOffTheStockTurnsOverOnTheWay()
    {
        var (board, _, animator) = Table();
        board.Mutable(Stock).Add(Down(Suit.Hearts, Rank.Nine));

        board.DealFromStock();
        animator.Capture(0);

        // The board has already turned it over. The picture has not, until half way.
        Assert.True(board.FaceUpPile[0].IsFaceUp);
        Assert.False(animator.InFlight[0].FaceUp);

        // Stock to waste is one column, so it is one of the quick ones — past half way well
        // before the fifty milliseconds a card crossing the whole board would still be flying.
        animator.Tick(50);
        Assert.True(animator.InFlight[0].FaceUp);
    }

    [Fact]
    public void AnUncoveredCardTurnsOverWhereItLies()
    {
        var (board, layout, animator) = Table();
        board.Mutable(Tableau(0)).Add(Down(Suit.Clubs, Rank.Four));
        board.Mutable(Tableau(0)).Add(Up(Suit.Hearts, Rank.Nine));
        board.Mutable(Tableau(1)).Add(Up(Suit.Spades, Rank.Ten));

        board.MakeMove(new Move(Tableau(0), Tableau(1), 1));
        animator.Capture(0);

        // Two things are moving: the nine crossing to column 1, and the four turning over.
        Assert.Equal(2, animator.InFlight.Count);
        Assert.Equal(Rank.Four, animator.InFlight[1].Card.Rank);

        // It starts as a card, is barely there half way through, and is a card again at the
        // end — the turn is drawn as the rectangle it is printed in narrowing to nothing.
        Assert.Equal(layout.CardWidth, animator.InFlight[1].Rect.W, 1);

        animator.Tick(65);
        var turning = animator.InFlight[^1];
        Assert.Equal(Rank.Four, turning.Card.Rank);
        Assert.Equal(layout.CardHeight, turning.Rect.H, 1);
        Assert.True(turning.Rect.W < layout.CardWidth * 0.2,
            $"a card half way through turning is {turning.Rect.W:F1} wide");

        // ...and it shows its new face only once it is past the halfway point.
        Assert.True(turning.FaceUp);
    }

    [Fact]
    public void ADealIsStaggeredRatherThanDroppedAllAtOnce()
    {
        var board = new Board();
        var layout = new BoardLayout(Width, Height);
        var animator = new Animator(board, layout);

        // A fresh board announces the whole deal — twenty-eight cards leaving the stock.
        Assert.Equal(28, board.Motions.Count);

        animator.Capture(0);
        Assert.True(animator.Busy);

        // The first is away; the last is still waiting its turn.
        Assert.True(animator.InFlight.Count < 28, "the whole deal arrived at once");
        Assert.True(animator.InFlight.Count >= 1, "nothing left the stock");

        // Every column is empty until its cards land, so nothing is drawn twice on the way.
        for (int i = 0; i < 7; i++)
        {
            Assert.Equal(0, animator.HiddenFrom(Tableau(i)));
        }

        animator.Tick(5000);
        Assert.False(animator.Busy);
    }

    [Fact]
    public void ADroppedStackFliesFromWhereItWasLetGoOfRatherThanFromItsPile()
    {
        // The board reports a move as "these cards left that pile", which is all it knows.
        // Animating that literally means a stack the player has just dragged across the
        // board snaps back to where it started and flies out again.
        var (board, layout, animator) = Table();
        board.Mutable(Tableau(0)).Add(Up(Suit.Hearts, Rank.Nine));
        board.Mutable(Tableau(1)).Add(Up(Suit.Spades, Rank.Ten));

        // Let go of it a long way from either column.
        var releasedAt = new Rect(600, 700, layout.CardWidth, layout.CardHeight);
        animator.ReleaseAt(Tableau(0), 0, releasedAt);

        board.MakeMove(new Move(Tableau(0), Tableau(1), 1));
        animator.Capture(0);

        var start = animator.InFlight[0].Rect;
        Assert.Equal(releasedAt.X, start.X, 1);
        Assert.Equal(releasedAt.Y, start.Y, 1);
    }

    [Fact]
    public void AReleaseThatMovedNothingIsForgotten()
    {
        // An illegal drop makes no move at all. The next real move must not be animated from
        // a pointer that has long since moved on.
        var (board, layout, animator) = Table();
        board.Mutable(Tableau(0)).Add(Up(Suit.Hearts, Rank.Nine));
        board.Mutable(Tableau(1)).Add(Up(Suit.Spades, Rank.Ten));

        animator.ReleaseAt(Tableau(0), 0, new Rect(600, 700, layout.CardWidth, layout.CardHeight));
        animator.Capture(0); // nothing was moved, so nothing is captured

        board.MakeMove(new Move(Tableau(0), Tableau(1), 1));
        animator.Capture(0);

        var expected = layout.CardRect(Tableau(0), 0);
        Assert.Equal(expected.X, animator.InFlight[0].Rect.X, 1);
    }

    [Fact]
    public void AShortMoveTakesLessTimeThanALongOne()
    {
        // A fixed duration makes the nudge into the next column — the move a player makes
        // over and over — look laboured.
        double near = TimeToMove(Tableau(1));
        double far = TimeToMove(Tableau(6));

        Assert.True(far > near, $"a move across the board ({far}ms) is no slower than one next door ({near}ms)");
    }

    /// <summary>How long a nine takes to reach a ten in the given column, from column 0.</summary>
    private static double TimeToMove(Location destination)
    {
        var (board, _, animator) = Table();
        board.Mutable(Tableau(0)).Add(Up(Suit.Hearts, Rank.Nine));
        board.Mutable(destination).Add(Up(Suit.Spades, Rank.Ten));

        Assert.True(board.MakeMove(new Move(Tableau(0), destination, 1)));
        animator.Capture(0);

        return Settled(animator);
    }

    /// <summary>How long until nothing is moving, to the nearest millisecond.</summary>
    private static double Settled(Animator animator)
    {
        for (double t = 1; t < 2000; t++)
        {
            animator.Tick(t);

            if (!animator.Busy)
            {
                return t;
            }
        }

        return 2000;
    }

    [Fact]
    public void ClearingLandsEverythingImmediately()
    {
        var (board, _, animator) = Table();
        board.Mutable(Tableau(0)).Add(Up(Suit.Hearts, Rank.Nine));
        board.Mutable(Tableau(1)).Add(Up(Suit.Spades, Rank.Ten));
        board.MakeMove(new Move(Tableau(0), Tableau(1), 1));
        animator.Capture(0);

        animator.Clear();

        Assert.False(animator.Busy);
        Assert.Empty(animator.InFlight);
        Assert.Equal(int.MaxValue, animator.HiddenFrom(Tableau(1)));
    }

    [Fact]
    public void AWonGameThrowsItsFoundationsDownTheBoard()
    {
        var board = FourKingsFromDone();
        var layout = new BoardLayout(Width, Height);
        var cascade = new WinCascade(board, layout);

        for (int i = 0; i < 4; i++)
        {
            board.MakeMove(new Move(Tableau(i), Foundation(i), 1));
        }

        cascade.Start();
        Assert.True(cascade.IsRunning);

        cascade.Tick();
        Assert.NotEmpty(cascade.Falling);

        // They fall, and the position they came from is untouched — this is a picture, not
        // a move.
        double before = cascade.Falling[0].Y;
        for (int i = 0; i < 20; i++)
        {
            cascade.Tick();
        }

        Assert.NotEqual(before, cascade.Falling.Count > 0 ? cascade.Falling[0].Y : before + 1);
        Assert.Equal(52, board.FoundationTotal);
    }

    [Fact]
    public void TheCascadeEmptiesTheBoardAndStops()
    {
        var board = FourKingsFromDone();
        var layout = new BoardLayout(Width, Height);
        var cascade = new WinCascade(board, layout);

        for (int i = 0; i < 4; i++)
        {
            board.MakeMove(new Move(Tableau(i), Foundation(i), 1));
        }

        cascade.Start();

        // Long enough for fifty-two cards to be launched and to leave the screen.
        for (int i = 0; i < 20000 && cascade.IsRunning; i++)
        {
            cascade.Tick();
        }

        Assert.False(cascade.IsRunning);
        Assert.Empty(cascade.Falling);
    }

    [Fact]
    public void ThePanelWaitsForTheCardsToFinishFalling()
    {
        var game = new Solitaire();
        game.Resize(Width, Height);

        var board = game.Board;
        ClearAll(board);
        board.ClearMotions();

        for (int i = 0; i < 4; i++)
        {
            var suit = (Suit)i;
            for (int rank = (int)Rank.Ace; rank <= (int)Rank.Queen; rank++)
            {
                board.Mutable(Foundation(i)).Add(Up(suit, (Rank)rank));
            }

            board.Mutable(Tableau(i)).Add(Up(suit, Rank.King));
        }

        for (int i = 0; i < 4; i++)
        {
            board.MakeMove(new Move(Tableau(i), Foundation(i), 1));
        }

        // Frames enough for the four kings to land, and then some.
        for (int frame = 0; frame < 40; frame++)
        {
            game.Update(TimeSpan.FromMilliseconds(frame * 16));
        }

        Assert.Equal(GameState.Won, game.State);
        Assert.NotEmpty(game.Falling);
        Assert.False(game.ShowBanner);

        // A press cuts it short, and then the board asks what the player wants next.
        game.OnPointerDown(Width / 2, Height / 2);
        Assert.True(game.ShowBanner);
    }
}
