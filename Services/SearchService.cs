using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using dev;
using Coflnet.Sky.Core;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Newtonsoft.Json;
using Coflnet.Sky.Api.Client.Api;

namespace Coflnet.Sky.Commands.Shared
{
    public class SearchService
    {
        const int targetAmount = 5;
        private const string VALID_MINECRAFT_NAME_CHARS = "abcdefghijklmnopqrstuvwxyz1234567890_";
        ConcurrentQueue<PopularSite> popularSite = new ConcurrentQueue<PopularSite>();
        private ItemDetails itemDetails;
        private NBT nBT;
        private Items.Client.Api.IItemsApi itemsApi;

        private int updateCount = 0;

        public SearchService(ItemDetails itemDetails, NBT nBT, Items.Client.Api.IItemsApi itemsApi)
        {
            this.itemDetails = itemDetails;
            this.nBT = nBT;
            this.itemsApi = itemsApi;
        }

        public async Task AddPopularSite(string type, string id)
        {
            string title = "";
            if (type == "player")
                title = await Shared.DiHandler.GetService<PlayerName.PlayerNameService>()
                    .GetName(id) + " auctions hypixel skyblock";
            else if (type == "item")
                title = ItemDetails.TagToName(id) + " price hypixel skyblock";
            var entry = new PopularSite(title, $"{type}/{id}");
            if (!popularSite.Contains(entry))
                popularSite.Enqueue(entry);
            if (popularSite.Count > 100)
                popularSite.TryDequeue(out PopularSite result);
        }

        public IEnumerable<PopularSite> GetPopularSites()
        {
            return popularSite;
        }

        public Task<Channel<SearchResultItem>> Search(string search, CancellationToken token)
        {
            if (search.Length > 48)
                return Task.FromResult(Channel.CreateBounded<SearchResultItem>(1));
            return CreateResponse(search, token);
        }


        private async Task Work()
        {
            using (var context = new HypixelContext())
            {
                if (updateCount % 11 == 9)
                    await AddOccurences(context);
                if (updateCount % 10000 == 9999)
                    ShrinkHits(context);
            }
            await SaveHits();
        }

        private async Task AddOccurences(HypixelContext context)
        {
            foreach (var itemId in itemDetails.TagLookup.Values)
            {
                var sample = await context.Auctions
                                .Where(a => a.ItemId == itemId)
                                .OrderByDescending(a => a.Id)
                                .Take(20)
                                .Select(a => a.ItemName)
                                .ToListAsync();

                sample = sample.Select(s => ItemReferences.RemoveReforgesAndLevel(s)).ToList();

                var names = context.AltItemNames.Where(n => n.DBItemId == itemId);
                foreach (var item in names)
                {
                    var occured = sample.Count(s => s == item.Name);
                    if (occured == 0)
                        continue;
                    item.OccuredTimes += occured;
                    context.Update(item);
                }
                await context.SaveChangesAsync();
            }
            await Task.Delay(TimeSpan.FromSeconds(1));
        }


        public async Task SaveHits()
        {
            using (var context = new HypixelContext())
            {
                //if (updateCount % 12 == 5)
                //    PartialUpdateCache(context);
                itemDetails.SaveHits(context);
                PlayerSearch.Instance.SaveHits(context);
                await context.SaveChangesAsync();
            }
            updateCount++;
        }

        private void ShrinkHits(HypixelContext context)
        {
            Console.WriteLine("shrinking hits !!");
            ShrinkHitsType(context, context.Players);
            ShrinkHitsType(context, context.Items);
        }

        private static void ShrinkHitsType(HypixelContext context, IEnumerable<IHitCount> source)
        {
            // heavy searched results are reduced in order to allow other results to overtake them
            var res = source.Where(p => p.HitCount > 4);
            foreach (var item in res)
            {
                item.HitCount = item.HitCount * 9 / 10; // - 1; players that were searched once will be prefered forever
                context.Update(item);
            }
        }

        internal void RunForEver()
        {
            Task.Run(async () =>
            {
                //PopulateCache();
                while (true)
                {
                    await Task.Delay(10000);
                    try
                    {
                        await Work();
                    }
                    catch (Exception e)
                    {
                        Logger.Instance.Error("Searchserive got an error " + e.Message + e.StackTrace);
                    }

                }
            }).ConfigureAwait(false);
        }


