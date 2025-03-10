
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Linq;
using Coflnet.Sky.Filter;

namespace Coflnet.Sky.Commands.Shared;
public class HasRuneDetailedFlipFilter : DetailedFlipFilter
{
    public object[] Options => new object[] { "Any", "Valuable", "None" };
    private HashSet<string> valuable = [
        "UNIQUE_RUNE_RAINY_DAY","RUNE_SLIMY_3","UNIQUE_RUNE_HEARTSPLOSION","UNIQUE_RUNE_BARK_TUNES","UNIQUE_RUNE_MEOW_MUSIC",
            "UNIQUE_RUNE_SMITTEN","UNIQUE_RUNE_ICE_SKATES","RUNE_TIDAL_3","RUNE_TIDAL_2","RUNE_SNAKE_3","RUNE_MUSIC_1","RUNE_MUSIC_3",
            "RUNE_MUSIC_2","RUNE_FIERY_BURST_3","UNIQUE_RUNE_SPELLBOUND","UNIQUE_RUNE_GRAND_FREEZING","RUNE_RAINBOW_3",
            "RUNE_LIGHTNING_3","UNIQUE_RUNE_PRIMAL_FEAR","UNIQUE_RUNE_GOLDEN_CARPET","RUNE_COUTURE_3","UNIQUE_RUNE_ORNAMENTAL",
            "UNIQUE_RUNE_SUPER_PUMPKIN","RUNE_FIRE_SPIRAL_3","RUNE_DRAGON_3","RUNE_DRAGON_2","RUNE_ENCHANT_1","RUNE_ENCHANT_3",
            "RUNE_ENCHANT_2","RUNE_GRAND_SEARING_3"];

    public FilterType FilterType => FilterType.Equal;

    public Expression<Func<FlipInstance, bool>> GetExpression(FilterContext filters, string val)
    {
        if (val == "Any")
            return f => GetRuneString(f).Key != null;
        if (val == "Valuable")
            return f => NewMethod(f);
        return f => GetRuneString(f).Key == null;
    }

    private bool NewMethod(FlipInstance f)
    {
        var runeId = GetRuneString(f);
        if (runeId.Key == null)
            return false;
        if (runeId.Key.Contains("UNIQUE"))
            return true;
        return valuable.Contains(runeId.Key + "_" + runeId.Value);
    }

    private Func<FlipInstance, KeyValuePair<string, string>> GetRuneString = f => f.Auction.FlatenedNBT.FirstOrDefault(kv => kv.Key.Contains("RUNE_"));
}
