using System;
using System.Linq;
using System.Linq.Expressions;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;

namespace Coflnet.Sky.Commands.Shared;

[FilterDescription("Triggers when either the mayor or minister has a given perk active")]
public class ActivePerkDetailedFlipFilter : DetailedFlipFilter
{
    public object[] Options => [
        "Extra Event",
        "Sweet Tooth",
        "Fishing Festival",
        "Fishing XP Buff",
        "Luck of the Sea 2.0",
        "Mythological Ritual",
        "Slayer XP Buff",
        "Mining Fiesta",
        "Mining XP Buff",
        "Prospection",
        "Marauder",
        "Barrier Street",
        "Shopping Spree",
        "Benevolence",
        "Perkpocalypse",
        "Statspocalypse",
        "Jerrypocalypse",
        "Astral Negotiator",
        "SLASHED Pricing",
        "Benediction",
        "Lucky!",
        "Bribe",
        "Darker Auctions",
        "Pathfinder",
        "Arcane Catalyst",
        "EZPZ",
        "TURBO MINIONS!!!",
    // "AH CLOSED!!!", won't come back
        "DOUBLE MOBS HP!!!",
        "MOAR SKILLZ!!!",
        "Pet XP Buff",
        "Farming Simulator",
        "Pelt-pocalypse",
        "GOATed",
        "Pest Eradicator",
        "Blooming Business",
        "Sharing is Caring",
        "Chivalrous Carnival",
        "Long Term Investment",
        "A Time for Giving",
        "Volume Trading",
        "Stock Exchange",
        "Molten Forge",
        "QUAD TAXES!!!"
    ];

    public FilterType FilterType => FilterType.Equal;

    public virtual Expression<Func<FlipInstance, bool>> GetExpression(FilterContext filters, string val)
    {
        val = CheckValue(val);
        var service = DiHandler.GetService<FilterStateService>();
        service.UpdateState().Wait();
        return (f) => service.State.CurrentPerks.Contains(val, StringComparer.OrdinalIgnoreCase);
    }

    protected string CheckValue(string val)
    {
        val = Options.FirstOrDefault(t => t.ToString().ToLower() == val.ToLower())?.ToString().ToLower();
        if (val == null)
            throw new CoflnetException("invalid_perk", "The specified Perk was not found");
        return val;
    }
}