        private static int prefetchIndex = new Random().Next(1000);
        /*        private async Task PrefetchCache()
                {
                    var charCount = VALID_MINECRAFT_NAME_CHARS.Length;
                    var combinations = charCount * charCount + charCount;
                    var index = prefetchIndex++ % combinations;
                    var requestString = "";
                    if (index < charCount)
                    {
                        requestString = VALID_MINECRAFT_NAME_CHARS[index].ToString();
                    }
                    else
                    {
                        index = index - charCount;
                        requestString = VALID_MINECRAFT_NAME_CHARS[index / charCount].ToString() + VALID_MINECRAFT_NAME_CHARS[index % charCount];
                    }
                    await Server.ExecuteCommandWithCache<string, object>("fullSearch", requestString);
                }*/

        private static Regex RomanNumber = new Regex("^[IVX]+$");
        private async Task<Channel<SearchResultItem>> CreateResponse(string search, CancellationToken token)
        {
            var searchSpan = Activity.Current;

            //var singlePlayer = PlayerSearch.Instance.FindDirect(search);
            var itemTask = GetItems(ReplaceStart(search, "item"), 12);
            var playerName = ReplaceStart(search, "player").TrimStart(':').Trim();
            var playersTask = PlayerSearch.Instance.Search(playerName, targetAmount, false);

            var Results = Channel.CreateBounded<SearchResultItem>(50);
            var searchTasks = new ConfiguredTaskAwaitable[4];
            var searchWords = search.Split(' ');
            var isSpecialSearch = search.StartsWith("item") || search.StartsWith("player");

            searchTasks[0] = Task.Run(async () =>
            {
                var source = DiHandler.GetService<ActivitySource>();
                using var activity = source.StartActivity("itemSearch");
                activity?.SetTag("search", search);
                await FindItems(search, itemTask, Results, activity);
            }, token).ConfigureAwait(false);

            searchTasks[1] = Task.Run(async () =>
            {
                await FindPlayers(playersTask, Results);
            }, token).ConfigureAwait(false);

            searchTasks[2] = Task.Run(async () =>
            {
                if (isSpecialSearch)
                    return;
                await FindSimilarSearches(search, Results, searchWords);
            }, token).ConfigureAwait(false);
            searchTasks[3] = Task.Run(async () =>
            {
                await SearchForAuctions(search, Results, searchWords);

            }, token).ConfigureAwait(false);
            ComputeEnchantments(search, Results, searchWords);

            if (search.StartsWith("item") || search.Contains("pet"))
                await searchTasks[0];

            return Results;
            // return result.OrderBy(r => r.Name?.Length / 2 - r.HitCount - (r.Name?.ToLower() == search.ToLower() ? 10000000 : 0)).Take(targetAmount).ToList();
        }

        private async Task<IEnumerable<ItemDetails.ItemSearchResult>> GetItems(string term, int resultAmount)
        {
            var items = await itemsApi.ItemsSearchTermGetAsync(term, resultAmount);
            return items?.Select(i => new ItemDetails.ItemSearchResult()
            {
                Name = i.Text + (i.Flags.Value.HasFlag(Sky.Items.Client.Model.ItemFlags.BAZAAR) ? " - bazaar"
                        : i.Flags.Value.HasFlag(Sky.Items.Client.Model.ItemFlags.AUCTION) ? "" : " - not on ah"),
                Tag = i.Tag,
                Tier = (Coflnet.Sky.Core.Tier)i.Tier - 1,
                IconUrl = "https://sky.coflnet.com/static/icon/" + i.Tag,
                HitCount = i.Tag == "CAKE_SOUL" ? 2 : 30 // items higher base hit count

            }).ToList();
        }

        private async Task SearchForAuctions(string search, Channel<SearchResultItem> Results, string[] searchWords)
        {
            if (searchWords.Count() > 1)
                return;
            if (long.TryParse(search, out long uid))
            {
                if (uid < 1000 && uid > 0)
                    return; // probably not a uid
                Console.WriteLine("detected uid " + uid);
                using (var context = new HypixelContext())
                {
                    var adjustedLower = uid - 1000;
                    var adjustedUpper = uid + 1000; // allow js 56 bit integers
                    var auction = await context.Auctions.Where(a => a.UId > adjustedLower && a.UId < adjustedUpper).Include(a => a.NBTLookup).FirstOrDefaultAsync();
                    Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(auction));
                    try
                    {
                        AddAuctionAsResult(Results, auction);

                    }
                    catch (Exception e)
                    {
                        dev.Logger.Instance.Error(e, "adding auction for search " + search);
                    }
                }
            }
            else if (search.Length == 32)
            {
                var auction = await AuctionService.Instance.GetAuctionAsync(search, a => a.Include(a => a.NBTLookup));
                AddAuctionAsResult(Results, auction);
            }
            else if (search.Length == 12 || search.Length == 36)
            {
                var key = nBT.GetKeyId("uid");
                var val = NBT.UidToLong(search);
                using (var context = new HypixelContext())
                {
                    var auctions = await context.Auctions
                                .Where(a => a.NBTLookup.Where(l => l.KeyId == key && l.Value == val).Any())
                                .Include(a => a.NBTLookup)
                                .ToListAsync();
                    if (auctions.Count == 0)
                        return;
                    foreach (var auction in auctions.OrderByDescending(a => a.End).GroupBy(a => a.Tag).Select(a => a.First()))
                    {
                        AddAuctionAsResult(Results, auction);
                    }
                }
            }
        }

