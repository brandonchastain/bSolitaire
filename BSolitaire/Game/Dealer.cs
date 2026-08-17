namespace BSolitaire.Game;

public class Dealer
{
    /// <summary>
    /// Deal cards from facedown into tableau piles. The first pile gets 1 card, the second gets 2, and so on, up to the seventh pile which gets 7 cards. The top card of each tableau pile is turned face up.
    /// Dealing must happen in order of tableau piles, one card at a time, from the top of the facedown pile.
    /// </summary>
    public void Deal(List<Card> faceDownPile, List<Card>[] tableauPiles)
    {
        faceDownPile.AddRange(BuildDefaultDeck());
        // Deal left-to-right, adding one more card to each tableau pile than the previous one.
        // Each new card is taken from the top of the stock and placed on the current pile,
        // so the last card dealt to a pile is the one on top.
        for (int row = 0; row < tableauPiles.Length; row++)
        {
            for (int pileIndex = row; pileIndex < tableauPiles.Length; pileIndex++)
            {
                var card = faceDownPile[^1];
                faceDownPile.RemoveAt(faceDownPile.Count - 1);
                tableauPiles[pileIndex].Add(card);

                if (pileIndex == row)
                {
                    card.Flip();
                }
            }
        }
    }

    private static List<Card> BuildDefaultDeck()
    {
        var deck = new List<Card>();
        foreach (Suit suit in Enum.GetValues<Suit>())
        {
            foreach (Rank rank in Enum.GetValues<Rank>())
            {
                deck.Add(new Card(suit, rank));
            }
        }

        // shuffle the deck
        var rng = new Random();
        int n = deck.Count;
        while (n > 1)
        {
            n--;
            int k = rng.Next(n + 1);
            (deck[k], deck[n]) = (deck[n], deck[k]);
        }

        return deck;
    }
}