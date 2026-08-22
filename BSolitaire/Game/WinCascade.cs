namespace BSolitaire.Game;

/// <summary>
/// One card bouncing its way off the bottom of the screen.
/// </summary>
public sealed class FallingCard
{
    public required Card Card { get; init; }

    public double X { get; set; }

    public double Y { get; set; }

    public double VelocityX { get; set; }

    public double VelocityY { get; set; }
}

/// <summary>
/// The reward. When the last card goes home the foundations throw themselves down the board
/// and bounce off the bottom, and the trail they leave is the picture of a finished game.
///
/// This is the one thing on the board that is pure celebration — it changes no position and
/// answers no question — so it lives on its own, and the drawer is what knows that the trail
/// is drawn by simply never rubbing the last frame out.
/// </summary>
public sealed class WinCascade
{
    /// <summary>Downward acceleration, in board pixels per frame per frame. Tuned against a
    /// card height rather than a screen, so it falls the same way at any size.</summary>
    private const double Gravity = 0.0016;

    /// <summary>How much of its speed a card keeps when it hits the bottom. Below about
    /// three quarters it stops looking like a card and starts looking like a beanbag.</summary>
    private const double Bounce = 0.78;

    /// <summary>Frames between one card being launched and the next.</summary>
    private const int FramesPerCard = 7;

    /// <summary>How many can be falling at once. The trail is free — it is already painted —
    /// but every card still in the air is drawn again every frame.</summary>
    private const int MaxInFlight = 7;

    private readonly Board board;
    private readonly BoardLayout layout;
    private readonly List<FallingCard> falling = new();
    private readonly Random random = new();

    /// <summary>How many have already been taken off each foundation. The piles are drained
    /// in turn, so each needs its own count — one running total would take the wrong card
    /// the moment a foundation ran out before its neighbours did.</summary>
    private readonly int[] taken = new int[4];

    private int nextFoundation;
    private int framesUntilNextCard;
    private int launched;

    public WinCascade(Board board, BoardLayout layout)
    {
        this.board = board;
        this.layout = layout;
    }

    /// <summary>Whether the cards are still coming down. The end-of-game panel waits for
    /// this — the player should get to watch before being asked what to do next.</summary>
    public bool IsRunning { get; private set; }

    public IReadOnlyList<FallingCard> Falling => falling;

    /// <summary>Sends the foundations down the board. Started once, when the game is won.</summary>
    public void Start()
    {
        falling.Clear();
        Array.Clear(taken);
        IsRunning = true;
        nextFoundation = 0;
        framesUntilNextCard = 0;
        launched = 0;
    }

    /// <summary>Cuts the celebration short, because the player pressed something. Whatever is
    /// already painted stays painted; nothing more is launched.</summary>
    public void Stop()
    {
        IsRunning = false;
        falling.Clear();
    }

    /// <summary>
    /// Moves every falling card on one frame and launches the next one when it is due.
    /// Returns true while there is anything to see.
    /// </summary>
    public bool Tick()
    {
        if (!IsRunning)
        {
            return false;
        }

        if (--framesUntilNextCard <= 0 && falling.Count < MaxInFlight)
        {
            framesUntilNextCard = FramesPerCard;
            Launch();
        }

        for (int i = falling.Count - 1; i >= 0; i--)
        {
            var card = falling[i];
            card.X += card.VelocityX * layout.CardWidth;
            card.Y += card.VelocityY * layout.CardHeight;
            card.VelocityY += Gravity * layout.Height / layout.CardHeight;

            if (card.Y + layout.CardHeight >= layout.Height && card.VelocityY > 0)
            {
                card.Y = layout.Height - layout.CardHeight;
                card.VelocityY = -card.VelocityY * Bounce;
            }

            // Gone off the side, which is where every one of them ends up.
            if (card.X + layout.CardWidth < 0 || card.X > layout.Width)
            {
                falling.RemoveAt(i);
            }
        }

        if (launched >= TotalToLaunch() && falling.Count == 0)
        {
            IsRunning = false;
        }

        return true;
    }

    /// <summary>
    /// Takes the top card off the next foundation and throws it. Foundations are drained in
    /// turn rather than one at a time, so all four come down together the way the original
    /// did — and the cards are only borrowed for the picture: the position is untouched.
    /// </summary>
    private void Launch()
    {
        for (int attempt = 0; attempt < board.FoundationPiles.Count; attempt++)
        {
            int index = nextFoundation;
            nextFoundation = (nextFoundation + 1) % board.FoundationPiles.Count;

            var pile = board.FoundationPiles[index];
            int depth = taken[index];

            if (depth >= pile.Count)
            {
                continue;
            }

            var slot = layout.EmptySlot(new Location(PileKind.Foundation, index));

            falling.Add(new FallingCard
            {
                Card = pile[pile.Count - 1 - depth],
                X = slot.X,
                Y = slot.Y,

                // Left or right, but never straight down: a card that falls on its own slot
                // just sits there flickering.
                VelocityX = (random.NextDouble() * 0.07 + 0.04) * (random.Next(2) == 0 ? -1 : 1),
                VelocityY = random.NextDouble() * 0.04,
            });

            taken[index]++;
            launched++;
            return;
        }

        // Nothing left on any foundation to throw.
        launched = TotalToLaunch();
    }

    private int TotalToLaunch() => board.FoundationTotal;
}
