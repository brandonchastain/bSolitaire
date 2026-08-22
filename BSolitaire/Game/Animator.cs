namespace BSolitaire.Game;

/// <summary>
/// A card part-way between two places, ready to be drawn. Everything here is already in
/// board pixels, so the drawer paints it and asks nothing.
/// </summary>
public readonly record struct CardInFlight(Card Card, Rect Rect, bool FaceUp);

/// <summary>
/// Turns the board's <see cref="Motion"/>s into cards moving across the felt. This is the
/// one place that knows a move takes time: the position itself changes all at once, as it
/// should — every rule, every test, and the solver see a move land the instant it is made —
/// and what the player watches is this, running a step behind.
///
/// Because the board is already in its new state, an animated card would otherwise be drawn
/// twice: once at its destination, where it now belongs, and once in the air. So a flight
/// also holds its destination back — see <see cref="HiddenFrom"/> — until it lands.
/// </summary>
public sealed class Animator
{
    /// <summary>
    /// The longest a card may take to cross the board, however far it is going. Short: this
    /// is feedback, not a cutscene, and a player making moves quickly must never be waiting
    /// on the last one.
    /// </summary>
    private const double MoveMs = 160;

    /// <summary>And the shortest. A card nudged into the slot it is already sitting over
    /// still wants a moment of travel — instant reads as a glitch — but only a moment.</summary>
    private const double MinMoveMs = 70;

    /// <summary>
    /// How fast a card travels, in card widths per millisecond. Expressed against the card
    /// rather than in pixels so a phone and a desktop feel the same: the board is about seven
    /// cards wide on both, so a move of the same *distance in cards* takes the same time.
    /// A move's duration comes from this and is only then clamped, so a shuffle across two
    /// columns is quick and a card going home from the far side is not instant.
    /// </summary>
    private const double CardWidthsPerMs = 0.05;

    /// <summary>A card turning over where it lies. Slightly quicker than a move — it covers
    /// no distance, so the same duration reads as a hesitation.</summary>
    private const double FlipMs = 130;

    /// <summary>How far apart the twenty-eight cards of a deal leave the stock. Twenty-eight
    /// of these is about three quarters of a second, which is a deal you can watch without
    /// being made to wait for it.</summary>
    private const double DealStaggerMs = 26;

    private const double DealMs = 240;

    /// <summary>
    /// A deal arrives as one batch of twenty-eight motions, and nothing else the board does
    /// comes close. Above this the batch is taken to be a deal and staggered.
    /// </summary>
    private const int DealThreshold = 8;

    private readonly Board board;
    private readonly BoardLayout layout;

    // Where a stack was let go, if the move about to arrive is a drop rather than a tap.
    // Without this a drop is animated from the pile the cards came from: the player drags a
    // card across the board, releases it, and watches it jump back to where it started and
    // fly to where they had already put it. The cards are on screen under the pointer at the
    // moment of release, and that is where the move has to start from.
    private Location? releaseFrom;
    private int releaseIndex;
    private Rect releaseRect;

    private readonly List<Flight> flights = new();
    private readonly List<CardInFlight> inFlight = new();
    private readonly Dictionary<Location, int> hidden = new();

    public Animator(Board board, BoardLayout layout)
    {
        this.board = board;
        this.layout = layout;
    }

    /// <summary>Whether anything is moving. While it is, the picture is out of date every
    /// frame and the search stands aside.</summary>
    public bool Busy => flights.Count > 0;

    /// <summary>The cards to paint over the board this frame, in the order they were sent.</summary>
    public IReadOnlyList<CardInFlight> InFlight => inFlight;

    /// <summary>
    /// The index from which a pile must not be drawn, because the cards at and above it are
    /// still in the air. <see cref="int.MaxValue"/> when the whole pile is settled.
    /// </summary>
    public int HiddenFrom(Location loc) => hidden.TryGetValue(loc, out int index) ? index : int.MaxValue;

    /// <summary>
    /// Takes whatever the board has done since the last frame and puts it in the air. Called
    /// before <see cref="Tick"/>, so a move made this frame is already moving in it.
    /// </summary>
    /// <summary>
    /// Says where the stack being carried actually is, for the move that a release is about
    /// to make. Good for that one move only — an illegal drop makes no move at all, and the
    /// next real one must not start from a pointer that has long since moved on.
    /// </summary>
    public void ReleaseAt(Location from, int index, Rect at)
    {
        releaseFrom = from;
        releaseIndex = index;
        releaseRect = at;
    }

    public void Capture(double nowMs)
    {
        var motions = board.Motions;

        if (motions.Count == 0)
        {
            // Nothing came of it — an illegal drop, or a press that moved nothing. The
            // release is stale either way.
            releaseFrom = null;
            return;
        }

        // A deal is dozens of cards leaving the same slot, and dropping them on the table
        // simultaneously is the one case where all-at-once looks worse than a queue.
        bool dealing = motions.Count >= DealThreshold;

        for (int i = 0; i < motions.Count; i++)
        {
            var motion = motions[i];
            double start = nowMs + (dealing ? i * DealStaggerMs : 0);
            var from = Origin(motion);
            var to = layout.CardRect(motion.To, motion.ToIndex);

            // Distance decides how long, within limits. A fixed duration makes a nudge into
            // the next column look laboured and a card going home from the far side look
            // hurried, and the nudge is the one a player makes over and over.
            double travel = Math.Abs(to.X - from.X) + Math.Abs(to.Y - from.Y);
            double duration = motion.Kind == MotionKind.Flip
                ? FlipMs
                : dealing
                    ? DealMs
                    : Math.Clamp(travel / (layout.CardWidth * CardWidthsPerMs), MinMoveMs, MoveMs);

            flights.Add(new Flight
            {
                Kind = motion.Kind,
                Card = motion.Card,
                From = from,
                To = to,
                Dest = motion.To,
                DestIndex = motion.ToIndex,
                StartMs = start,
                EndMs = start + duration,
                Reveals = motion.Reveals,
            });
        }

        board.ClearMotions();
        releaseFrom = null;
        Refresh(nowMs);
    }

