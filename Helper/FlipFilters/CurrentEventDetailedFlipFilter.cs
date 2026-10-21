
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Linq;
using Coflnet.Sky.Filter;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.Shared;

public class CurrentEventDetailedFlipFilter : DetailedFlipFilter
{
    public enum Events
    {
        None,
        TravelingZoo,
        SpookyFestival,
        DarkAuction,
        NewYear,
        SeasonOfJerry,
        JacobsFarmingContest,
        HarvestFeast,
        NotTravelingZoo = 101,
        NotSpookyFestival,
        NotDarkAuction,
        NotNewYear,
        NotSeasonOfJerry,
        NotJacobsFarmingContest,
        NotHarvestFeast
    }
    public enum Months
    {
        EarlySpring,
        Spring,
        LateSpring,
        EarlySummer,
        Summer,
        LateSummer,
        EarlyAutumn,
        Autumn,
        LateAutumn,
        EarlyWinter,
        Winter,
        LateWinter
    }
    public object[] Options => Enum.GetValues(typeof(Events)).Cast<object>().ToArray();

    public FilterType FilterType => FilterType.Equal;
    protected virtual DateTime Now => DateTime.UtcNow;
    /// <summary>
    /// The Grand Feast mayor perk extends the Harvest Feast to the whole year
    /// </summary>
    private static bool IsGrandFeastActive()
    {
        return DiHandler.GetService<FilterStateService>().State.CurrentPerks.Contains("Grand Feast", StringComparer.OrdinalIgnoreCase);
    }

    public Expression<Func<FlipInstance, bool>> GetExpression(FilterContext filters, string val)
    {
        var eventVal = (Events)Enum.Parse(typeof(Events), val);
        var currentDay = GetCurrentDay();
        var eventList = new List<(int, int, Events)>()
        {
            (12*31-3,12*31, Events.NewYear),
            (03*31,03*31+3, Events.TravelingZoo),
            (9*31,9*31+3, Events.TravelingZoo),
            ((int)Months.Autumn*31+28,(int)Months.LateAutumn*31, Events.SpookyFestival),
            (11*31+23,11*31+26, Events.SeasonOfJerry),
            ((int)Months.EarlyAutumn*31,(int)Months.EarlyWinter*31-1, Events.HarvestFeast)
        };
        var currentEvents = eventList.Where(e => currentDay >= e.Item1 && currentDay <= e.Item2).Select(e => e.Item3).ToHashSet();
        var shouldBeTrue = true;
        if (eventVal >= Events.NotTravelingZoo)
        {
            eventVal = eventVal - 100;
            shouldBeTrue = false;
        }
        if(eventVal == Events.DarkAuction)
        {
            return a => (Now.Minute >= 55 && Now.Minute <= 59) == shouldBeTrue;
        }
        if(eventVal == Events.JacobsFarmingContest)
        {
            return a => (Now.Minute >= 15 && Now.Minute <= 35) == shouldBeTrue;
        }

        if (eventVal == Events.HarvestFeast)
        {
            return a => (currentEvents.Contains(Events.HarvestFeast) || IsGrandFeastActive()) == shouldBeTrue;
        }
        if (eventVal == Events.None)
        {
            return a => currentEvents.Count == 0 && !IsGrandFeastActive();
        }

        return a => currentEvents.Contains(eventVal) == shouldBeTrue;
    }

    private int GetCurrentDay()
    {
        return (int)(Constants.SkyblockYear(Now) * 31 * 12) % (31 * 12);
    }
}
