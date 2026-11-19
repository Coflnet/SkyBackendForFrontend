using System;
using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.Shared;

/// <summary>Where a flip sits relative to the api arrival. Seconds are negative if before.</summary>
public readonly record struct ApiRelativeResult(string Source, double Seconds, bool InBed);

/// <summary>
/// Knows when the hypixel auction api updates and when the data of an update becomes available to us.
/// Two things are modeled explicitly:
/// the update grid (the <c>lastUpdated</c> stamps hypixel puts on the page, 60s apart) and
/// the arrival offset (first new auction arrival minus lastUpdated, ~7s because cloudflare finishes the page late).
/// Time 0 of the flip timing metrics is the arrival.
/// </summary>
public class ApiUpdateClock
{
    public static readonly TimeSpan UpdateInterval = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan BedDuration = TimeSpan.FromSeconds(20);
    /// <summary>Offset assumed until arrivals were observed</summary>
    public static readonly TimeSpan FallbackArrivalOffset = TimeSpan.FromSeconds(7);
    /// <summary>The updater <c>/api/time</c> endpoint returns lastUpdated plus this</summary>
    public static readonly TimeSpan ApiTimeEndpointAddition = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxPlausibleOffset = TimeSpan.FromSeconds(30);
    private const int KeepUpdates = 10;

    /// <summary>Largest absolute seconds a flip can plausibly be away from the api arrival</summary>
    public const double PlausibleLimitSeconds = 120;
    private static readonly double[] relativeBuckets =
        [-120, -90, -75, -60, -50, -40, -30, -25, -20, -15, -10, -7.5, -5, -3, -2, -1, -0.5, 0, 0.5, 1, 1.5, 2, 3, 5, 7.5, 10, 15, 20, 30, 40, 60, 90, 120];
    /// <summary>Shared buckets of all api relative histograms (a copy, the shared array cannot be changed)</summary>
    public static double[] RelativeBuckets => (double[])relativeBuckets.Clone();

    private readonly object sync = new();
    private readonly SortedDictionary<DateTime, DateTime> earliestArrivals = new();
    private TimeSpan? offset;

    /// <summary>UTC lastUpdated stamp of the newest known api update, null until first refresh</summary>
    public DateTime? LastUpdate { get; private set; }

    /// <summary>The offset in use (median of observed offsets, else the fallback)</summary>
    public TimeSpan ArrivalOffset
    {
        get { lock (sync) return offset ?? FallbackArrivalOffset; }
    }

    /// <summary>
    /// Sets the grid from the updater <c>/api/time</c> value which is lastUpdated + 10s, the 10s are removed here.
    /// Never moves the grid backwards.
    /// </summary>
    public void Set(DateTime apiTimeUtc)
    {
        SetLastUpdated(AsUtc(apiTimeUtc) - ApiTimeEndpointAddition);
    }

    /// <summary>Sets the grid from a real lastUpdated stamp. Never moves the grid backwards.</summary>
    public void SetLastUpdated(DateTime lastUpdated)
    {
        lock (sync)
        {
            var value = AsUtc(lastUpdated);
            if (LastUpdate is not DateTime current || value > current)
                LastUpdate = value;
        }
    }

    /// <summary>
    /// Records that data of the update stamped <paramref name="lastUpdated"/> was first seen at <paramref name="arrival"/>.
    /// Keeps the earliest arrival per update.
    /// </summary>
    public void ObserveArrival(DateTime lastUpdated, DateTime arrival)
    {
        lastUpdated = AsUtc(lastUpdated);
        arrival = AsUtc(arrival);
        lock (sync)
        {
            if (!earliestArrivals.TryGetValue(lastUpdated, out var known) || arrival < known)
                earliestArrivals[lastUpdated] = arrival;
            while (earliestArrivals.Count > KeepUpdates)
                earliestArrivals.Remove(earliestArrivals.Keys.First());
            if (LastUpdate is not DateTime current || lastUpdated > current)
                LastUpdate = lastUpdated;
            offset = MedianOffset();
        }
    }