    /// <summary>
    /// Moves everything on a frame and retires whatever has arrived. Returns true when the
    /// picture changed, which is whenever anything is in the air at all.
    /// </summary>
    public bool Tick(double nowMs)
    {
        if (flights.Count == 0)
        {
            return false;
        }

        for (int i = flights.Count - 1; i >= 0; i--)
        {
            if (nowMs < flights[i].EndMs)
            {
                continue;
            }

            // The card belongs to its pile again, and the pile has been drawn without it
            // ever since the flight began — so it needs painting once more.
            board.Touch(flights[i].Dest);
            flights.RemoveAt(i);
        }

        Refresh(nowMs);
        return true;
    }

    /// <summary>Drops everything mid-air. The board is already right; this is only the
    /// picture of it, so there is nothing to finish.</summary>
    public void Clear()
    {
        foreach (var flight in flights)
        {
            board.Touch(flight.Dest);
        }

        flights.Clear();
        inFlight.Clear();
        hidden.Clear();
        board.ClearMotions();
    }

    /// <summary>
    /// Ease-out: quick away, gentle on arrival. A card that decelerates into its slot looks
    /// placed; one moving at a constant speed looks dragged by a machine.
    /// </summary>
    private static double Ease(double t)
    {
        t = Math.Clamp(t, 0, 1);
        double inverse = 1 - t;
        return 1 - inverse * inverse * inverse;
    }
    /// <summary>
    /// Where a card starts from. Its slot in the pile it left, unless the player was holding
    /// it — in which case it starts under their hand, which is where they can see it is.
    /// </summary>
    private Rect Origin(Motion motion)
    {
        if (motion.Kind != MotionKind.Move ||
            releaseFrom != motion.From ||
            motion.FromIndex < releaseIndex)
        {
            return layout.CardRect(motion.From, motion.FromIndex);
        }

        // A held run is fanned under the pointer exactly as it was in its pile, so each card
        // of it starts that much further down than the one the player actually grabbed.
        return new Rect(
            releaseRect.X,
            releaseRect.Y + (motion.FromIndex - releaseIndex) * layout.FanOffset,
            layout.CardWidth,
            layout.CardHeight);
    }

    /// <summary>
    /// Works out, for this instant, where every flying card is and which piles are holding
    /// cards back. Rebuilt from the flights rather than maintained alongside them, so the
    /// two cannot disagree.
    /// </summary>
    private void Refresh(double nowMs)
    {
        inFlight.Clear();
        hidden.Clear();

        foreach (var flight in flights)
        {
            // A staggered deal leaves most of its cards waiting their turn. They are held
            // out of their pile from the moment the deal is announced — otherwise a card
            // would sit at its destination and then take off from it.
            hidden[flight.Dest] = hidden.TryGetValue(flight.Dest, out int lowest)
                ? Math.Min(lowest, flight.DestIndex)
                : flight.DestIndex;

            if (nowMs < flight.StartMs)
            {
                continue;
            }

            double raw = Math.Clamp((nowMs - flight.StartMs) / (flight.EndMs - flight.StartMs), 0, 1);

            if (flight.Kind == MotionKind.Flip)
            {
                // Linear, unlike a move: a turn is symmetric, and easing it puts the narrow
                // half of the card in the first fifth of the time and the flat half in the
                // rest — which reads as a card twitching rather than turning.
                inFlight.Add(Turning(flight, raw));
                continue;
            }

            double t = Ease(raw);

            inFlight.Add(new CardInFlight(
                flight.Card,
                new Rect(
                    flight.From.X + (flight.To.X - flight.From.X) * t,
                    flight.From.Y + (flight.To.Y - flight.From.Y) * t,
                    layout.CardWidth,
                    layout.CardHeight),

                // A card dealt off the stock turns over half way, which is where a hand
                // would turn it — at the end it reads as a card that arrived wrong.
                FaceUp: flight.Reveals ? t >= 0.5 : flight.Card.IsFaceUp));
        }
    }

    /// <summary>
    /// A card turning over, drawn as the card narrowing to nothing and opening out again
    /// about its own middle. No transform and no second canvas: a card is drawn inside a
    /// rectangle, so squeezing the rectangle squeezes everything printed on it.
    /// </summary>
    private CardInFlight Turning(Flight flight, double t)
    {
        double half = Math.Abs(t - 0.5) * 2;

        // Never quite to zero: a rectangle of no width is a card that vanishes for a frame.
        double width = Math.Max(layout.CardWidth * 0.06, layout.CardWidth * half);

        return new CardInFlight(
            flight.Card,
            new Rect(
                flight.To.X + (layout.CardWidth - width) / 2,
                flight.To.Y,
                width,
                layout.CardHeight),
            FaceUp: t >= 0.5);
    }


    private sealed class Flight
    {
        public required MotionKind Kind { get; init; }

        public required Card Card { get; init; }

        public required Rect From { get; init; }

        public required Rect To { get; init; }

        public required Location Dest { get; init; }

        public required int DestIndex { get; init; }

        public required double StartMs { get; init; }

        public required double EndMs { get; init; }

        public required bool Reveals { get; init; }
    }
}
