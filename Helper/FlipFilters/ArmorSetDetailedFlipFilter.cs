
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Linq;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;
using System.Text.RegularExpressions;
using System.Collections.ObjectModel;
using Coflnet.Sky.Core.Services;

namespace Coflnet.Sky.Commands.Shared;
public class ArmorSetDetailedFlipFilter : DetailedFlipFilter
{
    public object[] Options => GetSetOptions();

    private static object[] GetSetOptions()
    {
        var service = DiHandler.GetService<HypixelItemService>();
        var sets = service.GetArmorSets();
        if (sets.Count > 2)
        {
            return sets.Keys.ToArray();
        }
        return [];
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
        var service = DiHandler.GetService<HypixelItemService>();
        if (service.GetArmorSets().TryGetValue(val, out var tags))
            return flip => tags.Contains(flip.Auction.Tag);
        var regex = new Regex('^' + Regex.Escape(val) + "_(CHESTPLATE|HELMET|BOOTS|LEGGINGS)");
        return flip => regex.Match(flip.Auction.Tag).Success;
    }
}
