using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky;
using Coflnet.Sky.Filter;
using Coflnet.Sky.Core;
using Microsoft.EntityFrameworkCore;
using Coflnet.Sky.Bazaar.Client.Api;
using Coflnet.Sky.Items.Client.Api;
using System.Threading;

namespace Coflnet.Sky.Commands.Shared
{
    public class PricesService
    {
        private HypixelContext context;
        private BazaarApi bazaarClient;
        private IItemsApi itemClient;
        private FilterEngine FilterEngine;
        private HashSet<string> bazaarItems;

        /// <summary>
        /// Creates a new 
        /// </summary>
        /// <param name="context"></param>
        /// <param name="bazaarClient"></param>
        /// <param name="itemClient"></param>
        /// <param name="filterEngine"></param>
        public PricesService(HypixelContext context, BazaarApi bazaarClient, IItemsApi itemClient, FilterEngine filterEngine)
        {
            this.context = context;
            this.bazaarClient = bazaarClient;
            this.itemClient = itemClient;
            FilterEngine = filterEngine;
        }

        public async Task<HashSet<string>> GetBazaarItems()
        {
            if (bazaarItems == null || Random.Shared.NextDouble() < 0.01)
            {
                var items = await itemClient.ItemsBazaarTagsGetAsync();
                if (items != null)
                {
                    bazaarItems = [.. items];
                }
            }
            return bazaarItems;
        }

        /// <summary>
        /// Get sumary of price
        /// </summary>
        /// <param name="itemTag"></param>
        /// <param name="filter"></param>
        /// <returns></returns>
        public async Task<PriceSumary> GetSumary(string itemTag, Dictionary<string, string> filter)
        {
            int id = GetItemId(itemTag);
            filter ??= [];

            var days = 2;
            var bazaarItems = await GetBazaarItems();
            if (bazaarItems?.Contains(itemTag) ?? false)
            {
                var val = await bazaarClient.GetHistoryGraphAsync(itemTag, DateTime.UtcNow - TimeSpan.FromDays(days), DateTime.UtcNow);
                if (val == null)
                    return null;
                if (val.Count() == 0)
                    val = await bazaarClient.GetHistoryGraphAsync(itemTag, DateTime.UtcNow - TimeSpan.FromDays(0.9), DateTime.UtcNow);
                if (val.Count() == 0)
                    return new();
                return new PriceSumary()
                {
                    Max = (long)val.Max(p => p.MaxBuy),
                    Med = (long)val.Select(v => (v.Sell + (v.Buy == 0 ? v.Sell : v.Buy)) / 2).OrderByDescending(v => v).Skip(val.Count() / 2).FirstOrDefault(),
                    Min = (long)val.Min(p => p.MinSell),
                    Mean = (long)val.Average(p => p.Buy),
                    Mode = (long)val.GroupBy(p => p.Buy).OrderByDescending(p => p.Count()).FirstOrDefault().Key,
                    Volume = (long)val.Select(p => p.SellVolume + p.BuyVolume).First() / 7
                };
            }
            var minTime = DateTime.Now.Subtract(TimeSpan.FromDays(days));
            var mainSelect = context.Auctions.Where(a => a.ItemId == id && a.End < DateTime.Now && a.End > minTime && a.HighestBidAmount > 0);
            filter["ItemId"] = id.ToString();
            var auctions = (await FilterEngine.AddFilters(mainSelect, filter)
                            .Select(a => a.HighestBidAmount / a.Count).ToListAsync()).OrderByDescending(p => p).ToList();
            var mode = auctions.GroupBy(a => a).OrderByDescending(a => a.Count()).FirstOrDefault();
            return new PriceSumary()
            {
                Max = auctions.FirstOrDefault(),
                Med = auctions.Count > 0 ? auctions.Skip(auctions.Count() / 2).FirstOrDefault() : 0,
                Min = auctions.LastOrDefault(),
                Mean = auctions.Count > 0 ? auctions.Average() : 0,
                Mode = mode?.Key ?? 0,
                Volume = auctions.Count > 0 ? ((double)auctions.Count()) / days : 0
            };
        }

        public async Task<(long cost, string uuid, long slbin)> GetLowestBinData(string itemTag, Dictionary<string, string> filters = null)
        {
            var itemId = GetItemId(itemTag);
            var select = context.Auctions
                        .Where(auction => auction.ItemId == itemId && auction.Bin)
                        .Where(auction => auction.End > DateTime.Now)
                        .Where(auction => auction.HighestBidAmount == 0);
            if (filters != null && filters.Count > 0)
            {
                filters["ItemId"] = itemId.ToString();
                select = FilterEngine.AddFilters(select, filters);
            }

            var dbResult = await select
                .Select(item =>
                    new
                    {
                        item.Uuid,
                        item.StartingBid
                    })
                .OrderBy(a => a.StartingBid)
                .Take(2)
                .ToListAsync();

            if (dbResult.Count == 0)
                return (0, null, 0);
            if (dbResult.Count == 1)
                return (dbResult[0].StartingBid, dbResult[0].Uuid, 0);
            return (dbResult[0].StartingBid, dbResult[0].Uuid, dbResult[1].StartingBid);
        }

