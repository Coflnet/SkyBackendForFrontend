
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;

namespace Coflnet.Sky.Commands.Shared;

[FilterDescription("Sets a price to relist at, mostly for user finder")]
public class RelistAtDetailedFlipFilter : NumberDetailedFlipFilter
{
    public override object[] Options => [1, 50_000_000_000];

    public override FilterType FilterType => FilterType.NUMERICAL;

    public override Expression<Func<FlipInstance, bool>> GetExpression(FilterContext filters, string val)
    {
        var target = NumberParser.Double(val);
        return f => AddTarget(f, target);
    }

    private static bool AddTarget(FlipInstance f, double target)
    {
        f.Context["target"] = target.ToString();
        return target * 0.97 > f.Auction.StartingBid;
    }
}
