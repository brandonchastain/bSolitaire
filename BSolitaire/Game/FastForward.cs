namespace BSolitaire.Game;

/// <summary>What one frame of a fast-forward did.</summary>
public enum FastForwardTick
{
    /// <summary>Nothing moved — this frame was part of the gap between cards.</summary>
    Idle,

    /// <summary>A card went home.</summary>
    Advanced,

    /// <summary>The run is over, because the game is or the board stopped making progress.</summary>
    Finished
}

/// <summary>
/// Plays a decided game out, one card every few frames. This is pacing rather than play — the
/// move itself is <see cref="Board.FastForwardStep"/> — so it lives apart from the game: a
/// frame counter and a stall guard are the sort of thing that quietly accretes on whatever
/// class is nearest.
/// </summary>
public sealed class FastForward
{
    /// <summary>
    /// Frames between cards during a fast-forward. Instant would be a worse answer than fast:
    /// the player asked to skip the clicking, not to skip seeing the game finish.
    /// </summary>
    private const int FramesPerCard = 3;

    /// <summary>
    /// A stop for a fast-forward that is turning the stock over without ever finding anything
    /// to play. It cannot happen from a position that offers the button — but the button is
    /// not the only thing that could ever call it, and a loop that never ends is a worse bug
    /// than one card left unplayed.
    /// </summary>
    private const int StallLimit = 60;

    private readonly Board board;

    private int framesUntilNextCard;
    private int stepsWithoutProgress;

    public FastForward(Board board) => this.board = board;

    /// <summary>Whether the rest of the game is currently playing itself out.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Starts playing the rest of the game out.</summary>
    public void Start()
    {
        IsRunning = true;
        framesUntilNextCard = 1;
        stepsWithoutProgress = 0;
    }

    public void Stop() => IsRunning = false;

    /// <summary>
    /// Plays one more card home, if it is time to. Only called while <see cref="IsRunning"/>.
    /// </summary>
    public FastForwardTick Tick()
    {
        if (board.State != GameState.Playing || stepsWithoutProgress > StallLimit)
        {
            IsRunning = false;
            return FastForwardTick.Finished;
        }

        if (--framesUntilNextCard > 0)
        {
            return FastForwardTick.Idle;
        }

        framesUntilNextCard = FramesPerCard;

        int before = board.FoundationTotal;
        if (!board.FastForwardStep())
        {
            IsRunning = false;
            return FastForwardTick.Finished;
        }

        // Turning the stock over is a step but not progress. Only cards reaching a
        // foundation reset the stall count.
        stepsWithoutProgress = board.FoundationTotal > before ? 0 : stepsWithoutProgress + 1;
        return FastForwardTick.Advanced;
    }
}
