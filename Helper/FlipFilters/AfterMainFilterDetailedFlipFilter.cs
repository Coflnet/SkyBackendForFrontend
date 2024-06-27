
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Coflnet.Sky.Filter;

namespace Coflnet.Sky.Commands.Shared
{
    [FilterDescription("Moves this whitelist after the main filters (minprofit, maxcost etc)")]
    public class AfterMainFilterDetailedFlipFilter : DetailedFlipFilter
    {
        public object[] Options => new object[] { "true" };

        public FilterType FilterType => FilterType.BOOLEAN | FilterType.SIMPLE;

        public Expression<Func<FlipInstance, bool>> GetExpression(FilterContext filters, string val)
        {
            return null;
        }
    }
}