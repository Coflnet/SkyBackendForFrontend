using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.Shared;

public interface IFlipMatchTracker
{
    /// <summary>A connected user's filter matched this auction; the flip is sent or waiting for its fairness delay.</summary>
    /// <returns>true when the highest known tier for the auction increased</returns>
    bool ReportMatch(long auctionUId, AccountTier tier);
    /// <summary>Highest tier that matched, AccountTier.NONE when nobody did.</summary>
    AccountTier HighestMatchedTier(long auctionUId);
    /// <summary>When this instance first knew that some tier >= <paramref name="tier"/> matched, null if never.</summary>
    DateTime? ReachedAt(long auctionUId, AccountTier tier);
}

public class InMemoryFlipMatchTracker : IFlipMatchTracker
{
    private const int PruneEveryWrites = 1000;
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(2);
    private readonly ConcurrentDictionary<long, MatchEntry> matches = new();
    private int writes;
    private readonly Func<DateTime> clock;

    public InMemoryFlipMatchTracker() : this(() => DateTime.UtcNow) { }

    internal InMemoryFlipMatchTracker(Func<DateTime> clock)
    {
        this.clock = clock;
    }

    public bool ReportMatch(long auctionUId, AccountTier tier)
    {
        var now = clock();
        if (Interlocked.Increment(ref writes) % PruneEveryWrites == 0)
            Prune(now);
        return TryRaise(auctionUId, tier, now);
    }

    private bool TryRaise(long auctionUId, AccountTier tier, DateTime now)
    {
        while (true)
        {
            if (!matches.TryGetValue(auctionUId, out var existing))
            {
                if (matches.TryAdd(auctionUId, MatchEntry.Empty.Raise(tier, now)))
                    return tier > AccountTier.NONE;
                continue;
            }
            if (existing.Tier >= tier)
                return false;
            if (matches.TryUpdate(auctionUId, existing.Raise(tier, now), existing))
                return true;
        }
    }

    public AccountTier HighestMatchedTier(long auctionUId)
    {
        return matches.TryGetValue(auctionUId, out var entry) ? entry.Tier : AccountTier.NONE;
    }

    public DateTime? ReachedAt(long auctionUId, AccountTier tier)
    {
        return matches.TryGetValue(auctionUId, out var entry) ? entry.ReachedAt(tier) : null;
    }

    private void Prune(DateTime now)
    {
        foreach (var old in matches.Where(m => now - m.Value.At > MaxAge).Select(m => m.Key).ToList())
            matches.TryRemove(old, out _);
    }

    /// <summary>Immutable value so updates can use TryUpdate; holds when each tier level was first known.</summary>
    private readonly record struct MatchEntry(AccountTier Tier, DateTime At, DateTime Starter, DateTime Premium, DateTime PremiumPlus, DateTime Super)
    {
        public static readonly MatchEntry Empty = default;

        public MatchEntry Raise(AccountTier tier, DateTime now)
        {
            var result = this with { Tier = tier, At = now };
            for (var level = Tier + 1; level <= tier; level++)
                result = result.WithLevel(level, now);
            return result;
        }

        public DateTime? ReachedAt(AccountTier tier)
        {
            if (tier <= AccountTier.NONE || Tier < tier)
                return null;
            return tier switch
            {
                AccountTier.STARTER_PREMIUM => Starter,
                AccountTier.PREMIUM => Premium,
                AccountTier.PREMIUM_PLUS => PremiumPlus,
                AccountTier.SUPER_PREMIUM => Super,
                _ => null
            };
        }

        private MatchEntry WithLevel(AccountTier level, DateTime at) => level switch
        {
            AccountTier.STARTER_PREMIUM => this with { Starter = at },
            AccountTier.PREMIUM => this with { Premium = at },
            AccountTier.PREMIUM_PLUS => this with { PremiumPlus = at },
            AccountTier.SUPER_PREMIUM => this with { Super = at },
            _ => this
        };
    }
}