        public async Task<PriceSumary> GetSumaryCache(string itemTag, Dictionary<string, string> filters = null)
        {
            var filterString = Newtonsoft.Json.JsonConvert.SerializeObject(filters);
            var key = "psum" + itemTag + filterString;
            var sumary = await CacheService.Instance.GetFromRedis<PriceSumary>(key);
            if (sumary == default)
            {
                if (filters == null)
                    filters = new Dictionary<string, string>();
                sumary = await GetSumary(itemTag, filters);
                await CacheService.Instance.SaveInRedis(key, sumary, TimeSpan.FromHours(2));
            }
            return sumary;
        }

        private static int GetItemId(string itemTag, bool forceget = true)
        {
            return DiHandler.GetService<ItemDetails>().GetItemIdForTag(itemTag, forceget);
        }

        public class PriceStatistics
        {
            public long AverageSellTimeSeconds { get; set; }
            public int TotalAuctionsSold { get; set; }
            public int TotalListed { get; set; }
            public int TotalSellers { get; set; }
            public int TotalBuyers { get; set; }
            public int TotalBids { get; set; }
            public long TotalCoinsTransferred { get; set; }
            public int TotalAuctions { get; set; }
            public int TotalItemsSold { get; set; }
            public int BinCount { get; set; }
            public List<AveragePrice> Prices { get; set; } = new List<AveragePrice>();
            public List<ItemPrices.AuctionPreview> RecentSamples { get; set; } = new();
        }

        public async Task<PriceStatistics> GetDetailedHistory(string itemTag, DateTime start, DateTime end, Dictionary<string, string> filters)
        {
            var itemId = GetItemId(itemTag);
            // Build a filtered base query (we'll apply time-slicing below)
            var baseSelect = context.Auctions
                        .Where(auction => auction.ItemId == itemId);
            if (filters != null && filters.Count > 0)
            {
                filters["ItemId"] = itemId.ToString();
                baseSelect = FilterEngine.AddFilters(baseSelect, filters);
            }
            // Increase timeout for batched fetches and limit overall rows to avoid long-running MySQL statements
            var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(300)).Token;

            // We'll page through the results in batches to avoid hitting max_statement_time and memory spikes.
            const int maxTotalRows = 400_000;
            const int batchSize = 50_000;

            // Aggregation container keyed by (Date, Hour)
            var aggregates = new Dictionary<(DateTime Date, int Hour), (
                double SumPrices, int AuctionCount, long SumItemCount, int AuctionsSold,
                double SellTimeSum, HashSet<string> Sellers, int BinCount, HashSet<int> Bidders,
                double MaxPrice, double MinPrice, long TotalCoinsTransferred)>(
                );

            // Keep a bounded list of recent samples (we'll take top 60 by End at the end)
            var recentSamplesBuffer = new List<ItemPrices.AuctionPreview>();

            var fetched = 0;

            // Split overall time range into at most 60-day slices to avoid long-running queries
            var maxSlice = TimeSpan.FromDays(60);
            var sliceStart = start;
            while (sliceStart < end && fetched < maxTotalRows)
            {
                var sliceEnd = sliceStart + maxSlice;
                if (sliceEnd > end) sliceEnd = end;

                // For this slice, build the time-limited query
                var sliceSelect = baseSelect.Where(a => a.End > sliceStart && a.End <= sliceEnd);
                var orderedQuery = sliceSelect.OrderBy(a => a.End).ThenBy(a => a.Uuid);

                var sliceOffset = 0;
                while (fetched < maxTotalRows)
                {
                    var toTake = Math.Min(batchSize, maxTotalRows - fetched);
                    var batch = await orderedQuery.Skip(sliceOffset).Take(toTake)
                        .Select(item => new
                        {
                            item.End,
                            item.Start,
                            item.Bin,
                            item.HighestBidAmount,
                            item.Count,
                            SellerId = item.AuctioneerId,
                            item.Uuid,
                            Bidders = item.Bids.OrderByDescending(b => b.Amount).Select(b => b.BidderId).ToList(),
                        }).AsNoTracking().ToListAsync(timeout);

                    if (batch == null || batch.Count == 0)
                        break;

                    foreach (var s in batch)
                    {
                        var key = (s.End.Date, s.End.Hour);
                        var pricePerUnit = s.Count != 0 ? (double)s.HighestBidAmount / s.Count : 0.0;

                        if (!aggregates.TryGetValue(key, out var ag))
                        {
                            ag = (0.0, 0, 0L, 0, 0.0, new HashSet<string>(), 0, new HashSet<int>(), double.MinValue, double.MaxValue, 0L);
                        }

                        ag.SumPrices += pricePerUnit;
                        ag.AuctionCount += 1;
                        ag.SumItemCount += s.Count;
                        if (s.HighestBidAmount > 0)
                            ag.AuctionsSold += 1;
                        if ((s.End - s.Start).TotalDays < 15)
                            ag.SellTimeSum += (s.End - s.Start).TotalSeconds;
                        else 
                            ag.SellTimeSum += 24 * 3600; // fill in 1 day placeholder for items that are unknown, to avoid skewing the average
                        if (s.SellerId != null) ag.Sellers.Add(s.SellerId);
                        if (s.Bin) ag.BinCount += 1;
                        if (s.Bidders != null)
                        {
                            foreach (var b in s.Bidders)
                                ag.Bidders.Add(b);
                        }
                        if (pricePerUnit > ag.MaxPrice) ag.MaxPrice = pricePerUnit;
                        if (pricePerUnit < ag.MinPrice) ag.MinPrice = pricePerUnit;
                        ag.TotalCoinsTransferred += s.HighestBidAmount;

                        aggregates[key] = ag;

                        // recent samples buffer (we'll trim later)
                        recentSamplesBuffer.Add(new ItemPrices.AuctionPreview()
                        {
                            End = s.End,
                            Price = (long)pricePerUnit,
                            Seller = s.SellerId,
                            Uuid = s.Uuid
                        });
                    }

                    fetched += batch.Count;
                    sliceOffset += batch.Count;
                    if (batch.Count < toTake)
                        break; // no more rows in this slice
                }

                // move to next slice
                sliceStart = sliceEnd;
            }

