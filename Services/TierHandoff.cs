using System;
using System.Threading.Tasks;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.Shared;

/// <summary>Outcome of a handoff step: when the tracker was checked, whether a higher tier had matched by then and the release the step is measured from.</summary>
public record HandoffDecision(DateTime DecidedAt, bool Contested, DateTime ReleasedToHigherAt);

/// <summary>
/// Spaces out the release of a flip between tiers and gives lower tiers extra time
/// when a higher tier matched the auction, so that paying users are not outrun by the tier below.
/// </summary>
public class TierHandoff
{
    public TimeSpan Step { get; init; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan GraceMin { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan GraceMax { get; init; } = TimeSpan.FromSeconds(5);

    private readonly IFlipMatchTracker tracker;
    public IFlipMatchTracker Tracker => tracker;
    private readonly Func<TimeSpan, Task> delay;
    private readonly Func<DateTime> now;
    private readonly Random random;

    public TierHandoff(IFlipMatchTracker tracker, Func<TimeSpan, Task> delay = null, Func<DateTime> now = null, Random random = null)
    {
        this.tracker = tracker;
        this.delay = delay ?? (span => Task.Delay(span));
        this.now = now ?? (() => DateTime.UtcNow);
        this.random = random ?? Random.Shared;
    }

    /// <summary>
    /// Waits until releasedToHigherAt + (minHold ?? Step). If afterwards any tier above nextTier matched
    /// waits until releasedToHigherAt + grace.
    /// </summary>
    public async Task<HandoffDecision> AwaitReleaseTo(long auctionUId, AccountTier nextTier, DateTime releasedToHigherAt, TimeSpan? minHold = null)
    {
        await WaitUntil(releasedToHigherAt + (minHold ?? Step));
        var decidedAt = now();
        if (tracker == null || tracker.HighestMatchedTier(auctionUId) <= nextTier)
            return new HandoffDecision(decidedAt, false, releasedToHigherAt);
        await WaitUntil(releasedToHigherAt + RandomGrace());
        return new HandoffDecision(decidedAt, true, releasedToHigherAt);
    }

    private TimeSpan RandomGrace()
    {
        return GraceMin + (GraceMax - GraceMin) * random.NextDouble();
    }

    private async Task WaitUntil(DateTime target)
    {
        var span = target - now();
        if (span <= TimeSpan.Zero)
            return;
        await delay(span).ConfigureAwait(false);
    }
}
