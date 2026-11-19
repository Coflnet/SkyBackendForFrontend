using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Shared;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Tests;

public class TierHandoffTests
{
    private DateTime clock;
    private List<TimeSpan> waits;
    private InMemoryFlipMatchTracker tracker;
    private DateTime start;

    [SetUp]
    public void Setup()
    {
        start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        clock = start;
        waits = new();
        tracker = new InMemoryFlipMatchTracker();
    }

    private TierHandoff Create(IFlipMatchTracker t)
    {
        return new TierHandoff(t, span => { waits.Add(span); clock += span; return Task.CompletedTask; }, () => clock, new Random(7));
    }

    private TimeSpan TotalWait => TimeSpan.FromTicks(waits.Sum(w => w.Ticks));

    [Test]
    public async Task NoMatchWaitsOnlyStep()
    {
        var handoff = Create(tracker);
        await handoff.AwaitReleaseTo(1, AccountTier.PREMIUM, start);
        Assert.That(TotalWait, Is.EqualTo(handoff.Step));
    }

    [Test]
    public async Task MatchAboveNextTierWaitsGrace()
    {
        tracker.ReportMatch(1, AccountTier.PREMIUM_PLUS);
        var handoff = Create(tracker);
        await handoff.AwaitReleaseTo(1, AccountTier.PREMIUM, start);
        Assert.That(TotalWait, Is.InRange(handoff.GraceMin, handoff.GraceMax));
    }

    [TestCase(AccountTier.PREMIUM)]
    [TestCase(AccountTier.STARTER_PREMIUM)]
    public async Task MatchByNextTierOrBelowWaitsOnlyStep(AccountTier matched)
    {
        tracker.ReportMatch(1, matched);
        var handoff = Create(tracker);
        await handoff.AwaitReleaseTo(1, AccountTier.PREMIUM, start);
        Assert.That(TotalWait, Is.EqualTo(handoff.Step));
    }

    [Test]
    public async Task MinHoldIsHonoured()
    {
        var handoff = Create(tracker);
        await handoff.AwaitReleaseTo(1, AccountTier.PREMIUM, start, TimeSpan.FromSeconds(1));
        Assert.That(TotalWait, Is.EqualTo(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public async Task NullTrackerWaitsOnlyMinHold()
    {
        var handoff = Create(null);
        await handoff.AwaitReleaseTo(1, AccountTier.NONE, start, TimeSpan.FromSeconds(1));
        Assert.That(TotalWait, Is.EqualTo(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public async Task ReleaseInThePastDoesNotWaitNegative()
    {
        var handoff = Create(tracker);
        await handoff.AwaitReleaseTo(1, AccountTier.PREMIUM, start - TimeSpan.FromMinutes(1));
        Assert.That(waits, Is.Empty);
    }

    [Test]
    public async Task UncontestedDecisionReportsTimeOfStepEnd()
    {
        var handoff = Create(tracker);
        var decision = await handoff.AwaitReleaseTo(1, AccountTier.PREMIUM, start);
        Assert.That(decision.Contested, Is.False);
        Assert.That(decision.ReleasedToHigherAt, Is.EqualTo(start));
        Assert.That(decision.DecidedAt, Is.EqualTo(start + handoff.Step));
    }

    [Test]
    public async Task ContestedDecisionIsFlaggedAndDecidedBeforeGrace()
    {
        tracker.ReportMatch(1, AccountTier.PREMIUM_PLUS);
        var handoff = Create(tracker);
        var decision = await handoff.AwaitReleaseTo(1, AccountTier.PREMIUM, start);
        Assert.That(decision.Contested, Is.True);
        Assert.That(decision.DecidedAt, Is.EqualTo(start + handoff.Step));
    }
}
