using System;
using System.Linq;
using System.Linq.Expressions;
using Coflnet.Sky.Filter;

namespace Coflnet.Sky.Commands.Shared;

[FilterDescription("Matches leading candidate perks in active public elections, both mayor and minister")]
public class NextPerkDetailedFlipFilter : ActivePerkDetailedFlipFilter
{
    public override Expression<Func<FlipInstance, bool>> GetExpression(FilterContext filters, string val)
    {
        val = CheckValue(val);
        var service = DiHandler.GetService<FilterStateService>();
        service.UpdateState().Wait();
        return (f) => service.State.NextPerks.Contains(val, StringComparer.OrdinalIgnoreCase);
    }
}