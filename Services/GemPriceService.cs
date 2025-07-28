using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using RestSharp;

namespace Coflnet.Sky.Commands.Shared
{
    public class GemPriceService : BackgroundService
    {
        public ConcurrentDictionary<string, int> Prices = new();
        public ConcurrentDictionary<(short, long), string> GemNames = new();
        public ConcurrentDictionary<(short, long), Dictionary<(short, long), string>> UniversalGemNames = new();
        private RestSharp.RestClient commandsClient;
        private IServiceScopeFactory scopeFactory;
        private ILogger<GemPriceService> logger;
        private IConfiguration configuration;

        public GemPriceService(IConfiguration config, IServiceScopeFactory scopeFactory, ILogger<GemPriceService> logger, IConfiguration configuration)
        {
            this.commandsClient = new RestSharp.RestClient(config["API_BASE_URL"]);
            this.scopeFactory = scopeFactory;
            this.logger = logger;
            this.configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (configuration["DBCONNECTION"] == null)
            {
                logger.LogWarning("No DBCONNECTION found in configuration, aborting background task. This is okay if this service doesn't need gemstone prices");
                return;
            }
            var rarities = new string[] { "PERFECT", "FLAWLESS" };
            var types = new string[] { "RUBY", "JASPER", "JADE", "TOPAZ", "AMETHYST", "AMBER", "SAPPHIRE", "OPAL", "AQUAMARINE", "CITRINE", "ONYX", "PERIDOT" };
            await LoadNameLookups(rarities, types);

            while (!stoppingToken.IsCancellationRequested)
            {
                foreach (var perfection in rarities)
                {
                    foreach (var type in types)
                    {
                        var itemId = $"{perfection}_{type}_GEM";
                        var route = $"/api/item/price/{itemId}/current";
                        try
                        {
                            var result = await commandsClient.ExecuteGetAsync(new RestRequest(route));
                            if (result.StatusCode != System.Net.HttpStatusCode.OK)
                                throw new Exception("Response has the status " + result.StatusCode);
                            var profit = JsonConvert.DeserializeObject<CurrentPrice>(result.Content).Sell;
                            if (perfection == "PERFECT")
                                profit -= 500_000;
                            else
                                profit -= 100_000;
                            Prices[itemId] = (int)profit;
                        }
                        catch (Exception e)
                        {
                            dev.Logger.Instance.Error(e, "retrieving gem price at " + route);
                        }
                    }
                }
                logger.LogInformation("Loaded gemstone prices");
                await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
            }
        }

        private async Task LoadNameLookups(string[] rarities, string[] types)
        {
            using var scope = scopeFactory.CreateScope();
            using var db = scope.ServiceProvider.GetRequiredService<HypixelContext>();
            var StringKeys = new List<string>();
            foreach (var item in types)
            {
                for (int i = 0; i < 4; i++)
                {
                    StringKeys.Add($"{item}_{i}");
                }
            }
            var Keys = await db.NBTKeys.Where(n => StringKeys.Contains(n.Slug)).ToListAsync();
            foreach (var key in Keys)
            {
                foreach (var rarity in rarities)
                {
                    var value = await db.NBTValues.Where(n => n.KeyId == key.Id && n.Value == rarity).FirstOrDefaultAsync();
                    if (value == default)
                        continue;
                    GemNames[(key.Id, value.Id)] = $"{rarity}_{key.Slug.Split("_").First()}_GEM";
                }
            }
            await GetUniversal(db);
        }

