using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AwesomeAssertions;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Tests;

public class HandoffAuditTests
{
    private ManualTracker tracker;
    private HandoffAudit audit;
    private DateTime released;

    [SetUp]
    public void Setup()
    {
        tracker = new ManualTracker();
        audit = new HandoffAudit(tracker, _ => Task.CompletedTask);
        released = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    }

    private StepDecision Step(AccountTier next, DateTime decidedAt, bool contested = false, DateTime? releasedToHigherAt = null) =>
        new(next, new HandoffDecision(decidedAt, contested, releasedToHigherAt ?? released));

    [Test]
    public void UncontestedStepWithLaterHigherMatchIsLate()
    {
        var decidedAt = released.AddMilliseconds(500);
        tracker.Set(AccountTier.PREMIUM_PLUS, released.AddMilliseconds(700));

        var findings = audit.Audit(1, "api", [Step(AccountTier.PREMIUM, decidedAt)]);

        findings.LateSteps.Should().Equal(AccountTier.PREMIUM);
    }

    [Test]
    public void MatchBeforeDecisionIsNotLate()
    {
        tracker.Set(AccountTier.PREMIUM_PLUS, released.AddMilliseconds(200));

        var findings = audit.Audit(1, "api", [Step(AccountTier.PREMIUM, released.AddMilliseconds(500))]);

        findings.LateSteps.Should().BeEmpty();
    }

    [Test]
    public void ContestedStepIsNeverLate()
    {
        tracker.Set(AccountTier.PREMIUM_PLUS, released.AddMilliseconds(700));

        var findings = audit.Audit(1, "api", [Step(AccountTier.PREMIUM, released.AddMilliseconds(500), true)]);

        findings.LateSteps.Should().BeEmpty();
    }

    [Test]
    public void NoHigherMatchIsNotLate()
    {
        var findings = audit.Audit(1, "api", [Step(AccountTier.NONE, released.AddMilliseconds(500))]);
        findings.LateSteps.Should().BeEmpty();
        findings.KnownDelays.Should().BeEmpty();
    }

    [Test]
    public void KnownDelayIsRelativeToTheStepsOwnRelease()
    {
        var premiumReleased = released.AddMilliseconds(500);
        tracker.Set(AccountTier.PREMIUM_PLUS, released.AddMilliseconds(800));

        var findings = audit.Audit(1, "api", [Step(AccountTier.STARTER_PREMIUM, premiumReleased.AddMilliseconds(500), true, premiumReleased)]);

        findings.KnownDelays.Should().ContainSingle(d => d.Step == AccountTier.STARTER_PREMIUM && Math.Abs(d.KnownAfterSeconds - 0.3) < 1e-6);
    }

    [Test]
    public void KnownDelayCanBeNegative()
    {
        tracker.Set(AccountTier.PREMIUM_PLUS, released.AddMilliseconds(-100));

        var findings = audit.Audit(1, "api", [Step(AccountTier.PREMIUM, released.AddMilliseconds(500), true)]);

        findings.KnownDelays.Should().ContainSingle(d => Math.Abs(d.KnownAfterSeconds + 0.1) < 1e-6);
    }

    [Test]
    public void TierAtOrBelowTheStepIsNotMeasured()
    {
        tracker.Set(AccountTier.PREMIUM, released.AddMilliseconds(100));

        var findings = audit.Audit(1, "api", [Step(AccountTier.PREMIUM, released.AddMilliseconds(500))]);

        findings.KnownDelays.Should().BeEmpty();
        findings.LateSteps.Should().BeEmpty();
    }

    [Test]
    public void OnlyFirstClaimPerAuctionSucceeds()
    {
        audit.TryClaim(5).Should().BeTrue();
        audit.TryClaim(5).Should().BeFalse();
        audit.TryClaim(6).Should().BeTrue();
    }

    [Test]
    public async Task AuditAfterWindowWaitsConfiguredWindow()
    {
        TimeSpan? waited = null;
        audit = new HandoffAudit(tracker, span => { waited = span; return Task.CompletedTask; }) { MatchAuditWindow = TimeSpan.FromSeconds(2) };
        tracker.Set(AccountTier.PREMIUM_PLUS, released.AddMilliseconds(300));

        var findings = await audit.AuditAfterWindow(1, "api", [Step(AccountTier.PREMIUM, released.AddMilliseconds(500), true)]);

        waited.Should().Be(TimeSpan.FromSeconds(2));
        findings.KnownDelays.Should().ContainSingle();
    }

    /// <summary>Tracker with directly controllable times</summary>
    private class ManualTracker : IFlipMatchTracker
    {
        private AccountTier highest;
        private DateTime at;

        public void Set(AccountTier tier, DateTime when) { highest = tier; at = when; }
        public bool ReportMatch(long auctionUId, AccountTier tier) => false;
        public AccountTier HighestMatchedTier(long auctionUId) => highest;
        public DateTime? ReachedAt(long auctionUId, AccountTier tier) => tier > AccountTier.NONE && highest >= tier ? at : null;
    }
}
