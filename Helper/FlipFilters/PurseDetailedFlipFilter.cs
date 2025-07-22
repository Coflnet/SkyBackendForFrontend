
using System;
using System.Linq.Expressions;
using Coflnet.Sky.Filter;

namespace Coflnet.Sky.Commands.Shared;

[FilterDescription("Filter for last known purse amount")]
public class PurseDetailedFlipFilter : NumberDetailedFlipFilter
{
    public override bool CanCache => false;
    public override object[] Options => [-1, 100_000_000_000];

    protected override Expression<Func<FlipInstance, double>> GetSelector(FilterContext filters)
    {
        return (f) => filters.playerInfo == null ? -1 : filters.playerInfo.Purse;
    }
}