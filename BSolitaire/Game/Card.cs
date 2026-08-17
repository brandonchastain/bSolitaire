namespace BSolitaire.Game;

/// <summary>
/// A standard playing card.
/// </summary>
public class Card
{
    private bool isFaceUp = false;

    public Card(Suit suit, Rank rank)
    {
        Suit = suit;
        Rank = rank;
    }

    public Suit Suit { get; }
    public Rank Rank { get; }

    public bool IsRed => Suit == Suit.Diamonds || Suit == Suit.Hearts;
    public bool IsFaceUp => isFaceUp;

    public void Flip()
    {
        isFaceUp = !isFaceUp;
    }
}