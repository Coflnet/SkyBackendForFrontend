using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Commands.Helper;
using Confluent.Kafka;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using RestSharp;
using Newtonsoft.Json;
using Coflnet.Sky.Core;
using Microsoft.Extensions.Hosting;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Coflnet.Kafka;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Commands.Shared
{

    /// <summary>
    /// Frontendfacing methods for the flipper
    /// </summary>
    public class FlipperService : BackgroundService
    {
        private ConcurrentDictionary<long, FlipConWrapper> Subs = new ConcurrentDictionary<long, FlipConWrapper>();
        private ConcurrentDictionary<long, FlipConWrapper> SlowSubs = new ConcurrentDictionary<long, FlipConWrapper>();
        private ConcurrentDictionary<long, FlipConWrapper> StarterSubs = new ConcurrentDictionary<long, FlipConWrapper>();
        private ConcurrentDictionary<long, FlipConWrapper> SuperSubs = new ConcurrentDictionary<long, FlipConWrapper>();
        private readonly object subscriptionLock = new object();
        public ConcurrentQueue<FlipInstance> Flipps = new ConcurrentQueue<FlipInstance>();
        public ServerFilterSumary FilterSumary { get; private set; } = new();
        /// <summary>
        /// Special hook
        /// </summary>
        public Func<FlipperService, LowPricedAuction, Task> PreApiLowPriceHandler { get; set; } = (s, auction) => Task.Delay(30_000);

        /// <summary>
        /// Wherether or not a given <see cref="SaveAuction.UId"/> was a flip or not
        /// </summary>
        private ConcurrentDictionary<long, bool> FlipIdLookup = new ConcurrentDictionary<long, bool>();
        public static readonly string ConsumeTopic = SimplerConfig.Config.Instance["TOPICS:FLIP"];
        public static readonly string LowPriceConsumeTopic = SimplerConfig.Config.Instance["TOPICS:LOW_PRICED"];
        public static readonly string AuctionConsumeTopic = SimplerConfig.Config.Instance["TOPICS:NEW_AUCTION"];

        private const string FoundFlippsKey = "foundFlipps";
        public int PremiumUserCount => Subs.Count() + SuperSubs.Count();

        static Prometheus.Histogram runtroughTime = Prometheus.Metrics.CreateHistogram("sky_commands_auction_to_flip_seconds", "Represents the time in seconds taken from loading the auction to sendingthe flip. (should be close to 0)",
            new Prometheus.HistogramConfiguration()
            {
                Buckets = Prometheus.Histogram.LinearBuckets(start: 1, width: 2, count: 10)
            });
        internal static Prometheus.Histogram releaseRelativeApi = Prometheus.Metrics.CreateHistogram("sky_commands_flip_release_relative_api_seconds",
            "Seconds a flip was released to a tier relative to the arrival of the api update data (negative = before). One observation per flip, tier and pod: for unique flips use max across pods or a single pod, not a sum",
            new Prometheus.HistogramConfiguration()
            {
                LabelNames = ["tier", "source", "bed"],
                Buckets = ApiUpdateClock.RelativeBuckets
            });
        internal static Prometheus.Counter releaseRelativeApiOutliers = Prometheus.Metrics.CreateCounter("sky_commands_flip_release_relative_api_outliers_total",
            "Flip releases that were implausibly far (over 120s) from the api arrival and are not part of the histogram",
            new Prometheus.CounterConfiguration() { LabelNames = ["tier", "source", "direction"] });
        static Prometheus.Histogram releaseTiming = Prometheus.Metrics.CreateHistogram("sky_commands_flip_release_seconds", "Seconds from auction FindTime until the flip was released to a tier",
            new Prometheus.HistogramConfiguration()
            {
                LabelNames = ["tier"],
                Buckets = [0.25, 0.5, 0.75, 1, 1.5, 2, 3, 5, 8, 13, 20, 40]
            });
        static Prometheus.Counter snipesReceived = Prometheus.Metrics.CreateCounter("sky_commands_snipes_received", "How many snipes were received");
        static Prometheus.Counter auctionsConsumed = Prometheus.Metrics.CreateCounter("sky_commands_auctions_received", "How many auctions were consumed");
        static Prometheus.Gauge tfmEnabled = Prometheus.Metrics.CreateGauge("sky_commands_tfm_enabled", "How many users have TFM enabled");

        /// <summary>
        /// Special load burst queue that will send out 5 flips at load
        /// </summary>
        private ConcurrentQueue<FlipInstance> LoadBurst = new ConcurrentQueue<FlipInstance>();
        /// <summary>
        /// Auctions whose flip was released to prem+ but has not reached the free tier yet (not in <see cref="FlipIdLookup"/> before that)
        /// </summary>
        private ConcurrentDictionary<long, DateTime> InCascade = new ConcurrentDictionary<long, DateTime>();
        private int cascadesInFlight;
        internal int CascadesInFlight => Volatile.Read(ref cascadesInFlight);
        private ConcurrentDictionary<long, DateTime> SoldAuctions = new ConcurrentDictionary<long, DateTime>();
        IConfiguration config;
        private ILogger<FlipperService> logger;

        public TierHandoff Handoff { get; set; }
        /// <summary>Measures whether the handoff step was long enough, see <see cref="HandoffAudit"/></summary>
        public HandoffAudit MatchAudit { get; set; }
        public ApiUpdateClock ApiClock { get; set; }

        public FlipperService(IConfiguration config, ILogger<FlipperService> logger, IFlipMatchTracker matchTracker = null, ApiUpdateClock apiClock = null)
        {
            this.config = config;
            this.logger = logger;
            Handoff = new TierHandoff(matchTracker ?? new InMemoryFlipMatchTracker());
            MatchAudit = new HandoffAudit(Handoff.Tracker);
            ApiClock = apiClock ?? new ApiUpdateClock();
        }

        public void AddConnectionPlus(IFlipConnection connection, bool sendHistory = true)
        {
            SubToTier(connection, sendHistory, SuperSubs);
        }

        public void AddConnection(IFlipConnection connection, bool sendHistory = true)
        {
            SubToTier(connection, sendHistory, Subs);
        }

        private void SubToTier(IFlipConnection connection, bool sendHistory, ConcurrentDictionary<long, FlipConWrapper> targetType)
        {
            var con = new FlipConWrapper(connection);
            ReplaceConnection(con, targetType);
            var toSendFlips = Flipps.Reverse().Take(250);
            if (sendHistory)
                SendFlipHistory(connection, toSendFlips, 100);
        }

        public void AddStarterConnection(IFlipConnection connection, bool sendHistory = true)
        {
            var con = new FlipConWrapper(connection);
            ReplaceConnection(con, StarterSubs);
            if (sendHistory)
                SendFlipHistory(connection, LoadBurst, 0);
        }

        public void AddNonConnection(IFlipConnection connection, bool sendHistory = true)
        {
            var con = new FlipConWrapper(connection);
            ReplaceConnection(con, SlowSubs);
            if (!sendHistory)
                return;
            SendFlipHistory(connection, LoadBurst, 0);
            if (Random.Shared.Next() % 30 == 0)
                logger.LogInformation("Added new con, now there are " + SlowSubs.Count);
            UpdateFilterSumaries();
        }

        public void RemoveConnection(IFlipConnection con)
        {
            lock (subscriptionLock)
            {
                Unsubscribe(Subs, con);
                Unsubscribe(SuperSubs, con);
                Unsubscribe(StarterSubs, con);
                Unsubscribe(SlowSubs, con);
            }
            UpdateFilterSumaries();
            ClearSoldBuffer();
        }

        private void ReplaceConnection(FlipConWrapper connection, ConcurrentDictionary<long, FlipConWrapper> targetType)
        {
            lock (subscriptionLock)
            {
                Unsubscribe(Subs, connection.Connection.Id);
                Unsubscribe(SuperSubs, connection.Connection.Id);
                Unsubscribe(StarterSubs, connection.Connection.Id);
                Unsubscribe(SlowSubs, connection.Connection.Id);
                targetType[connection.Connection.Id] = connection;
                connection.StartWorkers();
                connection.StartTierRefresh(UpdateConnectionTier);
            }
        }

        private Task UpdateConnectionTier(FlipConWrapper connection, AccountTier tier)
        {
            lock (subscriptionLock)
            {
                if (!OwnsConnection(connection))
                    return Task.CompletedTask;

                var targetType = tier switch
                {
                    AccountTier.SUPER_PREMIUM => SuperSubs,
                    AccountTier.PREMIUM_PLUS => SuperSubs,
                    AccountTier.PREMIUM => Subs,
                    AccountTier.STARTER_PREMIUM => StarterSubs,
                    _ => SlowSubs
                };
                if (targetType.TryGetValue(connection.Connection.Id, out var current)
                    && ReferenceEquals(current, connection))
                    return Task.CompletedTask;

                RemoveWrapper(Subs, connection);
                RemoveWrapper(SuperSubs, connection);
                RemoveWrapper(StarterSubs, connection);
                RemoveWrapper(SlowSubs, connection);
                targetType[connection.Connection.Id] = connection;
                connection.StartWorkers(tier);
            }
            UpdateFilterSumaries();
            return Task.CompletedTask;
        }

        private bool OwnsConnection(FlipConWrapper connection)
        {
            return OwnsConnection(Subs, connection)
                || OwnsConnection(SuperSubs, connection)
                || OwnsConnection(StarterSubs, connection)
                || OwnsConnection(SlowSubs, connection);
        }

        private static bool OwnsConnection(ConcurrentDictionary<long, FlipConWrapper> subscribers, FlipConWrapper connection)
        {
            return subscribers.TryGetValue(connection.Connection.Id, out var current)
                && ReferenceEquals(current, connection);
        }

        private static void RemoveWrapper(ConcurrentDictionary<long, FlipConWrapper> subscribers, FlipConWrapper connection)
        {
            subscribers.TryRemove(new KeyValuePair<long, FlipConWrapper>(connection.Connection.Id, connection));
        }


        private static void SendFlipHistory(IFlipConnection con, IEnumerable<FlipInstance> toSendFlips, int delay = 5000)
        {
            var end = new CancellationTokenSource(TimeSpan.FromMinutes(1)).Token;
            Task.Run(async () =>
            {
                try
                {
                    foreach (var item in toSendFlips.Where(f => f != null).ToList())
                    {
                        await con.SendFlip(item);

                        await Task.Delay(delay, end).ConfigureAwait(false);
                    }
                }
                catch (Exception e)
                {
                    dev.Logger.Instance.Error(e, "sending history");
                }
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Tell the flipper that an auction was sold
        /// </summary>
        /// <param name="auction"></param>
        public void AuctionSold(SaveAuction auction)
        {
            if (!FlipIdLookup.ContainsKey(auction.UId) && !InCascade.ContainsKey(auction.UId))
                return;
            SoldAuctions[auction.UId] = auction.End;
            var auctionUUid = auction.Uuid;
            Task.Run(() => NotifySubsInactiveAuction(auctionUUid));
        }

        private async Task NotifySubsInactiveAuction(string auctionUUid)
        {
            var inacive = new List<long>();
            foreach (var item in Subs)
            {
                if (!await item.Value.Connection.SendSold(auctionUUid))
                    inacive.Add(item.Key);
            }
            foreach (var item in SlowSubs)
            {
                if (!await item.Value.Connection.SendSold(auctionUUid))
                    inacive.Add(item.Key);
            }

            foreach (var item in inacive)
            {
                Unsubscribe(SlowSubs, item);
                Unsubscribe(Subs, item);
            }
        }

        public static FlipInstance LowPriceToFlip(LowPricedAuction flip)
        {
            var targetPrice = flip.Finder == LowPricedAuction.FinderType.SNIPER && flip.AdditionalProps.TryGetValue("mVal", out string mVal) ? long.Parse(mVal) : flip.TargetPrice;
            var interesting = PropertiesSelector.GetProperties(flip.Auction).OrderByDescending(a => a.Rating).Select(a => a.Value).ToList();
            var context = flip.AdditionalProps == null || flip.AdditionalProps.Count == 0 ? new Dictionary<string, string>() : new Dictionary<string, string>(flip.AdditionalProps);
            var flipIntance = new FlipInstance()
            {
                Auction = flip.Auction,
                MedianPrice = targetPrice,
                Uuid = flip.Auction.Uuid,
                Bin = flip.Auction.Bin,
                Name = flip.Auction.ItemName,
                Interesting = interesting,
                Tag = flip.Auction.Tag,
                Volume = flip.DailyVolume,
                Rarity = flip.Auction.Tier,
                Finder = flip.Finder,
                LowestBin = (flip.Finder == LowPricedAuction.FinderType.SNIPER || flip.Finder == LowPricedAuction.FinderType.USER) ? flip.TargetPrice : 0,
                Context = new(context)
            };
            return flipIntance;
        }

        public static async Task FillVisibilityProbs(FlipInstance flip, FlipSettings settings)
        {
            if (settings == null || settings.Visibility == null)
                return;
            var timeOut = new CancellationTokenSource(5000);
            if (settings.Visibility.Seller && flip.SellerName == null)
            {
                try
                {
                    flip.SellerName = (await DiHandler.ServiceProvider.GetRequiredService<PlayerName.Client.Api.IPlayerNameApi>()
                                    .PlayerNameNameUuidGetAsync(flip.Auction.AuctioneerId, 0, timeOut.Token).ConfigureAwait(false))?.Trim('"');
                }
                catch (TaskCanceledException)
                {
                    flip.SellerName = $"not-found";
                }
            }

            if (flip.LowestBin == 0 && (settings.Visibility.LowestBin || settings.Visibility.SecondLowestBin || settings.BasedOnLBin))
            {
                var lowestBin = await GetLowestBin(flip.Auction).ConfigureAwait(false);
                flip.LowestBin = lowestBin?.FirstOrDefault()?.Price;
                flip.SecondLowestBin = lowestBin?.Count >= 2 ? lowestBin[1].Price : 0L;
                if (settings.BasedOnLBin)
                    flip.MedianPrice = flip.LowestBin ?? flip.MedianPrice;
            }
        }

        public static async Task<List<ItemPrices.AuctionPreview>> GetLowestBin(SaveAuction auction)
        {
            var filters = new Dictionary<string, string>();
            var ulti = auction.Enchantments.Where(e => Constants.RelevantEnchants.Any(rel => rel.Type == e.Type && rel.Level <= e.Level)).FirstOrDefault();
            if (ulti != null)
            {
                filters["Enchantment"] = ulti.Type.ToString();
                filters["EnchantLvl"] = ulti.Level.ToString();
            }
            if (Constants.RelevantReforges.Contains(auction.Reforge))
            {
                filters["Reforge"] = auction.Reforge.ToString();
            }
            filters["Rarity"] = auction.Tier.ToString();
            var auctionsApi = DiHandler.GetService<Coflnet.Sky.Api.Client.Api.IAuctionsApi>();
            var exactLowest = await auctionsApi.ApiAuctionsTagItemTagActiveBinGetAsync(auction.Tag, filters);
            if (exactLowest?.Count > 1)
                return exactLowest.Select(a => new ItemPrices.AuctionPreview() { Price = Math.Max(a.StartingBid, a.HighestBidAmount), Uuid = a.Uuid, End = a.End, Seller = a.AuctioneerId }).ToList();
            return await ItemPrices.GetLowestBin(auction.Tag, auction.Tier);
        }

        /// <summary>
        /// Auction is no longer active for some reason
        /// </summary>
        /// <param name="uuid"></param>
        public async Task AuctionInactive(string uuid)
        {
            await NotifySubsInactiveAuction(uuid);
            var uid = AuctionService.Instance.GetId(uuid);
            SoldAuctions[uid] = DateTime.UtcNow;
        }



        /// <summary>
        /// Sends out new flips based on tier.
        /// (active on the light client)
        /// </summary>
        /// <param name="flip"></param>
        private async Task DeliverFlip(FlipInstance flip)
        {
            if (flip.Auction?.Start < DateTime.UtcNow - TimeSpan.FromMinutes(3) && flip.Auction?.Start != default)
                return; // skip old flips
            runtroughTime.Observe((DateTime.UtcNow - flip.Auction.FindTime).TotalSeconds);
            var tracer = DiHandler.GetService<ActivitySource>();
            using var activity = tracer.StartActivity("DeliverFlip")?.SetTag("uuid", flip.Auction.Uuid);
            flip.Finder = LowPricedAuction.FinderType.FLIPPER;

            if (flip.Auction.Context?.ContainsKey("pre-api") ?? true)
            {
                await PreApiLowPriceHandler.Invoke(this, new()
                {
                    AdditionalProps = flip.Context,
                    Auction = flip.Auction,
                    DailyVolume = flip.Volume,
                    Finder = flip.Finder,
                    TargetPrice = flip.MedianPrice
                });
            }
            activity.Log("sending to prem+");
            var uid = flip.UId;
            InCascade[uid] = DateTime.UtcNow;
            var releasedAt = await ReleaseTo(AccountTier.PREMIUM_PLUS, SuperSubs, subs => NotifyAll(flip, subs), flip.Auction.FindTime);
            var isPreApi = flip.Auction.Context?.ContainsKey("pre-api") ?? true;
            RunDetached(() => RunLowerCascade(flip, releasedAt, isPreApi), uid, "delivering flip to lower tiers");
        }

        /// <summary>
        /// Feeds the api clock with when this instance first got an auction of an api update.
        /// FindTime is the lastUpdated stamp (the upT context value is culture formatted and loses precision),
        /// fT is the time between lastUpdated and finding the auction.
        /// Rust flips are skipped: their FindTime is the receive time, not an api update stamp.
        /// </summary>
        internal void ObserveApiArrival(LowPricedAuction flip)
        {
            if (flip.Finder == LowPricedAuction.FinderType.Rust)
                return;
            var auction = flip.Auction;
            var context = auction.Context;
            if (context == null || !context.ContainsKey("upT") || !context.TryGetValue("fT", out var found))
                return;
            if (!TimeSpan.TryParse(found, System.Globalization.CultureInfo.InvariantCulture, out var findDelay))
                return;
            if (auction.FindTime == default)
                return;
            ApiClock.ObserveArrival(auction.FindTime, auction.FindTime + findDelay);
        }

        /// <param name="isPreApi">pre-api beds are premium plus exclusive until they end, so lower tiers wait for the api visibility</param>
        internal async Task RunLowerCascade(FlipInstance flip, DateTime releasedAt, bool isPreApi = false)
        {
            try
            {
                TierDelivery deliver = subs => NotifyAll(flip, subs);
                var uid = flip.UId;
                var findTime = flip.Auction.FindTime;
                if (isPreApi)
                    releasedAt = await WaitForApiVisibility(flip.Auction.Start, releasedAt);
                releasedAt = await HandOffAndRelease(uid, AccountTier.PREMIUM, Subs, deliver, findTime, releasedAt, isPreApi ? TimeSpan.FromSeconds(1) : null);
                releasedAt = await HandOffAndRelease(uid, AccountTier.STARTER_PREMIUM, StarterSubs, deliver, findTime, releasedAt);
                await Handoff.AwaitReleaseTo(uid, AccountTier.NONE, releasedAt);
                if (SoldAuctions.ContainsKey(uid))
                    flip.Sold = true;
                AddToHistory(flip);
                await ReleaseTo(AccountTier.NONE, SlowSubs, deliver, findTime);
            }
            finally
            {
                InCascade.TryRemove(flip.UId, out _);
            }
        }

        /// <summary>
        /// Runs the slower tiers without blocking the caller, so later flips of the same batch are not held up
        /// </summary>
        private void RunDetached(Func<Task> work, long uid, string errorMessage)
        {
            Interlocked.Increment(ref cascadesInFlight);
            _ = Task.Run(async () =>
            {
                try
                {
                    await work().ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    dev.Logger.Instance.Error(e, errorMessage);
                }
                finally
                {
                    Interlocked.Decrement(ref cascadesInFlight);
                }
            });
        }

        /// <summary>Tracing is optional, unavailable when no service provider is configured (tests)</summary>
        private static Activity StartTrace(string name, string uuid)
        {
            try
            {
                return DiHandler.GetService<ActivitySource>().StartActivity(name)?.SetTag("uuid", uuid);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private delegate Task TierDelivery(ConcurrentDictionary<long, FlipConWrapper> subscribers);

        /// <summary>Delivers to one tier and returns the time of the release</summary>
        private async Task<DateTime> ReleaseTo(AccountTier tier, ConcurrentDictionary<long, FlipConWrapper> subscribers, TierDelivery deliver, DateTime findTime, LowPricedAuction flip = null)
        {
            await deliver(subscribers).ConfigureAwait(false);
            var releasedAt = DateTime.UtcNow;
            releaseTiming.WithLabels(tier.ToString()).Observe((releasedAt - findTime).TotalSeconds);
            ObserveReleaseRelativeApi(tier, flip, releasedAt);
            return releasedAt;
        }

        internal void ObserveReleaseRelativeApi(AccountTier tier, LowPricedAuction flip, DateTime releasedAt)
        {
            try
            {
                var result = flip == null ? null : ApiClock.ApiRelative(flip, releasedAt);
                if (result is not ApiRelativeResult r)
                    return;
                if (!ApiUpdateClock.Plausible(r.Seconds))
                    releaseRelativeApiOutliers.WithLabels(tier.ToString(), r.Source, r.Seconds < 0 ? "early" : "late").Inc();
                else
                    releaseRelativeApi.WithLabels(tier.ToString(), r.Source, r.InBed ? "true" : "false").Observe(r.Seconds);
            }
            catch (Exception)
            {
                // metrics are best effort
            }
        }

        private async Task<DateTime> HandOffAndRelease(long uid, AccountTier tier, ConcurrentDictionary<long, FlipConWrapper> subscribers,
            TierDelivery deliver, DateTime findTime, DateTime releasedToHigherAt, TimeSpan? minHold = null,
            List<StepDecision> decisions = null, LowPricedAuction flip = null)
        {
            var decision = await Handoff.AwaitReleaseTo(uid, tier, releasedToHigherAt, minHold).ConfigureAwait(false);
            decisions?.Add(new StepDecision(tier, decision));
            return await ReleaseTo(tier, subscribers, deliver, findTime, flip).ConfigureAwait(false);
        }

        /// <summary>
        /// Remembers the flip for history requests and the load burst
        /// </summary>
        private void AddToHistory(FlipInstance flip)
        {
            if (FlipIdLookup.ContainsKey(flip.UId))
                return;
            Flipps.Enqueue(flip);
            FlipIdLookup[flip.UId] = true;
            if (Flipps.Count > 1500 && Flipps.TryDequeue(out FlipInstance result))
                FlipIdLookup.Remove(result.UId, out bool value);
            LoadBurst.Enqueue(flip);
            // runs concurrently, a plain Queue got corrupted (null entries when enumerated)
            if (LoadBurst.Count > 5)
                LoadBurst.TryDequeue(out _);
        }

        public async Task DeliverLowPricedAuctions(IEnumerable<LowPricedAuction> flips)
        {
            var stopAfterOneMinute = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(1));
            try
            {
                await Parallel.ForEachAsync(flips, stopAfterOneMinute.Token, async (item, s) =>
                {
                    await DeliverLowPricedAuction(item);
                }).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                // timed out after one minute
                Activity.Current?.AddEvent(new ActivityEvent("timeout"));
            }
        }


        public async Task DeliverLowPricedAuction(LowPricedAuction flip)
        {
            if (flip.Auction.Context != null)
                flip.Auction.Context["crec"] = (DateTime.UtcNow - flip.Auction.FindTime).ToString();
            using var activity = StartTrace("DeliverLP", flip.Auction.Uuid);

            if (flip.Auction.Context != null)
                flip.Auction.Context["csh"] = (DateTime.UtcNow - flip.Auction.FindTime).ToString();
            var isPreApi = flip.Auction.Context?.ContainsKey("pre-api") ?? true;
            if (isPreApi)
                await RunPreApiHandler(flip);
            else
                ObserveApiArrival(flip);
            if (flip.Auction.Context != null)
                flip.Auction.Context["csh"] = (DateTime.UtcNow - flip.Auction.FindTime).ToString();

            var uid = flip.UId;
            InCascade[uid] = DateTime.UtcNow;
            var releasedAt = await ReleaseTo(AccountTier.PREMIUM_PLUS, SuperSubs, subs => AddLowPriced(flip, subs), flip.Auction.FindTime, flip);
            RunDetached(() => RunLowerCascade(flip, isPreApi, releasedAt), uid, "delivering low priced auction to lower tiers");
        }

        internal async Task RunLowerCascade(LowPricedAuction flip, bool isPreApi, DateTime releasedAt)
        {
            try
            {
                TierDelivery deliver = subs => AddLowPriced(flip, subs);
                var uid = flip.UId;
                var audited = MatchAudit?.TryClaim(uid) == true;
                var steps = new List<StepDecision>();
                releasedAt = await ReleaseToPaidTiers(flip, isPreApi, deliver, releasedAt, steps);

                steps.Add(new StepDecision(AccountTier.NONE, await Handoff.AwaitReleaseTo(uid, AccountTier.NONE, releasedAt)));
                RecordInHistory(flip);
                var forFree = SoldAuctions.ContainsKey(uid) ? CopyWithProp(flip, "sold", "y") : flip;
                await ReleaseTo(AccountTier.NONE, SlowSubs, subs => AddLowPriced(forFree, subs), flip.Auction.FindTime, flip);
                if (audited)
                    MatchAudit.Schedule(uid, isPreApi ? HandoffAudit.SourcePreApi : HandoffAudit.SourceApi, steps);
            }
            finally
            {
                InCascade.TryRemove(flip.UId, out _);
            }
        }

        private void RecordInHistory(LowPricedAuction flip)
        {
            if (flip.TargetPrice - flip.Auction.StartingBid < 250_000)
                return;
            var historyFlip = LowPriceToFlip(flip);
            historyFlip.Sold = SoldAuctions.ContainsKey(flip.UId);
            AddToHistory(historyFlip);
        }

        private async Task RunPreApiHandler(LowPricedAuction flip)
        {
            try
            {
                await PreApiLowPriceHandler(this, flip);
            }
            catch (System.Exception e)
            {
                dev.Logger.Instance.Error(e, "Error in PreApiLowPriceHandler");
                await Task.Delay(30000);
            }
        }

        private async Task<DateTime> ReleaseToPaidTiers(LowPricedAuction flip, bool isPreApi, TierDelivery deliver, DateTime releasedAt, List<StepDecision> steps)
        {
            var uid = flip.UId;
            var findTime = flip.Auction.FindTime;
            if (isPreApi)
                releasedAt = await WaitForApiVisibility(flip.Auction.Start, releasedAt);
            if (flip.Finder == LowPricedAuction.FinderType.Rust)
                releasedAt = await ReleaseTo(AccountTier.PREMIUM, Subs, deliver, findTime, flip);
            else
                releasedAt = await HandOffAndRelease(uid, AccountTier.PREMIUM, Subs, deliver, findTime, releasedAt, isPreApi ? TimeSpan.FromSeconds(1) : null, steps, flip);
            return await HandOffAndRelease(uid, AccountTier.STARTER_PREMIUM, StarterSubs, deliver, findTime, releasedAt, null, steps, flip);
        }

        private static async Task<DateTime> WaitForApiVisibility(DateTime auctionStart, DateTime releasedAt)
        {
            var visibleAt = auctionStart + TimeSpan.FromSeconds(20.5);
            var waitTime = visibleAt - DateTime.UtcNow;
            if (waitTime > TimeSpan.Zero)
                await Task.Delay(waitTime).ConfigureAwait(false);
            return releasedAt > visibleAt ? releasedAt : visibleAt;
        }

        private static Task AddLowPriced(LowPricedAuction flip, ConcurrentDictionary<long, FlipConWrapper> subscribers)
        {
            foreach (var item in subscribers)
            {
                item.Value.AddLowPriced(flip);
            }
            return Task.CompletedTask;
        }

        private static LowPricedAuction CopyWithProp(LowPricedAuction flip, string key, string value)
        {
            var props = flip.AdditionalProps == null ? new Dictionary<string, string>() : new Dictionary<string, string>(flip.AdditionalProps);
            props[key] = value;
            return new LowPricedAuction()
            {
                AdditionalProps = props,
                Auction = flip.Auction,
                DailyVolume = flip.DailyVolume,
                Finder = flip.Finder,
                TargetPrice = flip.TargetPrice
            };
        }

        private static void SetProp(LowPricedAuction flip, string key, string value)
        {
            flip.AdditionalProps ??= new Dictionary<string, string>();
            flip.AdditionalProps[key] = value;
        }


        private static async Task NotifyAll(FlipInstance flip, ConcurrentDictionary<long, FlipConWrapper> subscribers)
        {
            foreach (var item in subscribers.Keys)
            {
                try
                {
                    if (!subscribers.TryGetValue(item, out FlipConWrapper connection) || !await connection.SendFlip(flip))
                    {
                        connection?.Connection?.Log("unsubed because of " + JsonConvert.SerializeObject(flip), Microsoft.Extensions.Logging.LogLevel.Error);
                        Unsubscribe(subscribers, item);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Failed to send flip {e.Message} {e.StackTrace}");
                    Unsubscribe(subscribers, item);
                }
            }
        }

        private static void Unsubscribe(ConcurrentDictionary<long, FlipConWrapper> subscribers, long item)
        {
            if (subscribers.TryRemove(item, out FlipConWrapper value))
                value.Stop();
        }

        private static void Unsubscribe(ConcurrentDictionary<long, FlipConWrapper> subscribers, IFlipConnection connection)
        {
            if (!subscribers.TryGetValue(connection.Id, out var value)
                || !ReferenceEquals(value.Connection, connection))
                return;

            if (subscribers.TryRemove(new KeyValuePair<long, FlipConWrapper>(connection.Id, value)))
                value.Stop();
        }

        ConsumerConfig consumerConf = new ConsumerConfig
        {
            GroupId = System.Net.Dns.GetHostName(),
            AutoOffsetReset = AutoOffsetReset.Latest,
            PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky,
        };


        public Task ListentoUnavailableTopics()
        {
            Console.WriteLine("listening to unavailibily topics");
            string[] topics = new string[] {
                SimplerConfig.Config.Instance["TOPICS:MISSING_AUCTION"],
                SimplerConfig.Config.Instance["TOPICS:SOLD_AUCTION"],
                SimplerConfig.Config.Instance["TOPICS:AUCTION_ENDED"] };
            return ConsumeBatch<SaveAuction>(topics, AuctionSold);
        }

        public async Task ListenToNewFlips()
        {
            string[] topics = new string[] { ConsumeTopic };

            Console.WriteLine("starting to listen for new auctions via topic " + ConsumeTopic);

            await ConsumeBatch<FlipInstance>(topics, flip =>
            {
                if (flip.MedianPrice - flip.LastKnownCost < 50_000)
                    return;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await DeliverFlip(flip);
                    }
                    catch (Exception e)
                    {
                        dev.Logger.Instance.Error(e, "delivering flip");
                    }
                }, new CancellationTokenSource(TimeSpan.FromMinutes(1)).Token);
            });
            Console.WriteLine("ended listening");
        }



        public async Task ListenToLowPriced(CancellationToken token)
        {
            string[] topics = new string[] { LowPriceConsumeTopic };

            await Kafka.KafkaConsumer.ConsumeBatch<LowPricedAuction>(config, topics, flips =>
            {
                var time = (DateTime.UtcNow - flips.First().Auction.FindTime).TotalSeconds;
                runtroughTime.Observe(time);
                QueueLowPriced(flips.Where(flip => IsBinFlip(flip) || IsBidFlip(flip)).ToList());

                return Task.CompletedTask;
            }, token, consumerConf.GroupId + Random.Shared.Next(), 50, AutoOffsetReset.Latest).ConfigureAwait(false);

            static bool IsBidFlip(LowPricedAuction flip)
            {
                return flip.Auction.End.ToUniversalTime() < DateTime.UtcNow + TimeSpan.FromMinutes(6) && flip.Auction.End.ToUniversalTime() > DateTime.UtcNow;
            }

            static bool IsBinFlip(LowPricedAuction flip)
            {
                return flip.Auction.Start.ToUniversalTime() > DateTime.UtcNow.ToUniversalTime() - TimeSpan.FromMinutes(4)
                                    && flip.Auction.Bin;
            }
        }

        public async Task ConsumeNewAuctions(CancellationToken token)
        {
            string[] topics = new string[] { AuctionConsumeTopic };

            await Kafka.KafkaConsumer.ConsumeBatch<SaveAuction>(config, topics, auctions =>
            {
                if (FilterSumary.UserFinderEnabledCount == 0)
                    return Task.CompletedTask;

                QueueLowPriced(auctions.Where(flip => !(flip.Start.ToUniversalTime() < DateTime.UtcNow.ToUniversalTime() - TimeSpan.FromMinutes(4)
                    && flip.Bin || flip.End < DateTime.UtcNow)).Select(auction => new LowPricedAuction()
                    {
                        Auction = auction,
                        DailyVolume = 0,
                        Finder = LowPricedAuction.FinderType.USER,
                        TargetPrice = auction.StartingBid
                    }));
                auctionsConsumed.Inc(auctions.Count());
                return Task.CompletedTask;
            }, token, consumerConf.GroupId, 50, AutoOffsetReset.Latest).ConfigureAwait(false);
        }

        private void QueueLowPriced(IEnumerable<LowPricedAuction> flips)
        {
            var collection = flips.ToList();
            Task.Run(async () =>
            {
                try
                {
                    await DeliverLowPricedAuctions(collection).ConfigureAwait(false);
                    snipesReceived.Inc(collection.Count);
                }
                catch (Exception e)
                {
                    dev.Logger.Instance.Error(e, "delivering low priced auction");
                }
            }, new CancellationTokenSource(TimeSpan.FromMinutes(1)).Token).ConfigureAwait(false);
        }


        private async Task ConsumeBatch<T>(string[] topics, Action<T> work, int batchSize = 20)
        {
            await Kafka.KafkaConsumer.ConsumeBatch<T>(config, topics, x =>
            {
                foreach (var item in x)
                {
                    work(item);
                }
                return Task.CompletedTask;
            }, CancellationToken.None, consumerConf.GroupId, batchSize, AutoOffsetReset.Latest);
        }

        /// <summary>
        /// Removes old <see cref="SoldAuctions"/>
        /// </summary>
        private void ClearSoldBuffer()
        {
            if (SoldAuctions.Count < 500)
                return;
            var toRemove = new List<long>();
            var oldestTime = DateTime.UtcNow - TimeSpan.FromMinutes(10);
            foreach (var item in SoldAuctions)
            {
                if (item.Value < oldestTime)
                    toRemove.Add(item.Key);
            }
            foreach (var item in toRemove)
            {
                SoldAuctions.TryRemove(item, out DateTime deleted);
            }
        }

        public async Task<List<SaveAuction>> GetReferences(string uuid)
        {
            RestClient SkyFlipperHost = new RestClient(SimplerConfig.Config.Instance["SKYFLIPPER_BASE_URL"] ?? "http://" + (SimplerConfig.Config.Instance["SKYFLIPPER_HOST"]));
            var response = await SkyFlipperHost.ExecuteAsync(new RestRequest("flip/{uuid}/based").AddParameter("uuid", uuid, ParameterType.UrlSegment));
            var result = JsonConvert.DeserializeObject<List<SaveAuction>>(response.Content);
            return result;
        }

        public void UpdateFilterSumaries()
        {
            var minProfit = long.MaxValue;
            var sumary = new ServerFilterSumary();
            foreach (var item in Connections)
            {
                try
                {
                    var settings = item.Connection.Settings;
                    if (settings == null)
                        continue;
                    if (settings.AllowedFinders.HasFlag(LowPricedAuction.FinderType.USER))
                        sumary.UserFinderEnabledCount++;
                    if (settings.AllowedFinders.HasFlag(LowPricedAuction.FinderType.TFM))
                        sumary.TfmFinderEnabledCount++;
                    minProfit = Math.Min(minProfit, settings.MinProfit);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Error in UpdateFilterSumaries");
                }
            }
            sumary.LowestMinProfit = minProfit;
            tfmEnabled.Set(sumary.TfmFinderEnabledCount);

            // assign after calculation so that there is no racecondition while calculating
            this.FilterSumary = sumary;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            RunIsolatedForever(ListentoUnavailableTopics, "flip wait", stoppingToken);
            RunIsolatedForever(ListenToNewFlips, "flip wait", stoppingToken);
            RunIsolatedForever(ListenToLowPriced, "low priced auctions", stoppingToken);
            RunIsolatedForever(ConsumeNewAuctions, "consuming new auctions for user filter", stoppingToken);
            StartApiClockRefresh(stoppingToken);

            DiHandler.ServiceProvider.GetRequiredService<SettingsService>(); // warmup
            return Task.CompletedTask;
        }

        private void StartApiClockRefresh(CancellationToken stoppingToken)
        {
            if (string.IsNullOrEmpty(config?["UPDATER_BASE_URL"]))
            {
                logger?.LogInformation("UPDATER_BASE_URL not set, api update clock disabled");
                return;
            }
            RunIsolatedForever(RefreshApiClock, "api update clock", stoppingToken, 2 * 60_000);
        }

        private async Task RefreshApiClock()
        {
            try
            {
                ApiClock.Set(await new Coflnet.Sky.Commands.MC.NextUpdateRetriever().GetLastUpdate());
            }
            catch (Exception e)
            {
                logger?.LogError(e, "refreshing api update clock");
            }
        }

        private static TaskFactory factory = new TaskFactory();
        public static void RunIsolatedForever(Func<CancellationToken, Task> todo, string message, CancellationToken stoppingToken, int backoff = 2000)
        {
            RunIsolatedForever(async () => await todo(stoppingToken), message, stoppingToken, backoff);
        }

        public static void RunIsolatedForever(Func<Task> todo, string message, CancellationToken stoppingToken, int backoff = 2000)
        {
            factory.StartNew(async () =>
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await todo().ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine();
                        Console.WriteLine($"{message}: {e.Message} {e.StackTrace}\n {e.InnerException?.Message} {e.InnerException?.StackTrace} {e.InnerException?.InnerException?.Message} {e.InnerException?.InnerException?.StackTrace}");
                        await Task.Delay(2000).ConfigureAwait(false);
                    }
                    await Task.Delay(backoff, stoppingToken).ConfigureAwait(false);
                }
            }, TaskCreationOptions.LongRunning).ConfigureAwait(false);
        }

        public List<FlipConWrapper> Connections
        {
            get
            {
                var list = new List<FlipConWrapper>();
                list.AddRange(SuperSubs.Values);
                list.AddRange(Subs.Values);
                list.AddRange(StarterSubs.Values);
                list.AddRange(SlowSubs.Values);
                return list;
            }
        }
    }
    public class ServerFilterSumary
    {
        public int UserFinderEnabledCount;
        public int TfmFinderEnabledCount;
        public long LowestMinProfit;
    }
}
