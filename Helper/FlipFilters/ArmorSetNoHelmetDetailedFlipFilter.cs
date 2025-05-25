
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Linq;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;

namespace Coflnet.Sky.Commands.Shared;

public class ArmorSetNoHelmetDetailedFlipFilter : DetailedFlipFilter
{
    public object[] Options => DiHandler.GetService<ItemDetails>().TagLookup.Where(t => t.Key.EndsWith("_LEGGINGS")).Select(t => (object)t.Key.Replace("_LEGGINGS", "")).ToArray();

    public FilterType FilterType => FilterType.Equal;

    public Expression<Func<FlipInstance, bool>> GetExpression(FilterContext filters, string val)
    {
        return flip => flip.Auction.Tag.StartsWith(val) && (flip.Auction.Tag.EndsWith("_LEGGINGS") || flip.Auction.Tag.EndsWith("_CHESTPLATE") || flip.Auction.Tag.EndsWith("_BOOTS"));
    }
}