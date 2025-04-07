
using System;
using System.Linq.Expressions;
using Coflnet.Sky.Filter;

namespace Coflnet.Sky.Commands.Shared;

[FilterDescription("Range based profit per hour, recommended for whitelists")]
public class EstProfitPerHourDetailedFlipFilter : NumberDetailedFlipFilter
{
    protected override Expression<Func<FlipInstance, double>> GetSelector(FilterContext filters)
    {
        return (f) => (double)GetVolume(f) * f.Profit;
    }

    private static float GetVolume(FlipInstance f)
    {
        if (f.Context?.TryGetValue("minToSell", out var minToSell) ?? false)
        {
            var minutes = float.Parse(minToSell);
            return 1 / (minutes / 60);
        }
        return f.Volume / 24;
    }
}
