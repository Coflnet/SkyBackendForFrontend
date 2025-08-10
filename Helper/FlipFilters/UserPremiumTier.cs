
using System;
using System.Linq;
using System.Linq.Expressions;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;

namespace Coflnet.Sky.Commands.Shared;

[FilterDescription("Triggers for the premium tier of the user")]
public class UserPremiumTier : DetailedFlipFilter
{
    public object[] Options => Enum.GetValues<AccountTier>().Select(v => (object)v).ToArray();

    public FilterType FilterType => FilterType.Equal;

    public Expression<Func<FlipInstance, bool>> GetExpression(FilterContext filters, string val)
    {
        var parsed = Enum.Parse<AccountTier>(val);
        if (filters.playerInfo == null)
            throw new CoflnetException("no tier passed", "Player info is required for UserPremiumTier filter");
        return f => filters.playerInfo.SessionTier == parsed;
    }
}