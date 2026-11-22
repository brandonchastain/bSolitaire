namespace BSolitaire.Game;

internal sealed class Dealer
{
    /// <summary>
    /// A shuffled deck as an order rather than as cards: fifty-two suit-and-rank pairs that
    /// can be kept, judged, and dealt later.
    ///
    /// Cards are the wrong thing to hold on to. They are mutable, dealing turns seven of them
    /// over, and the fifty-two objects belong to the deal they were made for. An order is a
    /// plain value, so <see cref="DealVetter"/> can sit on one while a search decides whether
    /// the deal is worth giving to anybody.
    /// </summary>
    public static (Suit Suit, Rank Rank)[] Shuffle()
    {
        var deck = new (Suit Suit, Rank Rank)[52];
        int next = 0;

        foreach (Suit suit in Enum.GetValues<Suit>())
        {
            foreach (Rank rank in Enum.GetValues<Rank>())
            {
                deck[next++] = (suit, rank);
            }
        }

        // Fisher-Yates, by way of the runtime: every ordering of the deck comes up equally
        // often, and Random.Shared spares us both an allocation per deal and the loop.
        Random.Shared.Shuffle(deck);

        return deck;
    }

    /// <summary>
    /// Shuffles a deck into the stock and deals it out: pile one gets a card, pile two gets
    /// two, and so on to seven, taken from the top of the stock a card at a time so the last
    /// card onto each pile is the one showing.
    /// </summary>
    /// <param name="order">
    /// The deck to deal, or null to shuffle a fresh one. The same order always deals the same
    /// position — that is what lets a deal be decided on before it is played, and it is the
    /// only way a test can name the deal it is talking about.
    /// </param>
    public void Deal(Position position, IReadOnlyList<(Suit Suit, Rank Rank)>? order = null)
    {
        var stock = new Location(PileKind.FaceDown, 0);
        position.Place(stock, CardsFrom(order ?? Shuffle()));

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

    /// <summary>Fresh cards for an order. A deal owns its fifty-two cards outright, so two
    /// deals of the same order share nothing and cannot turn each other's cards over.</summary>
    private static List<Card> CardsFrom(IReadOnlyList<(Suit Suit, Rank Rank)> order)
    {
        var cards = new List<Card>(order.Count);

        foreach (var (suit, rank) in order)
        {
            cards.Add(new Card(suit, rank));
        }

        return cards;
    }
}
