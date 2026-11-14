using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Crafts.Client.Api;

namespace Coflnet.Sky.Commands.Shared
{
    /// <summary>
    /// Supplies the cost to craft one unit of an item, used to cap implausible auction prices.
    /// </summary>
    public interface ICraftCostProvider
    {
        /// <summary>Craft cost of one unit, null when not craftable or unknown</summary>
        Task<double?> GetUnitCraftCost(string itemTag);
    }

    /// <summary>
    /// <see cref="ICraftCostProvider"/> backed by the crafts service. Results (including "no craft cost")
    /// are cached per tag, failures and timeouts yield null (= no cap) and are cached briefly.
    /// </summary>
    public class CraftCostProvider : ICraftCostProvider
    {
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan FailureCacheDuration = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);
        private readonly ICraftsApi craftsApi;
        private readonly ConcurrentDictionary<string, (DateTime expires, double? cost)> cache = new();

        public CraftCostProvider(ICraftsApi craftsApi)
        {
            this.craftsApi = craftsApi;
        }

        /// <inheritdoc />
        public async Task<double?> GetUnitCraftCost(string itemTag)
        {
            if (string.IsNullOrEmpty(itemTag))
                return null;
            if (cache.TryGetValue(itemTag, out var cached) && cached.expires > DateTime.UtcNow)
                return cached.cost;
            try
            {
                using var cts = new CancellationTokenSource(RequestTimeout);
                var plan = await craftsApi.GetAcquisitionAsync(itemTag, 1, cancellationToken: cts.Token);
                // CraftCost is the cost of the requested quantity (1) via the craft path. Recipes that yield more
                // than one item per craft (e.g. 3 PAPER) make this an overestimate, which only loosens the cap.
                double? cost = plan != null && plan.CraftEnough && plan.CraftCost > 0
                    ? plan.CraftCost / Math.Max(1, plan.Quantity)
                    : null;
                cache[itemTag] = (DateTime.UtcNow + CacheDuration, cost);
                return cost;
            }
            catch (Exception)
            {
                cache[itemTag] = (DateTime.UtcNow + FailureCacheDuration, null);
                return null;
            }
        }
    }
}