        private void AddAuctionAsResult(Channel<SearchResultItem> Results, SaveAuction auction)
        {
            if (auction == null)
                return;
            Results.Writer.TryWrite(new SearchResultItem
            {
                HitCount = 100_000, // account for "Enchantment" suffix
                Name = auction.ItemName + " (Auction)",
                Type = "auction",
                IconUrl = "https://sky.coflnet.com/static/icon/" + auction.Tag,
                Id = auction.Uuid
            });
            var key = nBT.GetKeyId("uid");
            var filter = new Dictionary<string, string>();
            var uid = auction.NBTLookup.Where(l => l.KeyId == key).FirstOrDefault();
            if (uid == null)
                return;
            filter["UId"] = uid.Value.ToString("X");
            AddFilterResult(Results, filter, auction.ItemName + " (Sells)", auction.Tag, 100_000);
        }

        private async Task FindItems(string search, Task<IEnumerable<ItemDetails.ItemSearchResult>> itemTask, Channel<SearchResultItem> Results, Activity itemSpan)
        {
            IEnumerable<ItemDetails.ItemSearchResult> items;
            try
            {
                items = await itemTask ?? throw new Exception("itemTask is null");
            }
            catch (Exception e)
            {
                dev.Logger.Instance.Error(e, "searching for items");
                items = await GetItems(search, 12);
            }

            itemSpan?.AddEvent(new ActivityEvent("received Items", DateTimeOffset.Now));
            foreach (var item in items.Select(item => new SearchResultItem(item)))
            {
                await Results.Writer.WriteAsync(item);
            }
            await Results.Writer.WriteAsync(new SearchResultItem() { Type = "internal", Id = "items" });
        }

        private static async Task FindPlayers(Task<IEnumerable<PlayerResult>> playersTask, Channel<SearchResultItem> Results)
        {
            var playerList = (await playersTask);
            foreach (var item in playerList.Select(player => new SearchResultItem(player)))
                await Results.Writer.WriteAsync(item);
            await Results.Writer.WriteAsync(new SearchResultItem() { Type = "internal", Id = "players" });
            if (playerList.Count() == 1)
                await IndexerClient.TriggerNameUpdate(playerList.First().UUid);
        }

        private static async Task FindSimilarSearches(string search, Channel<SearchResultItem> Results, string[] searchWords)
        {
            if (search.Length <= 8 || IsHex(search))
                return;
            await Task.Delay(20);
            var searchApi = DiHandler.ServiceProvider.GetRequiredService<ISearchApi>();
            foreach (var item in await searchApi.ApiSearchSearchValGetAsync(search.Substring(0, search.Length - 2)))
                await Results.Writer.WriteAsync(new(item));
            if (searchWords.Count() == 1 || String.IsNullOrWhiteSpace(searchWords.Last()))
                return;
            if (searchWords[1].Length < 2)
                return;
            foreach (var item in await searchApi.ApiSearchSearchValGetAsync(searchWords[1]))
            {
                var resultItem = new SearchResultItem(item);
                resultItem.HitCount -= 20; // no exact match
                await Results.Writer.WriteAsync(resultItem);
            }
        }

        private static bool IsHex(string search)
        {
            return search.Length >= 10 && long.TryParse(search.Substring(0, 10), System.Globalization.NumberStyles.HexNumber, null, out long res);
        }

        private static ConcurrentDictionary<string, Enchantment.EnchantmentType> Enchantments = new ConcurrentDictionary<string, Enchantment.EnchantmentType>();