    /// <summary>
    /// When the data of the update stamped <paramref name="lastUpdated"/> became available:
    /// the observed earliest arrival, else lastUpdated plus the estimated offset
    /// </summary>
    public DateTime? ArrivalOf(DateTime lastUpdated)
    {
        lastUpdated = AsUtc(lastUpdated);
        lock (sync)
        {
            if (earliestArrivals.TryGetValue(lastUpdated, out var arrival))
                return arrival;
            return lastUpdated + (offset ?? FallbackArrivalOffset);
        }
    }

    /// <summary>
    /// Predicted arrival of the first update whose lastUpdated is at or after <paramref name="t"/>.
    /// That arrival is the moment an auction started at t becomes public.
    /// </summary>
    public DateTime? FirstArrivalAfter(DateTime t)
    {
        var update = FirstUpdateStampAfter(t);
        return update.HasValue ? ArrivalOf(update.Value) : null;
    }

    /// <summary>True if the seconds are within the range a real send can have relative to the api arrival</summary>
    public static bool Plausible(double seconds)
    {
        return seconds >= -PlausibleLimitSeconds && seconds <= PlausibleLimitSeconds;
    }

    /// <summary>
    /// Where the flip comes from: "rust" (finder, FindTime is the receive time, no valid api reference), "preapi" or "api".
    /// Recheck flips (seller auctions re-requested by the flip tracker) are found the pre-api way and count as preapi.
    /// </summary>
    public static string FlipSource(LowPricedAuction flip)
    {
        if (flip.Finder == LowPricedAuction.FinderType.Rust)
            return "rust";
        var context = flip.Auction.Context;
        if (context == null || !context.ContainsKey("pre-api"))
            return "api";
        return "preapi";
    }

    /// <summary>
    /// Seconds between the arrival of the api update that made the auction public (time 0 = first new auction arrived) and now.
    /// Negative if before. InBed is true if the auction was still in its bed at that arrival.
    /// Null if the reference is unknown or does not exist (rust flips).
    /// </summary>
    public ApiRelativeResult? ApiRelative(LowPricedAuction flip, DateTime now)
    {
        var source = FlipSource(flip);
        if (source is not ("preapi" or "api"))
            return null;
        var isPreApi = source == "preapi";
        // api auctions carry the lastUpdated stamp as FindTime
        var reference = isPreApi ? FirstArrivalAfter(flip.Auction.Start) : ArrivalOf(flip.Auction.FindTime);
        if (reference == null)
            return null;
        var inBed = AsUtc(flip.Auction.Start) + BedDuration > reference.Value;
        return new ApiRelativeResult(source, (AsUtc(now) - reference.Value).TotalSeconds, inBed);
    }

    private DateTime? FirstUpdateStampAfter(DateTime t)
    {
        DateTime last;
        lock (sync)
        {
            if (LastUpdate is not DateTime l)
                return null;
            last = l;
        }
        var diff = (AsUtc(t) - last).Ticks;
        var interval = UpdateInterval.Ticks;
        var steps = diff >= 0 ? (diff + interval - 1) / interval : -(-diff / interval);
        return last + TimeSpan.FromTicks(steps * interval);
    }

    private TimeSpan? MedianOffset()
    {
        var offsets = earliestArrivals
            .Select(e => e.Value - e.Key)
            .Where(o => o >= TimeSpan.Zero && o <= MaxPlausibleOffset)
            .OrderBy(o => o)
            .ToList();
        if (offsets.Count == 0)
            return null;
        var mid = offsets.Count / 2;
        return offsets.Count % 2 == 1 ? offsets[mid] : TimeSpan.FromTicks((offsets[mid - 1].Ticks + offsets[mid].Ticks) / 2);
    }

    private static DateTime AsUtc(DateTime t)
    {
        return t.Kind == DateTimeKind.Local ? t.ToUniversalTime() : DateTime.SpecifyKind(t, DateTimeKind.Utc);
    }
}
