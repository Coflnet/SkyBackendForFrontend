
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Coflnet.Sky.Filter;
using hypixel;

namespace Coflnet.Sky.Commands.Shared
{
    public class MinProfitDetailedFlipFilter : DetailedFlipFilter
    {
        public object[] Options => new object[]{1,100_000_000};

        public FilterType FilterType => FilterType.NUMERICAL | FilterType.LOWER | FilterType.RANGE;

        public  Expression<Func<FlipInstance, bool>> GetExpression(Dictionary<string, string> filters, string val)
        {
            var min = long.Parse(val);
            return flip => flip.Profit > min;
        }
    }
    

}