            // Build grouped result from aggregates
            var groupedResult = aggregates.Select(kvp => new
            {
                End = new { Date = kvp.Key.Date, Hour = kvp.Key.Hour },
                Avg = kvp.Value.AuctionCount > 0 ? kvp.Value.SumPrices / kvp.Value.AuctionCount : 0.0,
                Max = kvp.Value.MaxPrice == double.MinValue ? 0.0 : kvp.Value.MaxPrice,
                Min = kvp.Value.MinPrice == double.MaxValue ? 0.0 : kvp.Value.MinPrice,
                Count = kvp.Value.SumItemCount,
                AuctionCount = kvp.Value.AuctionCount,
                AuctionsSold = kvp.Value.AuctionsSold,
                SellTimeSum = kvp.Value.SellTimeSum,
                Sellers = kvp.Value.Sellers.ToList(),
                BinCount = kvp.Value.BinCount,
                Bids = kvp.Value.Bidders.ToList(),
                TotalCoinsTransferred = kvp.Value.TotalCoinsTransferred
            }).ToList();

            // Get recent samples (top 60 by End)
            var recentSamples = recentSamplesBuffer.OrderByDescending(a => a.End).Take(60).ToList();
            var averageSellTimeSeconds = groupedResult
                .Where(a => a != null && a.AuctionCount > 0)
                .Select(a => a.SellTimeSum / (double)a.AuctionCount)
                .DefaultIfEmpty(0.0)
                .Average();

            return new PriceStatistics()
            {
                TotalAuctions = groupedResult.Sum(a => a.AuctionCount),
                TotalCoinsTransferred = groupedResult.Sum(a => a.TotalCoinsTransferred),
                TotalBuyers = groupedResult.SelectMany(a => a.Bids).Distinct().Count(),
                TotalAuctionsSold = groupedResult.Sum(a => a.AuctionsSold),
                TotalItemsSold = (int)groupedResult.Sum(a => a.Count),
                TotalSellers = groupedResult.SelectMany(a => a.Sellers).Distinct().Count(),
                TotalBids = groupedResult.Sum(a => a.Bids.Count()),
                BinCount = groupedResult.Sum(a => a.BinCount),
                AverageSellTimeSeconds = (long)averageSellTimeSeconds,
                TotalListed = (int)groupedResult.Sum(a => a.Count),
                Prices = groupedResult.Select(i => new AveragePrice()
                {
                    Volume = (int)i.Count,
                    Avg = (float)i.Avg,
                    Max = (float)i.Max,
                    Min = (float)i.Min,
                    Date = i.End.Date.Add(TimeSpan.FromHours(i.End.Hour)),
                    ItemId = itemId
                }).ToList(),
                RecentSamples = recentSamples
            };
        }


        public class AdvancedAnalysisResult
        {
            public List<VolumeBucket> VolumeBuckets { get; set; } = new();
            public List<SellSpeedBucket> SellSpeedBuckets { get; set; } = new();
            public int TotalSales { get; set; }
            public double AvgSellTimeSeconds { get; set; }
            public double MedianSellTimeSeconds { get; set; }
            public double AvgPrice { get; set; }
            public double MedianPrice { get; set; }
            public long MinPrice { get; set; }
            public long MaxPrice { get; set; }
            public double BinPercentage { get; set; }
            public double SalesPerDay { get; set; }
            public List<HourlyStat> HourlyBreakdown { get; set; } = new();
            public double PriceStdDev { get; set; }
            public double PriceCoeffVariation { get; set; }
            public List<SellerShare> TopSellers { get; set; } = new();
        }

