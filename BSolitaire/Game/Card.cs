namespace BSolitaire.Game;

/// <summary>
/// A standard playing card.
/// </summary>
public readonly record struct Card
{
    public Suit Suit { get; }
    public Rank Rank { get; }

    public Card(Suit suit, Rank rank)
    {
        Suit = suit;
        Rank = rank;
    }

    public bool IsRed => Suit == Suit.Diamonds || Suit == Suit.Hearts;
}