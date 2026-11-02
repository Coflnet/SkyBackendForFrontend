using System.Linq;
using Coflnet.Sky.Commands.Shared;
using AwesomeAssertions;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Tests;

public class InMemoryFlipMatchTrackerTests
{
    [Test]
    public void UnknownAuctionIsNone()
    {
        Assert.That(new InMemoryFlipMatchTracker().HighestMatchedTier(5), Is.EqualTo(AccountTier.NONE));
    }

    [Test]
    public void LowerTierDoesNotDowngrade()
    {
        var tracker = new InMemoryFlipMatchTracker();
        tracker.ReportMatch(1, AccountTier.PREMIUM_PLUS);
        tracker.ReportMatch(1, AccountTier.PREMIUM);
        Assert.That(tracker.HighestMatchedTier(1), Is.EqualTo(AccountTier.PREMIUM_PLUS));
    }

    [Test]
    public void ReportReturnsTrueOnlyWhenHighestIncreases()
    {
        var tracker = new InMemoryFlipMatchTracker();
        Assert.That(tracker.ReportMatch(1, AccountTier.PREMIUM), Is.True);
        Assert.That(tracker.ReportMatch(1, AccountTier.PREMIUM), Is.False);
        Assert.That(tracker.ReportMatch(1, AccountTier.STARTER_PREMIUM), Is.False);
        Assert.That(tracker.ReportMatch(1, AccountTier.PREMIUM_PLUS), Is.True);
    }

    [Test]
    public void HigherTierReplacesLower()
    {
        var tracker = new InMemoryFlipMatchTracker();
        tracker.ReportMatch(1, AccountTier.STARTER_PREMIUM);
        tracker.ReportMatch(1, AccountTier.PREMIUM);
        Assert.That(tracker.HighestMatchedTier(1), Is.EqualTo(AccountTier.PREMIUM));
    }

    [Test]
    public void ReachedAtIsNullForUnknownAndUnreachedLevels()
    {
        var tracker = new InMemoryFlipMatchTracker();
        tracker.ReachedAt(1, AccountTier.STARTER_PREMIUM).Should().BeNull();
        tracker.ReportMatch(1, AccountTier.PREMIUM);
        tracker.ReachedAt(1, AccountTier.PREMIUM_PLUS).Should().BeNull();
        tracker.ReachedAt(1, AccountTier.NONE).Should().BeNull();
    }

    [Test]
    public void ReachedAtFillsSkippedLevelsAndKeepsEarlierTimes()
    {
        var tracker = new InMemoryFlipMatchTracker();
        tracker.ReportMatch(1, AccountTier.PREMIUM);
        var first = tracker.ReachedAt(1, AccountTier.PREMIUM);
        first.Should().NotBeNull();
        tracker.ReachedAt(1, AccountTier.STARTER_PREMIUM).Should().Be(first);
        System.Threading.Thread.Sleep(5);

        tracker.ReportMatch(1, AccountTier.SUPER_PREMIUM);

        tracker.ReachedAt(1, AccountTier.PREMIUM).Should().Be(first);
        var later = tracker.ReachedAt(1, AccountTier.SUPER_PREMIUM);
        later.Should().BeAfter(first.Value);
        tracker.ReachedAt(1, AccountTier.PREMIUM_PLUS).Should().Be(later);
    }

    [Test]
    public void LowerReportDoesNotChangeReachedAt()
    {
        var tracker = new InMemoryFlipMatchTracker();
        tracker.ReportMatch(1, AccountTier.PREMIUM_PLUS);
        var before = tracker.ReachedAt(1, AccountTier.PREMIUM);
        System.Threading.Thread.Sleep(5);
        tracker.ReportMatch(1, AccountTier.STARTER_PREMIUM);
        tracker.ReachedAt(1, AccountTier.PREMIUM).Should().Be(before);
        tracker.ReachedAt(1, AccountTier.STARTER_PREMIUM).Should().Be(before);
    }

    [Test]
    public void OldEntriesArePrunedWhenOverThreshold()
    {
        var now = System.DateTime.UtcNow;
        var tracker = new InMemoryFlipMatchTracker(() => now);
        for (var i = 0; i < 999; i++)
            tracker.ReportMatch(i, AccountTier.PREMIUM);
        now = now.AddMinutes(10);

        tracker.ReportMatch(99999, AccountTier.PREMIUM); // 1000th write triggers the prune

        Assert.That(tracker.HighestMatchedTier(5), Is.EqualTo(AccountTier.NONE));
        Assert.That(tracker.HighestMatchedTier(99999), Is.EqualTo(AccountTier.PREMIUM));
    }
}