        public class VolumeBucket
        {
            public long MinPrice { get; set; }
            public long MaxPrice { get; set; }
            public long AvgPrice { get; set; }
            public int Count { get; set; }
        }

        public class SellSpeedBucket
        {
            public long MinPrice { get; set; }
            public long MaxPrice { get; set; }
            public long AvgPrice { get; set; }
            public double AvgSellTimeSeconds { get; set; }
            public string SpeedCategory { get; set; }
            public int SampleCount { get; set; }
        }

        public class HourlyStat
        {
            public int Hour { get; set; }
            public int Count { get; set; }
            public double AvgPrice { get; set; }
            public double AvgSellTimeSeconds { get; set; }
        }

        public class SellerShare
        {
            public string Seller { get; set; }
            public int Count { get; set; }
            public double Percentage { get; set; }
        }

        /// <summary>
        /// A single sold-auction data point used as input for <see cref="ComputeAdvancedAnalysis"/>.
        /// </summary>
        public readonly record struct AnalysisSample(long PricePerUnit, DateTime Start, DateTime End, bool IsBin, int SellerId);

        /// <summary>
        /// A single active-listing data point used as input for <see cref="ComputeLiveMarketAnalysis"/>.
        /// </summary>
        public readonly record struct LiveListingSample(long StartingBid, long HighestBidAmount, int Count, DateTime Start, bool Bin, int SellerId);

        /// <summary>
        /// Computes advanced analysis data: volume clustering by price and sell speed by price bucket.
        /// Fetches sold auctions in a single query and delegates the actual number crunching to
        /// <see cref="ComputeAdvancedAnalysis"/> so it stays independently testable.
        /// </summary>
        public async Task<AdvancedAnalysisResult> GetAdvancedAnalysis(string itemTag, DateTime start, DateTime end, Dictionary<string, string> filters)
        {
            var itemId = GetItemId(itemTag);
            var baseSelect = context.Auctions
                .Where(a => a.ItemId == itemId && a.End > start && a.End <= end && a.HighestBidAmount > 0);

            if (filters != null && filters.Count > 0)
            {
                filters["ItemId"] = itemId.ToString();
                baseSelect = FilterEngine.AddFilters(baseSelect, filters);
            }

            var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token;

            // Single query: fetch price + time data (limit to 50k for performance)
            // Note: Fetch Start/End separately because (End-Start).TotalSeconds can't be translated to SQL
            // Note: Fetch HighestBidAmount/Count separately (instead of dividing in SQL) so a Count of 0
            // can't cause a division by zero while materializing the row; min/max price are derived from
            // these same rows below instead of a separate aggregate query.
            var rawData = await baseSelect
                .OrderByDescending(a => a.End)
                .Take(50_000)
                .Select(a => new
                {
                    a.HighestBidAmount,
                    a.Count,
                    a.Start,
                    a.End,
                    IsBin = a.Bin,
                    a.SellerId
                })
                .AsNoTracking()
                .ToListAsync(timeout);

            if (rawData.Count == 0)
                return new AdvancedAnalysisResult();

            var samples = rawData.Select(d => new AnalysisSample(
                d.Count != 0 ? d.HighestBidAmount / d.Count : 0,
                d.Start,
                d.End,
                d.IsBin,
                d.SellerId
            )).ToList();

            return ComputeAdvancedAnalysis(samples, start, end);
        }

