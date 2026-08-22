namespace BSolitaire.Game;

/// <summary>
/// The half of <see cref="Board"/> that exists for the tests. A dealt board is random, which
/// is the wrong thing to assert against, so almost every test empties one and puts down
/// exactly the cards its rule is about.
///
/// This is deliberately not a general "build me a position" API. Nothing in the app ever
/// starts from anything but a fresh deal, and inventing an interface for arbitrary positions
/// would be surface area bought for a caller that does not exist. It lives in its own file so
/// that reading Board.cs is reading the game, not the scaffolding around it.
///
/// Being here changes no accessibility: these are still internal, and still callable from
/// anywhere in the assembly. The file boundary is a signal about intent, not a wall.
/// </summary>
public partial class Board
{
    /// <summary>
    /// Puts cards on a pile, on top of whatever is already there. This is how a position is
    /// arranged rather than played, and it deliberately does none of what <see cref="MakeMove"/>
    /// does: there is no undo step to record, no motion to animate, and no sound to play,
    /// because nothing moved. The pile it touched is marked for repaint, which is the one thing
    /// a card appearing does share with a card arriving.
    /// </summary>
    internal void Place(Location loc, params Card[] cards) => Place(loc, (IEnumerable<Card>)cards);

    /// <inheritdoc cref="Place(Location, Card[])"/>
    internal void Place(Location loc, IEnumerable<Card> cards)
    {
        Mutable(loc).AddRange(cards);
        MarkDirty(loc);
    }

    /// <summary>Takes every card off the board, leaving thirteen empty piles. The counterpart
    /// to <see cref="Place"/>: a test says what it wants on the board by clearing what the
    /// deal put there and putting down only the cards its rule is about. This is the one
    /// arrangement broad enough to be worth <see cref="AllDirty"/> rather than naming piles.
    /// </summary>
    internal void Strip()
    {
        foreach (var kind in AllKinds)
        {
            for (int i = 0; i < PileCountOf(kind); i++)
            {
                Mutable(new Location(kind, i)).Clear();
            }
        }

        AllDirty = true;
    }

    /// <summary>Takes every card off one pile.</summary>
    internal void Strip(Location loc)
    {
        Mutable(loc).Clear();
        MarkDirty(loc);
    }

    /// <summary>
    /// Judges the position that has just been arranged: recomputes won and stuck, the offer to
    /// play out, and the version the solver watches. <see cref="Place"/> deliberately does not
    /// do this — a board is nonsense half way through being built, and asking whether it is
    /// stuck before the last card is down would answer a question nobody asked. So the test
    /// says when it has finished, and only if it means to read any of those three.
    /// </summary>
    internal void Settle() => RefreshState();
}
