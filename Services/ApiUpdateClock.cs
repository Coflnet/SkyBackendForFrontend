using System;
using System.Collections.Generic;
using System.Linq;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.Shared;

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