        /// <summary>
        /// Pure computation of <see cref="AdvancedAnalysisResult"/> from already fetched sold-auction samples.
        /// Contains no DB access so it can be unit tested directly.
        /// </summary>
        public static AdvancedAnalysisResult ComputeAdvancedAnalysis(IReadOnlyList<AnalysisSample> samples, DateTime start, DateTime end)
        {
            if (samples == null || samples.Count == 0)
                return new AdvancedAnalysisResult();

            // Bucket in C# (15 buckets for volume, 10 for sell speed)
            const int numVolumeBuckets = 15;
            const int numSpeedBuckets = 10;
            var minPrice = samples[0].PricePerUnit;
            var maxPrice = samples[0].PricePerUnit;
            foreach (var s in samples)
            {
                if (s.PricePerUnit < minPrice) minPrice = s.PricePerUnit;
                if (s.PricePerUnit > maxPrice) maxPrice = s.PricePerUnit;
            }
            var priceRange = maxPrice - minPrice;

            if (priceRange <= 0)
                priceRange = 1;

            var volumeBucketWidth = (double)priceRange / numVolumeBuckets;
            var speedBucketWidth = (double)priceRange / numSpeedBuckets;

            var volumeBuckets = new int[numVolumeBuckets];
            var volumeBucketSums = new long[numVolumeBuckets];
            var speedBucketSellTime = new double[numSpeedBuckets];
            var speedBucketCounts = new int[numSpeedBuckets];
            var speedBucketSums = new long[numSpeedBuckets];
            double totalSellTime = 0;
            double totalPrice = 0;
            int binCount = 0;
            var allPrices = new List<long>(samples.Count);
            var allSellTimes = new List<double>(samples.Count);

            // Hourly breakdown (24 hours)
            var hourlyCounts = new int[24];
            var hourlyPriceSums = new double[24];
            var hourlySellTimeSums = new double[24];

            // Seller tracking
            var sellerCounts = new Dictionary<int, int>();

            foreach (var d in samples)
            {
                var sellTimeSeconds = Math.Max(0, (d.End - d.Start).TotalSeconds);
                sellTimeSeconds = Math.Min(sellTimeSeconds, 7 * 86400); // cap at 7 days

                // Volume bucket
                var vIdx = (int)((d.PricePerUnit - minPrice) / volumeBucketWidth);
                if (vIdx >= numVolumeBuckets) vIdx = numVolumeBuckets - 1;
                if (vIdx < 0) vIdx = 0;
                volumeBuckets[vIdx]++;
                volumeBucketSums[vIdx] += d.PricePerUnit;

                // Speed bucket
                var sIdx = (int)((d.PricePerUnit - minPrice) / speedBucketWidth);
                if (sIdx >= numSpeedBuckets) sIdx = numSpeedBuckets - 1;
                if (sIdx < 0) sIdx = 0;
                speedBucketSellTime[sIdx] += sellTimeSeconds;
                speedBucketCounts[sIdx]++;
                speedBucketSums[sIdx] += d.PricePerUnit;

                totalSellTime += sellTimeSeconds;
                totalPrice += d.PricePerUnit;
                allPrices.Add(d.PricePerUnit);
                allSellTimes.Add(sellTimeSeconds);
                if (d.IsBin) binCount++;

                // Hourly
                var hour = d.End.Hour;
                hourlyCounts[hour]++;
                hourlyPriceSums[hour] += d.PricePerUnit;
                hourlySellTimeSums[hour] += sellTimeSeconds;

                // Seller
                if (d.SellerId != 0)
                {
                    if (!sellerCounts.TryGetValue(d.SellerId, out var cnt))
                        cnt = 0;
                    sellerCounts[d.SellerId] = cnt + 1;
                }
            }

            allPrices.Sort();
            allSellTimes.Sort();
            var medianPrice = allPrices.Count > 0 ? allPrices[allPrices.Count / 2] : 0;
            var medianSellTime = allSellTimes.Count > 0 ? allSellTimes[allSellTimes.Count / 2] : 3600;

            // Auto-adjust speed categories based on median sell time
            string CategoriseSellSpeed(double avgSeconds)
            {
                if (avgSeconds < medianSellTime * 0.5) return "FAST";
                if (avgSeconds < medianSellTime) return "MED";
                if (avgSeconds < medianSellTime * 2) return "SLOW";
                return "VERY_SLOW";
            }

            // Price volatility (standard deviation / coefficient of variation)
            var avgPriceVal = samples.Count > 0 ? totalPrice / samples.Count : 0;
            double sumSquaredDiffs = 0;
            foreach (var p in allPrices)
                sumSquaredDiffs += (p - avgPriceVal) * (p - avgPriceVal);
            var priceStdDev = samples.Count > 1 ? Math.Sqrt(sumSquaredDiffs / (samples.Count - 1)) : 0;
            var priceCV = avgPriceVal > 0 ? priceStdDev / avgPriceVal : 0;

            var result = new AdvancedAnalysisResult
            {
                TotalSales = samples.Count,
                AvgSellTimeSeconds = samples.Count > 0 ? totalSellTime / samples.Count : 0,
                MedianSellTimeSeconds = medianSellTime,
                AvgPrice = avgPriceVal,
                MedianPrice = medianPrice,
                MinPrice = minPrice,
                MaxPrice = maxPrice,
                BinPercentage = samples.Count > 0 ? (double)binCount / samples.Count * 100 : 0,
                SalesPerDay = samples.Count > 0 ? samples.Count / Math.Max(1, (end - start).TotalDays) : 0,
                VolumeBuckets = new List<VolumeBucket>(numVolumeBuckets),
                SellSpeedBuckets = new List<SellSpeedBucket>(numSpeedBuckets),
                PriceStdDev = priceStdDev,
                PriceCoeffVariation = priceCV
            };

            // Hourly breakdown
            for (int h = 0; h < 24; h++)
            {
                result.HourlyBreakdown.Add(new HourlyStat
                {
                    Hour = h,
                    Count = hourlyCounts[h],
                    AvgPrice = hourlyCounts[h] > 0 ? hourlyPriceSums[h] / hourlyCounts[h] : 0,
                    AvgSellTimeSeconds = hourlyCounts[h] > 0 ? hourlySellTimeSums[h] / hourlyCounts[h] : 0
                });
            }

            // Top sellers
            var topSellers = sellerCounts
                .OrderByDescending(kvp => kvp.Value)
                .Take(5)
                .Select(kvp => new SellerShare
                {
                    Seller = kvp.Key.ToString(),
                    Count = kvp.Value,
                    Percentage = samples.Count > 0 ? (double)kvp.Value / samples.Count * 100 : 0
                })
                .ToList();
            result.TopSellers = topSellers;

            for (int i = 0; i < numVolumeBuckets; i++)
            {
                if (volumeBuckets[i] == 0) continue;
                result.VolumeBuckets.Add(new VolumeBucket
                {
                    MinPrice = minPrice + (long)(i * volumeBucketWidth),
                    MaxPrice = minPrice + (long)((i + 1) * volumeBucketWidth),
                    AvgPrice = volumeBuckets[i] > 0 ? volumeBucketSums[i] / volumeBuckets[i] : 0,
                    Count = volumeBuckets[i]
                });
            }

            for (int i = 0; i < numSpeedBuckets; i++)
            {
                if (speedBucketCounts[i] == 0) continue;
                var avgSell = speedBucketSellTime[i] / speedBucketCounts[i];
                result.SellSpeedBuckets.Add(new SellSpeedBucket
                {
                    MinPrice = minPrice + (long)(i * speedBucketWidth),
                    MaxPrice = minPrice + (long)((i + 1) * speedBucketWidth),
                    AvgPrice = speedBucketSums[i] / speedBucketCounts[i],
                    AvgSellTimeSeconds = avgSell,
                    SpeedCategory = CategoriseSellSpeed(avgSell),
                    SampleCount = speedBucketCounts[i]
                });
            }

            return result;
        }

