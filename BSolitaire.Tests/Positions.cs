using BSolitaire.Game;

namespace BSolitaire.Tests;

/// <summary>
/// Boards built by hand. A dealt board is random, which is the wrong thing to assert against,
/// so almost every test here starts from an empty one and puts down exactly the cards the
/// rule under test is about.
/// </summary>
public static class Positions
{
    /// <summary>A face-up card. Cards are dealt face down, so this is the one that has to be said.</summary>
    public static Card Up(Suit suit, Rank rank)
    {
        var card = new Card(suit, rank);
        card.Flip();
        return card;
    }

    public static Card Down(Suit suit, Rank rank) => new(suit, rank);

    /// <summary>A board with no cards anywhere. A position of three cards is far easier to
    /// reason about than fifty-two in an order nobody chose.</summary>
    public static Board Empty()
    {
        var board = new Board();
        board.Position.Strip();
        return board;
    }

    /// <summary>
    /// Every suit home to the queen, the four kings face up on the tableau. A complete deck in
    /// a position one move per king from finished — what a board looks like when it is about
    /// to be won.
    /// </summary>
    public static Board FourKingsFromDone()
    {
        var board = Empty();

        for (int i = 0; i < 4; i++)
        {
            var suit = (Suit)i;

            for (int rank = (int)Rank.Ace; rank <= (int)Rank.Queen; rank++)
            {
                board.Position.Place(Foundation(i), Up(suit, (Rank)rank));
            }

            board.Position.Place(Tableau(i), Up(suit, Rank.King));
        }

        return board;
    }

    public static Location Tableau(int index) => new(PileKind.Tableau, index);

    public static Location Foundation(int index) => new(PileKind.Foundation, index);

    public static Location Stock => new(PileKind.FaceDown, 0);

    public static Location Waste => new(PileKind.FaceUp, 0);
}
