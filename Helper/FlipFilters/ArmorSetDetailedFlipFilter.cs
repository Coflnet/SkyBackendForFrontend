
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Linq;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;
using System.Text.RegularExpressions;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Coflnet.Sky.Core.Services;

namespace Coflnet.Sky.Commands.Shared;
public class ArmorSetDetailedFlipFilter : DetailedFlipFilter
{
    public object[] Options => GetSetOptions();

    private static object[] GetSetOptions()
    {
        var sets = GetLoadedArmorSets();
        if (sets != null && sets.Count > 2)
        {
            return sets.Keys.ToArray();
        }
        return [];
    }

    private static int warmUpStarted;

    /// <summary>
    /// Starts the item load, replaced in tests.
    /// </summary>
    internal static Func<HypixelItemService, Task> StartItemLoad = service => service.GetItemsAsync();

    /// <summary>
    /// Allows tests to arm the one-time warm-up again.
    /// </summary>
    internal static void ResetWarmUp() => Interlocked.Exchange(ref warmUpStarted, 0);

    /// <summary>
    /// Armor sets from the item metadata. The service returns null until the items are loaded,
    /// which hosts like SkySubscriptions never trigger themselves. Never blocks: returns null while unavailable
    /// and starts the load once per process so later filters get the real sets.
    /// </summary>
    private static Dictionary<string, HashSet<string>> GetLoadedArmorSets()
    {
        var service = DiHandler.GetService<HypixelItemService>();
        var sets = service.GetArmorSets();
        if (sets != null)
            return sets;
        if (Interlocked.CompareExchange(ref warmUpStarted, 1, 0) == 0)
        {
            // the service retries on its own, never start a second load from here
            var load = StartItemLoad;
            _ = Task.Run(() => load(service))
                .ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }
        return null;
    }

    public static ReadOnlyDictionary<string, string[]> ExtraArmorSets = new(new Dictionary<string, string[]>
    {
        { "seymour", new []{ "OXFORD_SHOES", "SATIN_TROUSERS", "CASHMERE_JACKET", "VELVET_TOP_HAT" } },
    });
    private static bool IsOnAh(KeyValuePair<string, int> t)
    {
        return !t.Key.Contains("_TERROR_") && !t.Key.Contains("_CRIMSON_") && !t.Key.Contains("_AURORA_") && !t.Key.Contains("_FERVOR_") && !t.Key.Contains("_HOLLOW_");
    }

    public FilterType FilterType => FilterType.Equal;

    public Expression<Func<FlipInstance, bool>> GetExpression(FilterContext filters, string val)
    {
        if (ExtraArmorSets.TryGetValue(val, out var set))
            return flip => set.Contains(flip.Auction.Tag);
        if (GetLoadedArmorSets()?.TryGetValue(val, out var tags) == true)
            return flip => tags.Contains(flip.Auction.Tag);
        var regex = new Regex('^' + Regex.Escape(val) + "_(CHESTPLATE|HELMET|BOOTS|LEGGINGS)");
        return flip => regex.Match(flip.Auction.Tag).Success;
    }
}