        public class LiveMarketAnalysisResult
        {
            public int BinCount { get; set; }
            public int AuctionCount { get; set; }
            public int SellerCount { get; set; }
            public long LowestBin { get; set; }
            public long MedianBin { get; set; }
            public long HighestBin { get; set; }
            public double AvgTimeOnMarketSeconds { get; set; }
            public double MedianTimeOnMarketSeconds { get; set; }
            public long BucketRangeMax { get; set; }
            public int AboveRangeCount { get; set; }
            public List<VolumeBucket> PriceBuckets { get; set; } = new();
            public List<SellSpeedBucket> TimeOnMarketBuckets { get; set; } = new();
        }

        /// <summary>
        /// Gets a live snapshot of the market for an item: BIN price distribution, time-on-market
        /// buckets and active listing/seller counts. Auction-house only (bazaar is not supported yet,
        /// items without a matching auction-house item id or without active listings return an empty result).
        /// </summary>
        public async Task<LiveMarketAnalysisResult> GetLiveMarketAnalysis(string itemTag, Dictionary<string, string> filters)
        {
            // forceget: false - bazaar-only (or otherwise unknown) tags have no auction-house item id;
            // treat that the same as "no active listings found" instead of throwing, like GetCurrentPrice does.
            var itemId = GetItemId(itemTag, false);
            if (itemId == 0)
                return new LiveMarketAnalysisResult();

            var now = DateTime.UtcNow;
            var baseSelect = context.Auctions
                .Where(a => a.ItemId == itemId && a.End > now && (!a.Bin || a.HighestBidAmount == 0));

            if (filters != null && filters.Count > 0)
            {
                filters["ItemId"] = itemId.ToString();
                baseSelect = FilterEngine.AddFilters(baseSelect, filters);
            }

            var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token;

            // Ordered so the 20k cap picks a deterministic subset; End is covered by the (ItemId, End) index
            var rawData = await baseSelect
                .OrderByDescending(a => a.End)
                .Take(20_000)
                .Select(a => new
                {
                    a.StartingBid,
                    a.HighestBidAmount,
                    a.Count,
                    a.Start,
                    a.Bin,
                    a.SellerId
                })
                .AsNoTracking()
                .ToListAsync(timeout);

            if (rawData.Count == 0)
                return new LiveMarketAnalysisResult();

            var samples = rawData.Select(d => new LiveListingSample(
                d.StartingBid, d.HighestBidAmount, d.Count, d.Start, d.Bin, d.SellerId
            )).ToList();

            return ComputeLiveMarketAnalysis(samples, now);
        }

