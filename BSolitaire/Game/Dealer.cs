using System.Runtime.InteropServices;

namespace BSolitaire.Game;

internal sealed class Dealer
{
    /// <summary>
    /// Shuffles a deck into the stock and deals it out: pile one gets a card, pile two gets
    /// two, and so on to seven, taken from the top of the stock a card at a time so the last
    /// card onto each pile is the one showing.
    /// </summary>
    public void Deal(Position position)
    {
        var stock = new Location(PileKind.FaceDown, 0);
        position.Place(stock, BuildDefaultDeck());
        // Deal left-to-right, adding one more card to each tableau pile than the previous one.
        // Each new card is taken from the top of the stock and placed on the current pile,
        // so the last card dealt to a pile is the one on top.
        for (int row = 0; row < Position.TableauCount; row++)
        {
            for (int pileIndex = row; pileIndex < Position.TableauCount; pileIndex++)
            {
                var card = position.Take(stock, 1)[0];
                position.Place(new Location(PileKind.Tableau, pileIndex), card);

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

        // Fisher-Yates, by way of the runtime: every ordering of the deck comes up equally
        // often, and Random.Shared spares us both an allocation per deal and the loop.
        Random.Shared.Shuffle(CollectionsMarshal.AsSpan(deck));

        return deck;
    }
}