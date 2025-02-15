
using System;
using System.Linq.Expressions;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;

namespace Coflnet.Sky.Commands.Shared;

[FilterDescription("Above 1 reduces by absolute number, from 0-1 uses percentage 0.2 removes 20%, always matches every flip")]
public class ReduceTargetByDetailedFlipFilter : NumberDetailedFlipFilter
{
    public override object[] Options => [0, 50_000_000_000];

    public FilterType FilterType => FilterType.NUMERICAL;

    public override Expression<Func<FlipInstance, bool>> GetExpression(FilterContext filters, string val)
    {
        var target = NumberParser.Double(val);
        if (target < 1)
        {
            return f => ReducePercentage(f, target);
        }
        return f => ReduceAbsolute(f, target);
    }

    private static bool ReducePercentage(FlipInstance f, double target)
    {
        f.Context["target"] = ((long)(f.Target * (1 - target))).ToString();
        return true;
    }

    private static bool ReduceAbsolute(FlipInstance f, double target)
    {
        f.Context["target"] = (f.Target - target).ToString();
        return true;
    }
}
