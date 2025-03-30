using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Api.Client.Api;
using Coflnet.Sky.Crafts.Client.Api;
using Coflnet.Sky.Crafts.Client.Model;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Commands.Shared;
public class UpgradePriceService : BackgroundService
{
    private Api.Client.Api.IPricesApi pricesApi;
    private Crafts.Client.Api.IKatApi katApi;
    private ILogger<UpgradePriceService> logger;
    private Dictionary<string, double> Prices = new Dictionary<string, double>();

    private List<string> ItemsToGet = new() { "RECOMBOBULATOR_3000", "PET_ITEM_TIER_BOOST" };
    private List<KatUpgradeResult> katPrices;

    public UpgradePriceService(IPricesApi pricesApi, ILogger<UpgradePriceService> logger, Crafts.Client.Api.IKatApi katApi)
    {
        this.pricesApi = pricesApi;
        this.logger = logger;
        this.katApi = katApi;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                katPrices = await katApi.GetAllKatAsync();
                foreach (var item in ItemsToGet)
                {
                    var price = await pricesApi.ApiItemPriceItemTagCurrentGetAsync(item, 1);
                    if (price == null)
                    {
                        Prices[item] = 1_010_101;
                        logger.LogError("could not get price for " + item);
                    }
                    else
                        Prices[item] = price.Buy;
                }
                await Task.Delay(TimeSpan.FromMinutes(30));
            }
            catch (TaskCanceledException)
            {
                return;
            }
            catch (System.Exception e)
            {
                logger.LogError(e, "retrieving prices");
                await Task.Delay(50000);
            }
        }
    }

    public double GetPrice(string itemId)
    {
        return Prices.GetValueOrDefault(itemId, 1);
    }

    public long GetTierBoostCost()
    {
        return -(long)GetPrice("PET_ITEM_TIER_BOOST");
    }

    public KatCost GetKatPrice(string petTag, Coflnet.Sky.Core.Tier targetRarity)
    {
        var convertedRarity = Enum.Parse<Tier>(targetRarity.ToString().Replace("_", ""));
        if (katPrices == null)
            return new(-1, -1);
        return katPrices.Where(p => p.TargetRarity == convertedRarity && p.CoreData.ItemTag == petTag).Select(p => new KatCost(p.MaterialCost, p.UpgradeCost)).FirstOrDefault();
    }

    public class KatCost
    {
        public double MaterialCost;
        public double UpgradeCost;

        public KatCost(double materialCost, double upgradeCost)
        {
            MaterialCost = materialCost;
            UpgradeCost = upgradeCost;
        }
    }
}