        private static void ComputeEnchantments(string search, Channel<SearchResultItem> Results, string[] searchWords)
        {
            var lastSpace = search.LastIndexOf(' ');
            if (Enchantments.Count == 0)
            {
                foreach (var item in Enum.GetValues(typeof(Enchantment.EnchantmentType)).Cast<Enchantment.EnchantmentType>())
                {
                    var name = item.ToString().Replace('_', ' ');
                    if (item != Enchantment.EnchantmentType.ultimate_wise)
                        name = name.Replace("ultimate ", "");
                    var formattedName = System.Threading.Thread.CurrentThread.CurrentCulture.TextInfo.ToTitleCase(name.ToLower());
                    Enchantments[formattedName] = item;
                }
            }
            var matchingEnchants = Enchantments.Keys.Where(name => name.ToLower().StartsWith(lastSpace > 1 ? search.Substring(0, lastSpace) : search));
            foreach (var item in matchingEnchants)
            {
                int lvl = 0;
                if (searchWords.Length > 1)
                    if (!int.TryParse(searchWords.Last(), out lvl))
                    {
                        var possibleLvl = searchWords.Last().Trim().ToUpper();
                        Console.WriteLine(possibleLvl);
                        if (RomanNumber.IsMatch(possibleLvl))
                            lvl = Roman.From(possibleLvl);
                    }

                var filter = new Dictionary<string, string>();
                filter["Enchantment"] = Enchantments[item].ToString();
                filter["EnchantLvl"] = "1";

                var resultText = item + " Enchantment";
                if (lvl != 0)
                {
                    resultText = item + $" {lvl} Enchantment";
                    filter["EnchantLvl"] = lvl.ToString();
                }

                AddFilterResult(Results, filter, resultText, "ENCHANTED_BOOK");
            }
        }

        private static void AddFilterResult(Channel<SearchResultItem> Results, Dictionary<string, string> filter, string resultText, string itemTag, int hitCount = 10)
        {
            Console.WriteLine($"Adding search {resultText} {hitCount}");
            Results.Writer.TryWrite(new SearchResultItem
            {
                HitCount = hitCount, // account for "Enchantment" suffix
                Name = resultText,
                Type = "filter",
                IconUrl = "https://sky.coflnet.com/static/icon/" + itemTag,
                Id = itemTag + "?itemFilter=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(JSON.Stringify(filter)))
            });
        }

        public List<SearchResultItem> RankSearchResults(string search, IEnumerable<SearchResultItem> result)
        {
            // remove special search indicators
            search = ReplaceStart(search, "player");
            search = ReplaceStart(search, "item");
            var orderedResult = result.Where(r => r.Name != null)
                            .Select(r =>
                            {
                                var lower = r.Name.ToLower().Replace("- bazaar", "");
                                return new
                                {
                                    rating = String.IsNullOrEmpty(r.Name) ? 0 :
                                lower.Length / 2
                                - r.HitCount * 5
                                - (lower == search ? r.Type == "item" ? 10000 : 800 : 0) // is exact match
                                - (lower.Length > search.Length && lower.Truncate(search.Length) == search ? 100 : 0) // matches search
                                - (Fastenshtein.Levenshtein.Distance(lower, search) <= 1 ? 40 : 0) // just one mutation off maybe a typo
                                + Fastenshtein.Levenshtein.Distance(lower.PadRight(search.Length), search) / 2 // distance to search
                                + Fastenshtein.Levenshtein.Distance(lower.Truncate(search.Length), search)
                                - (r.Type == "item" ? (search.Length < 5 ? 300 : 50) : 0),
                                    r
                                };
                            })
                            .OrderBy(r => r.rating)
                        .ToList()
                        .Select(r =>
                        {
                            Console.WriteLine(r.r.Name + " " + r.rating);
                            return r.r;
                        })
                        .Distinct(new SearchService.SearchResultComparer())
                        .Take(10)
                        .ToList();
            return orderedResult;
        }

        private static string ReplaceStart(string lower, string start)
        {
            if (lower.StartsWith(start))
                lower = lower.Replace(start + ":", "", true, CultureInfo.InvariantCulture).Replace(start + " ", "", true, CultureInfo.InvariantCulture);
            return lower;
        }

        public class SearchResultComparer : IEqualityComparer<SearchResultItem>
        {
            public bool Equals([AllowNull] SearchResultItem x, [AllowNull] SearchResultItem y)
            {
                return x != null && y != null && x.Equals(y);
            }

            public int GetHashCode([DisallowNull] SearchResultItem obj)
            {
                return obj.GetHashCode();
            }
        }

        public static string PlayerHeadUrl(string playerUuid)
        {
            return "https://sky.coflnet.com/avatars/" + playerUuid;
        }
    }
}