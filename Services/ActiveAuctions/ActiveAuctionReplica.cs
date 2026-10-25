using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prometheus;

namespace Coflnet.Sky.Commands.Shared;

/// <summary>
/// Keeps a read replica of all active auctions in memory: initial load from SkyIndexer (database as fallback),
/// kept current via Kafka and reconciled against the indexer once a minute.
/// </summary>
public class ActiveAuctionReplica : BackgroundService
{
    private static readonly Gauge CountGauge = Metrics.CreateGauge("sky_api_active_replica_count", "Auctions in the replica");
    private static readonly Gauge ReadyGauge = Metrics.CreateGauge("sky_api_active_replica_ready", "1 when the replica finished loading");
    private static readonly Counter Ingested = Metrics.CreateCounter("sky_api_active_replica_ingested_total", "Ingested auctions", new CounterConfiguration { LabelNames = new[] { "topic" } });
    private static readonly Counter Removed = Metrics.CreateCounter("sky_api_active_replica_removed_total", "Removed auctions", new CounterConfiguration { LabelNames = new[] { "reason" } });
    private static readonly Counter ResyncFetched = Metrics.CreateCounter("sky_api_active_replica_resync_fetched_total", "Auctions fetched by the resync");
    private static readonly Counter ResyncRemoved = Metrics.CreateCounter("sky_api_active_replica_resync_removed_total", "Auctions removed by the resync");
    private static readonly Gauge SnapshotAge = Metrics.CreateGauge("sky_api_active_replica_snapshot_age_seconds", "Age of the query snapshot");

    private const int MaxFetchPerResync = 500;

    private readonly IConfiguration config;
    private readonly IActiveIndexerClient indexer;
    private readonly ActiveAuctionDbLoader dbLoader;
    private readonly ILogger<ActiveAuctionReplica> logger;
    private readonly Dictionary<long, int> misses = new();

    public ActiveAuctionSet Set { get; }
    public bool IsReady => Set.IsReady;

    /// <summary>How long to wait for the indexer before using the database</summary>
    internal TimeSpan IndexerWaitTimeout { get; set; } = TimeSpan.FromMinutes(2);
    internal TimeSpan IndexerRetryDelay { get; set; } = TimeSpan.FromSeconds(10);
    internal TimeSpan ResyncInterval { get; set; } = TimeSpan.FromSeconds(60);

    private static readonly (string Key, RemoveReason? Reason)[] Topics =
    {
        ("NEW_AUCTION", null), ("NEW_BID", null),
        ("AUCTION_ENDED", RemoveReason.Ended), ("SOLD_AUCTION", RemoveReason.Sold), ("MISSING_AUCTION", RemoveReason.Cancelled)
    };

