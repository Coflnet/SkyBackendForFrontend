
using System;
using System.Linq.Expressions;
using hypixel;

namespace Coflnet.Sky.Commands.Shared
{
    public class ProfitPercentageDetailedFlipFilter : NumberDetailedFlipFilter
    {
        protected override Expression<Func<FlipInstance, long>> GetSelector()
        {
            return (f) => (long)f.ProfitPercentage;
        }
    }
    

}