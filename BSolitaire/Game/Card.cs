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

    /// <summary>
    /// Puts the card a definite way up rather than the other way up from however it is now.
    /// Undo is what needs this: restoring a position means saying what was true then, not
    /// counting how many times the card has been turned since.
    /// </summary>
    public void SetFaceUp(bool value) => isFaceUp = value;
}