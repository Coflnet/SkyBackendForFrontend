
using System;
using System.Linq.Expressions;
using Coflnet.Sky.Filter;
using Coflnet.Sky.Core;
using NUnit.Framework;

namespace Coflnet.Sky.Commands.Shared;

public class YearOfTheDetailedFlipFilter : DetailedFlipFilter
{
    public object[] Options => ["None", "Seal", "Pig"];

    public FilterType FilterType => FilterType.Equal;

    public Expression<Func<FlipInstance, bool>> GetExpression(FilterContext filters, string val)
    {
        string[] iterval = ["Seal", "None", "None", "None", "None", "None", "Pig", "None", "None", "None", "None", "None"];
        return f => iterval[((int)Constants.SkyblockYear(DateTime.UtcNow) - 400) % 12] == val;
    }
}