        /// <summary>
        /// Pure computation of <see cref="LiveMarketAnalysisResult"/> from already fetched active-listing samples.
        /// Contains no DB access so it can be unit tested directly.
        /// </summary>
        public static LiveMarketAnalysisResult ComputeLiveMarketAnalysis(IReadOnlyList<LiveListingSample> samples, DateTime now)
        {
            var result = new LiveMarketAnalysisResult();
            if (samples == null || samples.Count == 0)
                return result;

            var sellerIds = new HashSet<int>();
            var binPrices = new List<long>();
            var binTimeOnMarket = new List<double>();
            int binCount = 0;
            int auctionCount = 0;

            foreach (var s in samples)
            {
                if (s.SellerId != 0)
                    sellerIds.Add(s.SellerId);

                if (s.Bin)
                {
                    binCount++;
                    var pricePerUnit = s.Count != 0 ? s.StartingBid / s.Count : 0;
                    binPrices.Add(pricePerUnit);
                    binTimeOnMarket.Add(Math.Max(0, (now - s.Start).TotalSeconds));
                }
                else
                {
                    auctionCount++;
                }
            }

            result.BinCount = binCount;
            result.AuctionCount = auctionCount;
            result.SellerCount = sellerIds.Count;

            if (binPrices.Count == 0)
                return result;

            var sortedPrices = binPrices.OrderBy(p => p).ToList();
            result.LowestBin = sortedPrices[0];
            result.HighestBin = sortedPrices[^1];
            result.MedianBin = sortedPrices[sortedPrices.Count / 2];

            var sortedTimes = binTimeOnMarket.OrderBy(t => t).ToList();
            result.AvgTimeOnMarketSeconds = sortedTimes.Average();
            result.MedianTimeOnMarketSeconds = sortedTimes[sortedTimes.Count / 2];

            // Live listings contain extreme overpriced outliers; buckets span [LowestBin, P95] unless
            // there are too few BIN listings to make a percentile meaningful.
            var rangeMin = result.LowestBin;
            var rangeMax = binCount >= 20 ? NearestRankPercentile(sortedPrices, 95) : result.HighestBin;
            result.BucketRangeMax = rangeMax;
            result.AboveRangeCount = binPrices.Count(p => p > rangeMax);

            var range = rangeMax - rangeMin;
            if (range <= 0)
                range = 1;

            const int numPriceBuckets = 15;
            const int numTimeBuckets = 8;
            var priceBucketWidth = (double)range / numPriceBuckets;
            var timeBucketWidth = (double)range / numTimeBuckets;

            var priceBucketCounts = new int[numPriceBuckets];
            var priceBucketSums = new long[numPriceBuckets];

            var timeBucketCounts = new int[numTimeBuckets];
            var timeBucketPriceSums = new long[numTimeBuckets];
            var timeBucketTimeSums = new double[numTimeBuckets];

            for (int i = 0; i < binPrices.Count; i++)
            {
                var price = binPrices[i];
                if (price > rangeMax) continue; // excluded outlier, already counted in AboveRangeCount

                var pIdx = (int)((price - rangeMin) / priceBucketWidth);
                if (pIdx >= numPriceBuckets) pIdx = numPriceBuckets - 1;
                if (pIdx < 0) pIdx = 0;
                priceBucketCounts[pIdx]++;
                priceBucketSums[pIdx] += price;

                var tIdx = (int)((price - rangeMin) / timeBucketWidth);
                if (tIdx >= numTimeBuckets) tIdx = numTimeBuckets - 1;
                if (tIdx < 0) tIdx = 0;
                timeBucketCounts[tIdx]++;
                timeBucketPriceSums[tIdx] += price;
                timeBucketTimeSums[tIdx] += binTimeOnMarket[i];
            }

            for (int i = 0; i < numPriceBuckets; i++)
            {
                if (priceBucketCounts[i] == 0) continue;
                result.PriceBuckets.Add(new VolumeBucket
                {
                    MinPrice = rangeMin + (long)(i * priceBucketWidth),
                    MaxPrice = rangeMin + (long)((i + 1) * priceBucketWidth),
                    AvgPrice = priceBucketSums[i] / priceBucketCounts[i],
                    Count = priceBucketCounts[i]
                });
            }

            for (int i = 0; i < numTimeBuckets; i++)
            {
                if (timeBucketCounts[i] == 0) continue;
                var avgTimeOnMarket = timeBucketTimeSums[i] / timeBucketCounts[i];
                result.TimeOnMarketBuckets.Add(new SellSpeedBucket
                {
                    MinPrice = rangeMin + (long)(i * timeBucketWidth),
                    MaxPrice = rangeMin + (long)((i + 1) * timeBucketWidth),
                    AvgPrice = timeBucketPriceSums[i] / timeBucketCounts[i],
                    AvgSellTimeSeconds = avgTimeOnMarket,
                    SpeedCategory = CategoriseTimeOnMarket(avgTimeOnMarket),
                    SampleCount = timeBucketCounts[i]
                });
            }

            return result;

            static string CategoriseTimeOnMarket(double seconds)
            {
                if (seconds < 3600) return "FRESH";
                if (seconds < 6 * 3600) return "AGING";
                return "STALE";
            }
        }

        /// <summary>
        /// Nearest-rank percentile of an already ascending-sorted list (1-based rank = ceil(percentile/100 * n)).
        /// </summary>
        private static long NearestRankPercentile(List<long> sortedAscending, double percentile)
        {
            if (sortedAscending == null || sortedAscending.Count == 0)
                return 0;
            var rank = (int)Math.Ceiling(percentile / 100.0 * sortedAscending.Count);
            if (rank < 1) rank = 1;
            if (rank > sortedAscending.Count) rank = sortedAscending.Count;
            return sortedAscending[rank - 1];
        }

