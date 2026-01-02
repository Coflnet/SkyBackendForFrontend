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
        public ConcurrentQueue<FlipInstance> Flipps = new ConcurrentQueue<FlipInstance>();
        private ConcurrentQueue<FlipInstance> SlowFlips = new ConcurrentQueue<FlipInstance>();
        private ConcurrentQueue<LowPricedAuction> StarterFlips = new();
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
        static Prometheus.Counter snipesReceived = Prometheus.Metrics.CreateCounter("sky_commands_snipes_received", "How many snipes were received");
        static Prometheus.Counter auctionsConsumed = Prometheus.Metrics.CreateCounter("sky_commands_auctions_received", "How many auctions were consumed");
        static Prometheus.Gauge tfmEnabled = Prometheus.Metrics.CreateGauge("sky_commands_tfm_enabled", "How many users have TFM enabled");

        /// <summary>
        /// Special load burst queue that will send out 5 flips at load
        /// </summary>
        private Queue<FlipInstance> LoadBurst = new Queue<FlipInstance>();
        private ConcurrentDictionary<long, DateTime> SoldAuctions = new ConcurrentDictionary<long, DateTime>();
        IConfiguration config;
        private ILogger<FlipperService> logger;

        public FlipperService(IConfiguration config, ILogger<FlipperService> logger)
        {
            this.config = config;
            this.logger = logger;
        }

        public void AddConnectionPlus(IFlipConnection connection, bool sendHistory = true)
        {
            SubToTier(connection, sendHistory, SuperSubs);
            Subs.TryRemove(connection.Id, out _);
        }

        public void AddConnection(IFlipConnection connection, bool sendHistory = true)
        {
            SubToTier(connection, sendHistory, Subs);
        }

        private void SubToTier(IFlipConnection connection, bool sendHistory, ConcurrentDictionary<long, FlipConWrapper> targetType)
        {
            var con = new FlipConWrapper(connection);
            targetType.AddOrUpdate(con.Connection.Id, cid => con, (cid, oldMId) => { oldMId.Stop(); return con; });

            RemoveNonConnection(connection);
            var toSendFlips = Flipps.Reverse().Take(25);
            if (sendHistory)
                SendFlipHistory(connection, toSendFlips, 200);
            con.StartWorkers();
        }

        public void AddStarterConnection(IFlipConnection connection, bool sendHistory = true)
        {
            RemoveNonConnection(connection);
            var con = new FlipConWrapper(connection);
            StarterSubs.AddOrUpdate(connection.Id, cid => con, (cid, oldMId) => con);
            if (sendHistory)
                SendFlipHistory(connection, LoadBurst, 0);
            con.StartWorkers();
        }

        public void AddNonConnection(IFlipConnection connection, bool sendHistory = true)
        {
            var con = new FlipConWrapper(connection);
            SlowSubs.AddOrUpdate(connection.Id, cid => con, (cid, oldMId) => con);
            if (!sendHistory)
                return;
            SendFlipHistory(connection, LoadBurst, 0);
            if (Random.Shared.Next() % 30 == 0)
                logger.LogInformation("Added new con, now there are " + SlowSubs.Count);
            UpdateFilterSumaries();
        }

        private void RemoveNonConnection(IFlipConnection con)
        {
            Unsubscribe(SlowSubs, con.Id);
            UpdateFilterSumaries();
            ClearSoldBuffer();
        }

        public void RemoveConnection(IFlipConnection con)
        {
            Unsubscribe(Subs, con.Id);
            Unsubscribe(SuperSubs, con.Id);
            Unsubscribe(StarterSubs, con.Id);
            RemoveNonConnection(con);
        }


        private static void SendFlipHistory(IFlipConnection con, IEnumerable<FlipInstance> toSendFlips, int delay = 5000)
        {
            var end = new CancellationTokenSource(TimeSpan.FromMinutes(1)).Token;
            Task.Run(async () =>
            {
                try
                {
                    foreach (var item in toSendFlips.ToList())
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
            if (!FlipIdLookup.ContainsKey(auction.UId))
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
            var exactLowestTask = auctionsApi.ApiAuctionsTagItemTagActiveBinGetAsync(auction.Tag, filters); ;
            List<ItemPrices.AuctionPreview> lowestBin = await ItemPrices.GetLowestBin(auction.Tag, auction.Tier);
            var exactLowest = await exactLowestTask;
            if (exactLowest?.Count > 1)
                return exactLowest.Select(a => new ItemPrices.AuctionPreview() { Price = Math.Max(a.StartingBid, a.HighestBidAmount), Uuid = a.Uuid, End = a.End, Seller = a.AuctioneerId }).ToList();
            return lowestBin;
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
            await NotifyAll(flip, SuperSubs);
            await Task.Delay(1000);
            await NotifyAll(flip, Subs);
            activity.Log("sheduling for starter");
            PrepareSlow(flip);
        }

        private void PrepareSlow(FlipInstance flip)
        {
            if (FlipIdLookup.ContainsKey(flip.UId))
                return;
            SlowFlips.Enqueue(flip);
            Flipps.Enqueue(flip);
            FlipIdLookup[flip.UId] = true;
            if (Flipps.Count > 1500)
            {
                if (Flipps.TryDequeue(out FlipInstance result))
                {
                    FlipIdLookup.Remove(result.UId, out bool value);
                }
            }
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


        public async Task DeliverLowPricedAuction(LowPricedAuction flip, AccountTier minAccountTier = AccountTier.PREMIUM)
        {
            if (flip.Auction.Context != null)
                flip.Auction.Context["crec"] = (DateTime.UtcNow - flip.Auction.FindTime).ToString();
            var tracer = DiHandler.GetService<ActivitySource>();
            using var activity = tracer.StartActivity("DeliverLP")?.SetTag("uuid", flip.Auction.Uuid);
            var time = (DateTime.UtcNow - flip.Auction.FindTime).TotalSeconds;

            if (flip.Auction.Context != null)
                flip.Auction.Context["csh"] = (DateTime.UtcNow - flip.Auction.FindTime).ToString();
            if (flip.Auction.Context?.TryGetValue("pre-api", out var preApi) ?? true)
            {
                try
                {
                    await PreApiLowPriceHandler(this, flip);
                    minAccountTier = AccountTier.PREMIUM_PLUS;
                }
                catch (System.Exception e)
                {
                    dev.Logger.Instance.Error(e, "Error in PreApiLowPriceHandler");
                    await Task.Delay(30000);
                }
            }
            if (flip.Finder != LowPricedAuction.FinderType.FLIPPER_AND_SNIPERS)
                minAccountTier = AccountTier.PREMIUM_PLUS; // upgrade required tier for new finders
            if (flip.Auction.Context != null)
                flip.Auction.Context["csh"] = (DateTime.UtcNow - flip.Auction.FindTime).ToString();
            foreach (var item in SuperSubs)
            {
                item.Value.AddLowPriced(flip);
            }

            if (flip.Auction.Context?.TryGetValue("pre-api", out preApi) ?? true)
            {
                var waitTime = flip.Auction.Start - DateTime.UtcNow + TimeSpan.FromSeconds(20.5);
                if (waitTime > TimeSpan.Zero)
                    await Task.Delay(waitTime).ConfigureAwait(false);
            }
            if (flip.Finder != LowPricedAuction.FinderType.Rust)
            {
                if (minAccountTier >= AccountTier.PREMIUM_PLUS)
                    await Task.Delay(900).ConfigureAwait(false);
                await Task.Delay(100).ConfigureAwait(false); // the whole system got upgraded from prem+ money so this artificially slows down premium
            }
            foreach (var item in Subs)
            {
                item.Value.AddLowPriced(flip);
            }

            StarterFlips.Enqueue(flip);

            if (flip.TargetPrice - flip.Auction.StartingBid < 250_000) // send below 200k profit out to everyone
            {
                foreach (var item in SlowSubs)
                {
                    item.Value.AddLowPriced(flip);
                }
            }
            else
                PrepareSlow(LowPriceToFlip(flip));
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

        public async Task ProcessSlowQueue()
        {
            while (true)
            {
                if (SlowFlips.TryDequeue(out FlipInstance flip))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await SendSlowFlip(flip);
                        }
                        catch (Exception e)
                        {
                            logger.LogError(e, $"Failed to send slow flip");
                        }
                    }, new CancellationTokenSource(30_000).Token);
                }
                if (SlowFlips.Count > 800)
                    return; // to large queue, continue immediately
                await Task.Delay(DelayTimeFor(SlowFlips.Count)).ConfigureAwait(false);
            }
        }

        private async Task SendSlowFlip(FlipInstance flip)
        {
            var activitySource = DiHandler.GetService<ActivitySource>();
            using var activity = activitySource.StartActivity("SendSlowFlip")?.SetTag("uuid", flip.Auction.Uuid);
            if (SoldAuctions.ContainsKey(flip.UId))
                flip.Sold = true;
            await NotifyAll(flip, SlowSubs);
            if (flip.Uuid[0] == 'a')
                Console.Write("sf+" + SlowSubs.Count);
            LoadBurst.Enqueue(flip);
            if (LoadBurst.Count > 5)
                LoadBurst.Dequeue();
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

        public static int DelayTimeFor(int queueSize, double minutes = 0.66, int max = 10000)
        {
            return (int)Math.Min((TimeSpan.FromMinutes(minutes) / (Math.Max(queueSize, 1))).TotalMilliseconds, max);
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
            RunIsolatedForever(async () =>
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    if (!StarterFlips.TryDequeue(out LowPricedAuction flip))
                    {
                        await Task.Delay(TimeSpan.FromSeconds(13)).ConfigureAwait(false);
                        continue;
                    }
                    foreach (var item in StarterSubs.Keys)
                    {
                        if (!StarterSubs.TryGetValue(item, out FlipConWrapper con) || !con.AddLowPriced(flip) && con.Closed)
                        {
                            StarterSubs.TryRemove(item, out _);
                        }
                    }
                    await Task.Delay(DelayTimeFor(StarterFlips.Count, 0.2, 2000)).ConfigureAwait(false);
                }
            }, "starter premium flips", stoppingToken);

            RunIsolatedForever(ListentoUnavailableTopics, "flip wait", stoppingToken);
            RunIsolatedForever(ListenToNewFlips, "flip wait", stoppingToken);
            RunIsolatedForever(ListenToLowPriced, "low priced auctions", stoppingToken);
            RunIsolatedForever(ConsumeNewAuctions, "consuming new auctions for user filter", stoppingToken);

            RunIsolatedForever(ProcessSlowQueue, "slow queue processor", stoppingToken, 200);
            DiHandler.ServiceProvider.GetRequiredService<SettingsService>(); // warmup
            return Task.CompletedTask;
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
