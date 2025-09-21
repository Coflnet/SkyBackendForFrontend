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
            var select = context.Auctions
                        .Where(auction => auction.ItemId == itemId)
                        .Where(auction => auction.End > start && auction.End < end);
            if (filters != null && filters.Count > 0)
            {
                filters["ItemId"] = itemId.ToString();
                select = FilterEngine.AddFilters(select, filters);
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
            var orderedQuery = select.OrderBy(a => a.End).ThenBy(a => a.UId);

            while (fetched < maxTotalRows)
            {
                var toTake = Math.Min(batchSize, maxTotalRows - fetched);
                var batch = await orderedQuery.Skip(fetched).Take(toTake)
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
                    if (s.HighestBidAmount > 0) ag.AuctionsSold += 1;
                    ag.SellTimeSum += (s.End - s.Start).TotalSeconds;
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
                if (batch.Count < toTake)
                    break; // no more rows
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
            return new PriceStatistics()
            {
                TotalAuctions = groupedResult.Sum(a => a.AuctionCount),
                TotalCoinsTransferred = groupedResult.Sum(a => a.TotalCoinsTransferred),
                TotalBuyers = groupedResult.SelectMany(a => a.Sellers).Distinct().Count(),
                TotalAuctionsSold = groupedResult.Sum(a => a.AuctionsSold),
                TotalItemsSold = (int)groupedResult.Sum(a => a.Count),
                TotalSellers = groupedResult.SelectMany(a => a.Sellers).Distinct().Count(),
                TotalBids = groupedResult.Sum(a => a.Bids.Count()),
                BinCount = groupedResult.Sum(a => a.BinCount),
                AverageSellTimeSeconds = (long)groupedResult.Where(a => a.AuctionCount > 0).Average(a => a.SellTimeSum / a.AuctionCount),
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
                    Volume = (int)(i.BuyMovingWeek + i.SellMovingWeek)/2,
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
                    return new CurrentPrice() { Buy = sumary.Med, Sell = sumary.Min };
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
                return new CurrentPrice() { Buy = cost, Sell = sell * 0.98, Available = lowestBins.Count };
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
