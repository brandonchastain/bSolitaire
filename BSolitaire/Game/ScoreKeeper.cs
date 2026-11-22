using System.Text.Json;

namespace BSolitaire.Game;

/// <summary>
/// Watches the board finish and keeps the player's running record. Counting deals is not one
/// of solitaire's rules — the board plays exactly the same whether or not anyone is keeping
/// score — so it sits outside the game and listens instead.
/// </summary>
internal sealed class ScoreKeeper
{
    /// <summary>
    /// The longest gap between frames that counts as time the player spent playing.
    ///
    /// The frame loop stops while the tab is hidden, but the clock it reports does not: come
    /// back after a minute away and the next frame arrives a minute after the last one. A gap
    /// this long is the tab having been hidden, or the loop having stalled, and either way it
    /// is not time anybody spent on this deal. Four frames at sixty hertz is far longer than
    /// a frame ever legitimately takes and far shorter than a glance at another tab.
    /// </summary>
    private const double LongestFrame = 250;

    private readonly Board board;

    private int recordedDeal = -1;

    private int timedDeal = -1;
    private int dealtAtVersion = -1;
    private double? lastFrameMs;
    private double elapsedMs;

    public ScoreKeeper(Board board) => this.board = board;

    /// <summary>Raised when <see cref="Score"/> has changed and is worth persisting.</summary>
    public event Action? Changed;

    /// <summary>The local player's running record. The host loads it before the first frame
    /// and saves it whenever <see cref="Changed"/> fires — this only counts.</summary>
    public PlayerScore Score { get; } = new();

    /// <summary>How long the deal that just ended took, or null before one has. What the
    /// banner says; the averages come off the record itself.</summary>
    public long? LastGameSeconds { get; private set; }

    /// <summary>
    /// How long the deal in progress has taken so far. Zero until its first move, and it
    /// stops where <see cref="Tick"/> stops — a finished board, or a tab nobody is looking
    /// at. Counted down rather than rounded, because a clock that reads one before a second
    /// has passed is telling the player something that is not true.
    /// </summary>
    public long ElapsedSeconds => (long)(elapsedMs / 1000);

    /// <summary>Whether this deal has been played at all. A deal nobody has moved a card on
    /// is not a game they lost, so walking away from it records nothing.</summary>
    private bool Started => dealtAtVersion >= 0 && board.Version != dealtAtVersion;

    /// <summary>
    /// Keeps the clock on the deal in progress, given the frame's timestamp.
    ///
    /// A timestamp, not a gap: the frame loop hands out the clock it was given and the only
    /// thing that knows how long a frame took is whatever kept the last one. So this keeps
    /// it, and takes the difference itself — the same arrangement the animator has.
    ///
    /// Time runs from the first move rather than from the deal, because a deal sitting
    /// unplayed while somebody reads the board is not time spent on it, and stops while the
    /// board is finished or the tab is away.
    /// </summary>
    public void Tick(double nowMs)
    {
        double sinceLastFrame = lastFrameMs is { } last ? nowMs - last : 0;
        lastFrameMs = nowMs;

        if (timedDeal != board.DealId)
        {
            // A fresh deal. The version to compare against is the one it was dealt at, so
            // the first move is whatever first moves the board off it.
            timedDeal = board.DealId;
            dealtAtVersion = board.Version;
            elapsedMs = 0;
            return;
        }

        if (!Started || board.State != GameState.Playing)
        {
            return;
        }

        if (sinceLastFrame > 0 && sinceLastFrame <= LongestFrame)
        {
            elapsedMs += sinceLastFrame;
        }
    }

    /// <summary>
    /// Counts a deal the player walked away from, reporting whether anything was counted.
    /// Called before a new deal is dealt, while the one being abandoned is still the board's.
    ///
    /// A deal left part-played is a deal that was played and not won, and leaving it out
    /// would make the averages flatter — the games people give up on are the slow ones. A
    /// deal nobody has moved a card on is not a game at all and records nothing.
    /// </summary>
    public bool Abandon()
    {
        if (board.State != GameState.Playing || recordedDeal == board.DealId || !Started)
        {
            return false;
        }

        recordedDeal = board.DealId;
        Score.Games++;
        RecordTime(won: false);
        Changed?.Invoke();

        return true;
    }

    /// <summary>Renames the player and reports the change so it gets saved.</summary>
    public void SetNickname(string nickname)
    {
        Score.Nickname = nickname.Trim();
        Changed?.Invoke();
    }

    /// <summary>Silences the board, or lets it speak again. It rides along with the record
    /// because that is what the host already persists.</summary>
    public void ToggleMute()
    {
        Score.Muted = !Score.Muted;
        Changed?.Invoke();
    }

    /// <summary>Shows the draw-time overlay, or puts it away. Saved with the record for the
    /// same reason as the mute.</summary>
    public void ToggleStats()
    {
        Score.ShowStats = !Score.ShowStats;
        Changed?.Invoke();
    }

    /// <summary>Switches between turning one card off the stock and turning three. Rides
    /// along with the record for the same reason the mute does.</summary>
    public void ToggleDrawMode()
    {
        Score.DrawMode = Score.DrawMode == DrawMode.DrawThree ? DrawMode.DrawOne : DrawMode.DrawThree;
        Changed?.Invoke();
    }

    /// <summary>The record in the form the host stores it. What storage that is — a browser's
    /// localStorage, a file, nothing at all — is the host's business and not named here.</summary>
    public string ToJson() => JsonSerializer.Serialize(Score);

    /// <summary>
    /// Takes on a stored record, reporting whether it could be read. A record we cannot read
    /// is a record we start over, not a broken game, so unreadable JSON is a false rather than
    /// an exception. Copied into the score we already have rather than swapped for it: the
    /// game and the board are holding that same object.
    /// </summary>
    public bool Load(string json)
    {
        try
        {
            var saved = JsonSerializer.Deserialize<PlayerScore>(json);

            if (saved is null)
            {
                return false;
            }

            Score.CopyFrom(saved);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Counts a deal once it is over — won, stuck, or proved lost — and once only, reporting
    /// whether anything was counted. Keyed to the deal rather than to a flag this class
    /// clears, because a finished board reports the same state on every frame until it is
    /// dealt again. And keyed to the deal rather than to the position: a player who takes a
    /// move back off a stuck board is still playing the same deal, and must not be counted
    /// twice for getting stuck twice.
    /// </summary>
    public bool Update()
    {
        if (board.State == GameState.Playing || recordedDeal == board.DealId)
        {
            return false;
        }

        recordedDeal = board.DealId;
        Score.Games++;

        if (board.State == GameState.Won)
        {
            Score.Wins++;
        }

        RecordTime(board.State == GameState.Won);
        Changed?.Invoke();
        return true;
    }

    /// <summary>Puts this deal's time into the record. Whole seconds: the averages are read
    /// as minutes and seconds, and nothing is the better for the fractions.</summary>
    private void RecordTime(bool won)
    {
        // Floored, to agree with the clock on the board: a deal the player watched reach
        // 2:05 must not be recorded as 2:06.
        long seconds = (long)(elapsedMs / 1000);

        LastGameSeconds = seconds;
        Score.TimedGames++;
        Score.TotalGameSeconds += seconds;

        if (won)
        {
            Score.TimedWins++;
            Score.TotalWinSeconds += seconds;
        }
    }
}
