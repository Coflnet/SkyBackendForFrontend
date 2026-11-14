using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.Shared;

/// <summary>A handoff step towards <paramref name="NextTier"/> and what was decided</summary>
public record StepDecision(AccountTier NextTier, HandoffDecision Decision);

/// <summary>What the audit measured for one auction</summary>
public record AuditFindings(
    IReadOnlyList<(AccountTier Step, double KnownAfterSeconds)> KnownDelays,
    IReadOnlyList<AccountTier> LateSteps);

/// <summary>
/// Measures after the fact whether the handoff step was long enough: when did the next higher tier become known
/// compared to the release the step is measured from, and did a flip move on uncontested although a higher tier matched shortly after.
/// Only the first cascade per auction is audited, later deliveries of the same auction would see the earlier match as known.
/// </summary>
public class HandoffAudit
{
    public const string SourceApi = "api";
    public const string SourcePreApi = "preapi";
    private const int PruneEveryClaims = 1000;
    private static readonly TimeSpan ClaimMaxAge = TimeSpan.FromMinutes(2);

    static readonly Prometheus.Histogram knownSeconds = Prometheus.Metrics.CreateHistogram("sky_commands_flip_match_known_seconds",
        "Seconds from the step's reference release until this instance knew a tier above the step's tier matched (negative when known before)",
        new Prometheus.HistogramConfiguration
        {
            LabelNames = ["source", "step"],
            Buckets = [-1, -0.5, -0.25, -0.1, 0, 0.025, 0.05, 0.075, 0.1, 0.15, 0.2, 0.25, 0.3, 0.4, 0.5, 0.75, 1, 1.5, 2, 3, 5]
        });
    static readonly Prometheus.Counter lateMatches = Prometheus.Metrics.CreateCounter("sky_commands_flip_late_match_total",
        "Handoff steps that went uncontested although a higher tier matched afterwards", new Prometheus.CounterConfiguration { LabelNames = ["source", "step"] });
    static readonly Prometheus.Counter decisions = Prometheus.Metrics.CreateCounter("sky_commands_flip_handoff_decisions_total",
        "Handoff step decisions", new Prometheus.CounterConfiguration { LabelNames = ["source", "step", "contested"] });

    private readonly ConcurrentDictionary<long, DateTime> claimed = new();
    private int claims;

    /// <summary>How long to wait after the cascade before reading the tracker</summary>
    public TimeSpan MatchAuditWindow { get; set; } = TimeSpan.FromSeconds(3);

    private readonly IFlipMatchTracker tracker;
    private readonly Func<TimeSpan, Task> delay;

    public HandoffAudit(IFlipMatchTracker tracker, Func<TimeSpan, Task> delay = null)
    {
        this.tracker = tracker;
        this.delay = delay ?? (span => Task.Delay(span));
    }

    /// <summary>true for the first cascade of an auction on this instance, only that one should be audited</summary>
    public bool TryClaim(long auctionUId)
    {
        var now = DateTime.UtcNow;
        if (Interlocked.Increment(ref claims) % PruneEveryClaims == 0)
            PruneClaims(now);
        return claimed.TryAdd(auctionUId, now);
    }

    private void PruneClaims(DateTime now)
    {
        foreach (var old in claimed.Where(c => now - c.Value > ClaimMaxAge).Select(c => c.Key).ToList())
            claimed.TryRemove(old, out _);
    }

    /// <summary>Runs the audit detached; failures are swallowed as this is only measuring</summary>
    public void Schedule(long auctionUId, string source, IReadOnlyList<StepDecision> steps)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await AuditAfterWindow(auctionUId, source, steps).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                dev.Logger.Instance.Error(e, "handoff audit");
            }
        });
    }

    public async Task<AuditFindings> AuditAfterWindow(long auctionUId, string source, IReadOnlyList<StepDecision> steps)
    {
        await delay(MatchAuditWindow).ConfigureAwait(false);
        return Audit(auctionUId, source, steps);
    }

    public AuditFindings Audit(long auctionUId, string source, IReadOnlyList<StepDecision> steps)
    {
        if (tracker == null)
            return new AuditFindings([], []);
        var delays = new List<(AccountTier, double)>();
        var late = new List<AccountTier>();
        foreach (var step in steps)
        {
            var higherAt = tracker.ReachedAt(auctionUId, step.NextTier + 1);
            ObserveKnownDelay(source, step, higherAt, delays);
            if (RecordDecision(source, step, higherAt))
                late.Add(step.NextTier);
        }
        return new AuditFindings(delays, late);
    }

    private static void ObserveKnownDelay(string source, StepDecision step, DateTime? higherAt, List<(AccountTier, double)> delays)
    {
        if (higherAt == null)
            return;
        var seconds = (higherAt.Value - step.Decision.ReleasedToHigherAt).TotalSeconds;
        knownSeconds.WithLabels(source, step.NextTier.ToString()).Observe(seconds);
        delays.Add((step.NextTier, seconds));
    }

    /// <returns>true when the step was late</returns>
    private static bool RecordDecision(string source, StepDecision step, DateTime? higherAt)
    {
        var label = step.NextTier.ToString();
        decisions.WithLabels(source, label, step.Decision.Contested ? "true" : "false").Inc();
        if (step.Decision.Contested || higherAt == null || higherAt.Value <= step.Decision.DecidedAt)
            return false;
        lateMatches.WithLabels(source, label).Inc();
        return true;
    }
}