    public ActiveAuctionReplica(IConfiguration config, ActiveAuctionSet set, IActiveIndexerClient indexer,
        ActiveAuctionDbLoader dbLoader, ILogger<ActiveAuctionReplica> logger)
    {
        this.config = config;
        Set = set;
        this.indexer = indexer;
        this.dbLoader = dbLoader;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!(config.GetValue<bool?>("ACTIVE_STORE:ENABLED") ?? true))
        {
            logger.LogInformation("Active auction store is disabled");
            return;
        }
        StartConsumers(ct);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await LoadInitialAsync(ct);
                break;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Initial load of active auctions failed, retrying");
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
            }
        }
        Set.MarkReady();
        ReadyGauge.Set(1);
        logger.LogInformation("Active auction replica ready with {count} auctions", Set.Count);
        await Task.WhenAll(
            Loop(ResyncInterval, ResyncOnceAsync, "resync", ct),
            Loop(TimeSpan.FromSeconds(10), _ => { Count(Set.ExpireEnded(DateTime.UtcNow), "expired"); return Task.CompletedTask; }, "expire", ct),
            Loop(TimeSpan.FromSeconds(2), _ => { Maintain(); return Task.CompletedTask; }, "snapshot", ct),
            Loop(TimeSpan.FromSeconds(60), _ => { Set.PurgeTombstones(DateTime.UtcNow); return Task.CompletedTask; }, "tombstones", ct));
    }

    private static void Count(int n, string reason)
    {
        if (n > 0)
            Removed.WithLabels(reason).Inc(n);
    }

    private void Maintain()
    {
        var snap = Set.RebuildSnapshotIfDirty();
        CountGauge.Set(Set.Count);
        SnapshotAge.Set((DateTime.UtcNow - snap.BuiltAt).TotalSeconds);
    }

    private async Task Loop(TimeSpan interval, Func<CancellationToken, Task> action, string name, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, ct);
                await action(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Active auction replica loop {name} failed", name);
            }
        }
    }

    private void StartConsumers(CancellationToken ct)
    {
        var groupId = $"sky-api-active-{Environment.MachineName}";
        foreach (var (key, reason) in Topics)
        {
            var topic = config["TOPICS:" + key];
            if (string.IsNullOrEmpty(topic))
            {
                logger.LogWarning("No topic configured for TOPICS:{key}, active replica will miss those events", key);
                continue;
            }
            var configKey = key;
            _ = Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        await Coflnet.Sky.Kafka.KafkaConsumer.Consume<SaveAuction>(config, topic, a =>
                        {
                            if (reason == null)
                                HandleNewAuction(a, configKey);
                            else
                                HandleRemoved(configKey, a);
                            return Task.CompletedTask;
                        }, ct, groupId, AutoOffsetReset.Latest);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                    catch (Exception e)
                    {
                        logger.LogError(e, "Kafka consumer for {topic} crashed", topic);
                        try { await Task.Delay(TimeSpan.FromSeconds(10), ct); } catch (OperationCanceledException) { return; }
                    }
                }
            }, ct);
        }
    }

    private static void EnsureUid(SaveAuction a)
    {
        if (a != null && a.UId == 0 && a.Uuid != null)
            a.UId = AuctionService.Instance.GetId(a.Uuid);
    }

    /// <summary>Handles a new auction or bid event</summary>
    internal void HandleNewAuction(SaveAuction a, string topic)
    {
        EnsureUid(a);
        if (Set.Upsert(a, topic))
            Ingested.WithLabels(topic).Inc();
    }

    /// <summary>Handles an ended, sold or cancelled event, <paramref name="topic"/> is the TOPICS config key</summary>
    internal void HandleRemoved(string topic, SaveAuction a)
    {
        EnsureUid(a);
        if (a == null)
            return;
        var reason = Topics.FirstOrDefault(t => t.Key == topic).Reason ?? RemoveReason.Manual;
        if (Set.Remove(a.UId, reason))
            Removed.WithLabels(reason.ToString().ToLowerInvariant()).Inc();
    }

    /// <summary>Loads from the indexer, falls back to the database when it stays unavailable</summary>
    internal async Task LoadInitialAsync(CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + IndexerWaitTimeout;
        while (true)
        {
            try
            {
                var n = 0;
                await foreach (var a in indexer.StreamAllAsync(ct))
                {
                    if (Set.Upsert(a, "indexer"))
                        n++;
                }
                if (n > 0)
                    return;
                logger.LogInformation("Indexer returned no active auctions yet");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                logger.LogWarning("Indexer not available for the initial load: {message}", e.Message);
            }
            if (DateTime.UtcNow >= deadline)
                break;
            await Task.Delay(IndexerRetryDelay, ct);
        }
        logger.LogWarning("Falling back to the database for the initial active auction load");
        var now = DateTime.UtcNow;
        var lower = await dbLoader.FindLowerBoundIdAsync(now.AddDays(-15), ct);
        await foreach (var a in dbLoader.LoadActiveAsync(lower, now, ct))
            Set.Upsert(a, "db");
    }

    /// <summary>One reconciliation cycle against the indexer</summary>
    internal async Task ResyncOnceAsync(CancellationToken ct)
    {
        var remote = await indexer.GetUidEndTicksAsync(ct);
        if (remote == null || remote.Count == 0)
            return;
        var local = Set.UidEndTicks();
        foreach (var uid in local.Keys)
        {
            if (remote.ContainsKey(uid))
            {
                misses.Remove(uid);
                continue;
            }
            var count = misses.GetValueOrDefault(uid) + 1;
            if (count >= 2)
            {
                misses.Remove(uid);
                if (Set.Remove(uid, RemoveReason.Summary))
                {
                    ResyncRemoved.Inc();
                    Removed.WithLabels("summary").Inc();
                }
            }
            else
                misses[uid] = count;
        }
        foreach (var stale in misses.Keys.Where(u => !local.ContainsKey(u)).ToList())
            misses.Remove(stale);

        var now = DateTime.UtcNow.Ticks;
        var missing = remote.Where(p => p.Value > now && !local.ContainsKey(p.Key)).Select(p => p.Key).Take(MaxFetchPerResync).ToList();
        foreach (var uid in missing)
        {
            var a = await indexer.GetAuctionAsync(uid, ct);
            if (a != null && Set.Upsert(a, "resync"))
                ResyncFetched.Inc();
        }
    }
}
