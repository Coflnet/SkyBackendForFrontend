
using System;
using System.Linq.Expressions;
using Coflnet.Sky.Filter;

namespace Coflnet.Sky.Commands.Shared;

[FilterDescription("Days until next Derpy, gets calculated once on load")]
public class DerpyDaysDetailedFlipFilter : DaysTillSpecialMayorFilter
{
    protected override DateTime MayorStart => FlipInstance.DerpyStart;
}

[FilterDescription("Days until next Scorpius, gets calculated once on load")]
public class ScorpiusDaysDetailedFlipFilter : DaysTillSpecialMayorFilter
{
    protected override DateTime MayorStart => FlipInstance.DerpyStart.AddHours(124 * 8 * 2); // Scorpius starts 2 mayor cycles after Derpy
}

[FilterDescription("Days until next Jerry, gets calculated once on load")]
public class JerryDaysDetailedFlipFilter : DaysTillSpecialMayorFilter
{
    protected override DateTime MayorStart => FlipInstance.DerpyStart.AddHours(124 * 8); // Jerry starts 1 mayor cycles after Derpy
}

public abstract class DaysTillSpecialMayorFilter : NumberDetailedFlipFilter
{
    public override bool CanCache => false; // has to be refreshed
    public override object[] Options => [0, 150];

    protected override Expression<Func<FlipInstance, double>> GetSelector(FilterContext filters)
    {
        double days = NextStartInDays(DateTime.UtcNow);
        return (f) => days;
    }

    public double NextStartInDays(DateTime now)
    {
        var nextStart = MayorStart;
        while (nextStart < now)
        {
            nextStart = nextStart.AddHours(124 * 8 * 3);
        }
        var days = (nextStart - now).TotalDays;
        return days;
    }

    protected abstract DateTime MayorStart { get; }
}
