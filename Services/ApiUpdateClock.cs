using System;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.Shared;

/// <summary>
/// Knows when the hypixel auction api updates, used to detect auctions that became publicly visible right away
/// </summary>
public class ApiUpdateClock
{
    public static readonly TimeSpan UpdateInterval = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan BedDuration = TimeSpan.FromSeconds(20);
    /// <summary>UTC time of the last known api update, null until first refresh</summary>
    public DateTime? LastUpdate { get; private set; }

    public void Set(DateTime lastUpdateUtc)
    {
        LastUpdate = AsUtc(lastUpdateUtc);
    }

    public DateTime? FirstUpdateAfter(DateTime t)
    {
        if (LastUpdate is not DateTime last)
            return null;
        var diff = (AsUtc(t) - last).Ticks;
        var interval = UpdateInterval.Ticks;
        var steps = diff >= 0 ? (diff + interval - 1) / interval : -(-diff / interval);
        return last + TimeSpan.FromTicks(steps * interval);
    }

    public bool IsPublicBed(SaveAuction auction, TimeSpan margin)
    {
        var start = AsUtc(auction.Start);
        var next = FirstUpdateAfter(start);
        return next.HasValue && next.Value <= start + BedDuration + margin;
    }

    private static DateTime AsUtc(DateTime t)
    {
        return t.Kind == DateTimeKind.Local ? t.ToUniversalTime() : DateTime.SpecifyKind(t, DateTimeKind.Utc);
    }
}
