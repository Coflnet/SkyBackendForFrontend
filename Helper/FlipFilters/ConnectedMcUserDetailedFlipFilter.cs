
using System;
using System.Linq.Expressions;
using Coflnet.Sky.Filter;

namespace Coflnet.Sky.Commands.Shared;

[FilterDescription("Matches for connected Minecraft account name/uuid")]
public class ConnectedMcUserDetailedFlipFilter : DetailedFlipFilter
{
    public object[] Options => [""];

    public FilterType FilterType => FilterType.TEXT;

    public Expression<Func<FlipInstance, bool>> GetExpression(FilterContext filters, string val)
    {
        if (val.Length < 32)
            return f => filters.playerInfo.McName == null ? false : filters.playerInfo.McName.Equals(val, StringComparison.OrdinalIgnoreCase);
        return f => filters.playerInfo.McUuid == null ? false : filters.playerInfo.McUuid.Equals(val, StringComparison.OrdinalIgnoreCase);
    }
}