        public async Task<IEnumerable<AveragePrice>> GetHistory(string itemTag, DateTime start, DateTime end, Dictionary<string, string> filters)
        {
            var itemId = GetItemId(itemTag);
            var select = context.Auctions
                        .Where(auction => auction.ItemId == itemId)
                        .Where(auction => auction.End > start && auction.End < end)
                        .Where(auction => auction.HighestBidAmount > 1);
            if (filters != null && filters.Count > 0)
            {
                filters["ItemId"] = itemId.ToString();
                select = FilterEngine.AddFilters(select, filters);
            }
            var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token;

            var groupedSelect = select.GroupBy(item => new { item.End.Date, Hour = 0 });
            if (end - start < TimeSpan.FromDays(7.001))
                groupedSelect = select.GroupBy(item => new { item.End.Date, item.End.Hour });

            var dbResult = await groupedSelect
                .Select(item =>
                    new
                    {
                        End = item.Key,
                        Avg = item.Average(a => a.HighestBidAmount / a.Count),
                        Max = item.Max(a => a.HighestBidAmount / a.Count),
                        Min = item.Min(a => a.HighestBidAmount / a.Count),
                        Count = item.Sum(a => a.Count)
                    }).AsNoTracking().ToListAsync(timeout);

            if (dbResult.Count == 0)
            {
                var result = await bazaarClient.GetHistoryGraphAsync(itemTag, start, end);
                return result.Select(i => new AveragePrice()
                {
                    Volume = (int)(i.BuyMovingWeek + i.SellMovingWeek) / 2,
                    Avg = (i.MaxBuy + i.MinSell) / 2,
                    Max = i.MaxBuy,
                    Min = i.MinSell,
                    Date = i.Timestamp,
                    ItemId = itemId
                });
            }

            return dbResult
                .Select(i => new AveragePrice()
                {
                    Volume = i.Count,
                    Avg = i.Avg,
                    Max = i.Max,
                    Min = i.Min,
                    Date = i.End.Date.Add(TimeSpan.FromHours(i.End.Hour)),
                    ItemId = itemId
                });
        }

        /// <summary>
        /// Gets the latest known buy and sell price for an item per type 
        /// </summary>
        /// <param name="itemTag">The itemTag to get prices for</param>
        /// <param name="count">For how many items the price should be retrieved</param>add 
        /// <returns></returns>
        public async Task<CurrentPrice> GetCurrentPrice(string itemTag, int count = 1)
        {
            int id = GetItemId(itemTag, false);
            if (id == 0)
                return new CurrentPrice() { Available = -1 };
            var bazaarItems = await GetBazaarItems();
            if (bazaarItems?.Contains(itemTag) ?? false)
            {
                var val = await bazaarClient.GetClosestToAsync(itemTag, DateTime.UtcNow);
                if (val == null)
                {
                    var all = await bazaarClient.GetAllPricesAsync();
                    return all.Where(a => a.ProductId == itemTag).Select(a => new CurrentPrice()
                    {
                        Buy = a.BuyPrice * count,
                        Sell = a.SellPrice * count,
                        Available = (int)(100000 / a.SellPrice + 10)
                    }).FirstOrDefault();
                }
                return new CurrentPrice()
                {
                    Buy = GetBazaarCostForCount(val.BuyOrders, count),
                    Sell = GetBazaarCostForCount(val.SellOrders, count),
                    Available = val.BuyOrders.Sum(b => b.Amount)
                };
            }
            else
            {
                var filter = new Dictionary<string, string>();
                var lowestBins = await context.Auctions
                        .Where(a => a.ItemId == id && a.End > DateTime.Now && a.HighestBidAmount == 0 && a.Bin)
                        .OrderBy(a => a.StartingBid)
                        .Take(count <= 1 ? 1 : count)
                        .AsNoTracking()
                        .ToListAsync();
                if (lowestBins == null || lowestBins.Count == 0)
                {
                    var sumary = await GetSumary(itemTag, filter);
                    return new CurrentPrice() { Buy = sumary.Med, Sell = sumary.Min, IsAh = true };
                }
                var foundcount = 0;
                var cost = count == 1 ? lowestBins.FirstOrDefault().StartingBid
                        : lowestBins.TakeWhile(a =>
                        {
                            foundcount += a.Count;
                            return foundcount <= count;
                        }).Sum(a => a.StartingBid);
                var sell = 0L;
                if (lowestBins.Count > 0)
                    sell = lowestBins.First().StartingBid / lowestBins.First().Count * count;
                return new CurrentPrice() { Buy = cost, Sell = sell * 0.98, Available = lowestBins.Count, IsAh = true};
            }
        }

        public double GetBazaarCostForCount(List<Bazaar.Client.Model.Order> orders, int count)
        {
            var totalCost = 0d;
            var alreadyAddedCount = 0;
            foreach (var sellOrder in orders)
            {
                var toTake = sellOrder.Amount + alreadyAddedCount > count ? count - alreadyAddedCount : sellOrder.Amount;
                totalCost += sellOrder.PricePerUnit * toTake;
                alreadyAddedCount += toTake;
                if (alreadyAddedCount >= count)
                    return totalCost;
            }

            return -1;
        }
    }
}