        private async Task GetUniversal(HypixelContext db)
        {
            var universalKeys = new List<string>();
            var universal = new string[] { "COMBAT", "DEFENSIVE", "UNIVERSAL", "MINING", "OFFENSIVE" }.SelectMany(b => new string[] { "0", "1" }.Select(lvl => $"{b}_{lvl}")).ToList();
            var typeKeys = universal.Select(s => s + "_gem").ToList();
            universalKeys.AddRange(typeKeys);
            universalKeys.AddRange(universal);
            var nbtElems = await db.NBTKeys.Where(n => universalKeys.Contains(n.Slug)).ToListAsync();
            var numericKeys = nbtElems.Select(n => n.Id).ToList();
            var values = (await db.NBTValues.Where(n => numericKeys.Contains(n.KeyId)).ToListAsync()).Where(v => !v.Value.Contains('"')).GroupBy(d => d.KeyId).ToDictionary(d => d.Key);
            foreach (var typeStringKey in typeKeys)
            {
                var typeKey = nbtElems.Where(n => n.Slug == typeStringKey).FirstOrDefault();
                if (typeKey == null)
                    continue;
                foreach (var purityStringkey in universal)
                {
                    var purityKey = nbtElems.Where(n => n.Slug == purityStringkey).FirstOrDefault();
                    if (purityKey == null || !values.TryGetValue(typeKey.Id, out var types) || !values.TryGetValue(purityKey.Id, out var purities))
                        continue;
                    foreach (var type in types)
                    {
                        foreach (var purity in purities)
                        {
                            if (purity.Value != "PERFECT" && purity.Value != "FLAWLESS")
                                continue;
                            var elem = UniversalGemNames.GetOrAdd((type.KeyId, type.Id), (a) => new Dictionary<(short, long), string>());
                            elem.Add((purity.KeyId, purity.Id), $"{purity.Value}_{type.Value}_GEM");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Gets a number that represents the amount of coins the gems on a given auction are worth
        /// </summary>
        /// <param name="auction"></param>
        /// <returns></returns>
        public Task<int> GetGemstoneWorth(SaveAuction auction)
        {
            if (auction == null)
                return Task.FromResult(0);
            if (auction.NbtData == null)
            {
                var lookup = auction.NBTLookup;
                // from db
                return Task.FromResult(GetGemWrthFromLookup(lookup));
            }
            var gems = auction.FlatenedNBT.Where(n => n.Value == "PERFECT" || n.Value == "FLAWLESS");
            var additionalWorth = 0;
            if (gems.Any())
            {
                foreach (var g in gems)
                {
                    var type = g.Key.Split("_").First();
                    if (type == "COMBAT" || type == "DEFENSIVE" || type == "UNIVERSAL")
                        type = auction.FlatenedNBT[g.Key + "_gem"];
                    if (Prices.TryGetValue($"{g.Value}_{type}_GEM", out int value))
                        additionalWorth += value;
                }
            }

            return Task.FromResult(additionalWorth);
        }

        public int GetGemWrthFromLookup(IEnumerable<NBTLookup> lookup)
        {
            return lookup.Sum(l =>
            {
                if (GemNames.TryGetValue((l.KeyId, l.Value), out string key))
                    if (Prices.TryGetValue(key, out int value))
                    {
                        return value;
                    }
                    else
                        logger.LogDebug("Price for not found " + key);

                return 0;
            });
        }


        public List<PropertyChange> LookupToGems(NBTLookup[] lookup)
        {
            return lookup.Select(l =>
            {
                if (GemNames.TryGetValue((l.KeyId, l.Value), out string key))
                    if (Prices.TryGetValue(key, out int value))
                    {
                        return new PropertyChange()
                        {
                            Description = $"{key}",
                            Effect = value
                        };
                    }
                if (UniversalGemNames.TryGetValue((l.KeyId, l.Value), out var mappings))
                {
                    foreach (var item in lookup)
                    {
                        if (mappings.TryGetValue((item.KeyId, item.Value), out var gemName))
                            if (Prices.TryGetValue(gemName, out int value))
                            {
                                return new PropertyChange()
                                {
                                    Description = $"Universal {gemName}",
                                    Effect = value
                                };
                            }
                    }
                }
                return null;
            }).Where(u => u != null).ToList();
        }

    }
}