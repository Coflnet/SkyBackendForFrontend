
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;

namespace Coflnet.Sky.Commands.Shared;

[FilterDescription("Adjusts target price based on craft cost of ingredients multiplied by weight")]
public class CraftCostWeightDetailedFlipFilter : NumberDetailedFlipFilter
{
    public override FilterType FilterType => FilterType.RANGE;
    private static Dictionary<string, double> DefaultWeights = new() {
        { "skin", 0.5 },
        { "ultimate_fatal_tempo", 0.65},
        { "rarity_upgrades", 0.5},
        { "upgrade_level", 0.8},
        { "talisman_enrichment", 0.1},
        { "RUNE_MUSIC", 0.5},
        { "RUNE_MEOW_MUSIC", 0.5},
        { "RUNE_DRAGON", 0.5},
        { "RUNE_TIDAL", 0.5},
        { "RUNE_GRAND_SEARING", 0.5},
        { "RUNE_ENCHANT", 0.5},
        { "RUNE_BARK_TUNES", 0.5},
        { "RUNE_BARK_SMITTEN", 0.5},
        { "RUNE_BARK_ICE_SKATES", 0.5},
        { "RUNE_SPELLBOUND", 0.5},
        { "RUNE_GRAND_FREEZING", 0.5},
        { "RUNE_PRIMAL_FEAR", 0.5},
        { "cleanItem", 1}
    };
    public CraftCostWeightDetailedFlipFilter()
    {
        foreach (var item in Enum.GetNames<ItemReferences.Reforge>())
        {
            validModifiers.Add(item); // technically not all reforges can show up because of the threshold
        }
        foreach (var item in Constants.AttributeKeys)
        {
            DefaultWeights.TryAdd(item, 0);
        }
    }

    public override Expression<Func<FlipInstance, bool>> GetExpression(FilterContext filters, string val)
    {
        Dictionary<string, double> multipliers = ParseMultipliers(val);
        foreach (var item in multipliers)
        {
            var targetAttrib = item.Key.Split('.')[0];
            if (validModifiers.Contains(targetAttrib, StringComparer.OrdinalIgnoreCase))
                continue;
            var mostSimilar = validModifiers.OrderBy(m => Fastenshtein.Levenshtein.Distance(targetAttrib, m)).First();
            throw new CoflnetException("invalid_argument", $"Invalid modifier `{item.Key}` provided, did you mean `{mostSimilar}`?");
        }
        if (!multipliers.TryGetValue("default", out var defaultMultiplier))
            throw new CoflnetException("missing_argument", "No default multiplier provided, use default:0.9 to disable");
        foreach (var item in DefaultWeights)
        {
            var toUse = Math.Min(item.Value, defaultMultiplier);
            if (item.Key == "cleanItem")
                toUse = 1;
            multipliers.TryAdd(item.Key, toUse);
        }

        filters.filters.TryGetValue("MinProfit", out var minprofitString);
        filters.filters.TryGetValue("MinProfitPercentage", out var minprofitPercentString);
        NumberParser.TryLong(minprofitString, out var target);
        NumberParser.TryLong(minprofitPercentString, out var minProfitPercent);
        var anyDot = multipliers.Keys.Any(k => k.Contains('.'));

        return f => f.Finder == LowPricedAuction.FinderType.CraftCost &&
            CalculateCraftCostWithMultipliers(f, multipliers, defaultMultiplier, target, minProfitPercent, anyDot);
    }

    private static bool CalculateCraftCostWithMultipliers(FlipInstance f, Dictionary<string, double> multipliers, double defaultMultiplier, long minProfit, long minProfitPercent, bool anyDot)
    {
        if (!f.Context.TryGetValue("breakdown", out var breakdownSerialized))
            return false;
        var breakdown = JsonSerializer.Deserialize<Dictionary<string, long>>(breakdownSerialized);
        var lookup = new Dictionary<string, string>(f.Auction.FlatenedNBT, StringComparer.OrdinalIgnoreCase);
        if (anyDot)
            foreach (var item in f.Auction.Enchantments)
            {
                lookup.TryAdd(item.Type.ToString(), item.Level.ToString());
            }
        breakdown.TryAdd("cleanItem", long.Parse(f.Context["cleanCost"]));
        var valueSum = breakdown.Select(b => GetMultiplier(multipliers, defaultMultiplier, b, lookup) * b.Value).Sum();
        var targetMinusTax = valueSum * 0.98;
        var profit = targetMinusTax - f.Auction.StartingBid;
        if (minProfit > profit)
            return false;
        if (minProfitPercent > 0 && minProfitPercent > profit * 100 / (f.LastKnownCost == 0 ? int.MaxValue : f.LastKnownCost))
            return false;
        f.Context["target"] = valueSum.ToString();
        return true;
    }

    private static double GetMultiplier(Dictionary<string, double> multipliers, double defaultMultiplier, KeyValuePair<string, long> b, Dictionary<string, string> lookup)
    {
        if (multipliers.TryGetValue($"{b.Key}.{lookup.GetValueOrDefault(b.Key)}", out var multiplier))
            return multiplier;
        return multipliers.GetValueOrDefault(b.Key, defaultMultiplier);
    }

    private static Dictionary<string, double> ParseMultipliers(string val)
    {
        if (val.Contains('&'))
        {
            throw new CoflnetException("invalid_argument", "Use commas to separate multipliers, not `&`, also don't put unnecessary spaces anywhere");
        }
        try
        {
            return val.Split(',').Select(v =>
            {
                if (!v.Contains(':'))
                    throw new CoflnetException("invalid_argument", $"DoubleDot (:) missing in filter at '{v}'");
                return v;
            }).ToDictionary(m => m.Split(':')[0], m => NumberParser.Double(m.Split(':')[1]), StringComparer.OrdinalIgnoreCase);
        }
        catch (CoflnetException)
        {
            throw;
        }
        catch (ArgumentException e)
        {
            throw new CoflnetException("filter_parsing", $"Dupplicate weight in '{val.Truncate(20)}...' for {e.Message.Split("Key: ").Last()}");
        }
        catch (Exception e)
        {
            Console.WriteLine("craftcost filter: " + e);
            throw new CoflnetException("filter_parsing", $"Error in filter CraftCostWeight. Make sure to specify pairs separated by commas of like `modifier:multiplier,sharpness:0.7`");
        }
    }

    private readonly HashSet<string> validModifiers = ["default", "cleanItem",
    "AMBER_0",
    "AMBER_1",
    "AMETHYST_0",
    "AQUAMARINE_0",
    "AQUAMARINE_1",
    "arachno",
    "arachno_resistance",
    "art_of_war_count",
    "artOfPeaceApplied",
    "attack_speed",
    "bane_of_arthropods",
    "baseStatBoost",
    "baseStatBoostPercentage",
    "big_brain",
    "blast_protection",
    "blazing",
    "blazing_fortune",
    "blazing_resistance",
    "blessing",
    "blocksBroken",
    "blood_god_kills",
    "bow_kills",
    "breeze",
    "candyUsed",
    "cayenne",
    "champion",
    "chance",
    "charm",
    "CHISEL_0",
    "CHISEL_1",
    "CHISEL_2",
    "cleave",
    "collected_coins",
    "color",
    "COMBAT_0",
    "COMBAT_1",
    "combo",
    "compact",
    "counter_strike",
    "critical",
    "cubism",
    "cultivating",
    "deadeye",
    "dedication",
    "DEFENSIVE_0",
    "delicate",
    "divan_powder_coating",
    "divine_gift",
    "dominance",
    "double_hook",
    "dragon_hunter",
    "drill_part_engine",
    "drill_part_fuel_tank",
    "drill_part_upgrade_module",
    "dye_item",
    "edition",
    "efficiency",
    "elite",
    "eman_kills",
    "ender",
    "ender_resistance",
    "ender_slayer",
    "ethermerge",
    "execute",
    "exp",
    "experience",
    "expertise",
    "expertise_kills",
    "farming_for_dummies_count",
    "feather_falling",
    "ferocious_mana",
    "fire_aspect",
    "fire_protection",
    "first_strike",
    "fisherman",
    "fishing_experience",
    "fishing_speed",
    "fortitude",
    "frail",
    "full_bid",
    "giant_killer",
    "green_thumb",
    "growth",
    "handles_found",
    "hardened_mana",
    "harvesting",
    "hecatomb",
    "hotpc",
    "hunter",
    "ice_cold",
    "ignition",
    "infection",
    "is_shiny",
    "JADE_0",
    "JADE_1",
    "JASPER_0",
    "JASPER_1",
    "lapidary",
    "life_recovery",
    "life_regeneration",
    "life_steal",
    "lifeline",
    "looting",
    "luck",
    "lure",
    "magic_find",
    "mana_pool",
    "mana_regeneration",
    "mana_steal",
    "mana_vampire",
    "MASTER_CRYPT_TANK_ZOMBIE_60",
    "MASTER_CRYPT_TANK_ZOMBIE_70",
    "MASTER_CRYPT_TANK_ZOMBIE_80",
    "MASTER_CRYPT_UNDEAD__ONAH_25",
    "MASTER_CRYPT_UNDEAD_AGENTK_25",
    "MASTER_CRYPT_UNDEAD_ALEXANDER_40",
    "MASTER_CRYPT_UNDEAD_APUNCH_25",
    "MASTER_CRYPT_UNDEAD_BEMBO_25",
    "MASTER_CRYPT_UNDEAD_BLOOZING_25",
    "MASTER_CRYPT_UNDEAD_CECER_25",
    "MASTER_CRYPT_UNDEAD_CHILYNN_25",
    "MASTER_CRYPT_UNDEAD_CODENAME_B_25",
    "MASTER_CRYPT_UNDEAD_CONNORLINFOOT_25",
    "MASTER_CRYPT_UNDEAD_DCTR_25",
    "MASTER_CRYPT_UNDEAD_DONPIRESO_25",
    "MASTER_CRYPT_UNDEAD_DUECES_25",
    "MASTER_CRYPT_UNDEAD_EXTERNALIZABLE_25",
    "MASTER_CRYPT_UNDEAD_FLAMEBOY101_25",
    "MASTER_CRYPT_UNDEAD_HYPIXEL_25",
    "MASTER_CRYPT_UNDEAD_JAMIETHEGEEK_25",
    "MASTER_CRYPT_UNDEAD_JAYAVARMEN_25",
    "MASTER_CRYPT_UNDEAD_JUDG3_25",
    "MASTER_CRYPT_UNDEAD_LADYBLEU_25",
    "MASTER_CRYPT_UNDEAD_LIKAOS_25",
    "MASTER_CRYPT_UNDEAD_MAGICBOYS_25",
    "MASTER_CRYPT_UNDEAD_MINIKLOON_25",
    "MASTER_CRYPT_UNDEAD_NITROHOLIC__25",
    "MASTER_CRYPT_UNDEAD_ORANGEMARSHALL_25",
    "MASTER_CRYPT_UNDEAD_PLANCKE_25",
    "MASTER_CRYPT_UNDEAD_RELENTER_25",
    "MASTER_CRYPT_UNDEAD_REVENGEEE_25",
    "MASTER_CRYPT_UNDEAD_REZZUS_25",
    "MASTER_CRYPT_UNDEAD_SFARNHAM_25",
    "MASTER_CRYPT_UNDEAD_SKYERZZ_25",
    "MASTER_CRYPT_UNDEAD_SYLENT_25",
    "MASTER_CRYPT_UNDEAD_THEMGRF_25",
    "MASTER_CRYPT_UNDEAD_THORLON_25",
    "MASTER_CRYPT_UNDEAD_WILLIAMTIGER_25",
    "mending",
    "midas_touch",
    "mined_crops",
    "MINING_0",
    "MINOS_CHAMPION_310",
    "MINOS_INQUISITOR_750",
    "model",
    "new_years_cake",
    "OPAL_0",
    "overload",
    "paleontologist",
    "party_hat_color",
    "party_hat_emoji",
    "PERIDOT_0",
    "PERIDOT_1",
    "pesterminator",
    "petItem",
    "pgems",
    "piscary",
    "power",
    "pristine",
    "projectile_protection",
    "prosecute",
    "prosperity",
    "protection",
    "quantum",
    "raider_kills",
    "rarity_upgrades",
    "reflection",
    "rejuvenate",
    "replenish",
    "RUNE_BARK_TUNES",
    "RUNE_BITE",
    "RUNE_BLOOD_2",
    "RUNE_CLOUDS",
    "RUNE_COUTURE",
    "RUNE_DRAGON",
    "RUNE_ENCHANT",
    "RUNE_ENDERSNAKE",
    "RUNE_FIERY_BURST",
    "RUNE_FIRE_SPIRAL",
    "RUNE_GEM",
    "RUNE_GOLDEN",
    "RUNE_GOLDEN_CARPET",
    "RUNE_GRAND_FREEZING",
    "RUNE_GRAND_SEARING",
    "RUNE_HEARTS",
    "RUNE_HOT",
    "RUNE_ICE",
    "RUNE_ICE_SKATES",
    "RUNE_JERRY",
    "RUNE_LAVA",
    "RUNE_LAVATEARS",
    "RUNE_LIGHTNING",
    "RUNE_MAGIC",
    "RUNE_MEOW_MUSIC",
    "RUNE_MUSIC",
    "RUNE_PRIMAL_FEAR",
    "RUNE_RAINBOW",
    "RUNE_RAINY_DAY",
    "RUNE_REDSTONE",
    "RUNE_SLIMY",
    "RUNE_SMITTEN",
    "RUNE_SMOKEY",
    "RUNE_SNAKE",
    "RUNE_SNOW",
    "RUNE_SOULTWIST",
    "RUNE_SPARKLING",
    "RUNE_SPELLBOUND",
    "RUNE_SPIRIT",
    "RUNE_SUPER_PUMPKIN",
    "RUNE_TIDAL",
    "RUNE_WAKE",
    "RUNE_WHITE_SPIRAL",
    "RUNE_ZAP",
    "RUNE_ZOMBIE_SLAYER",
    "SAPPHIRE_0",
    "SAPPHIRE_1",
    "scavenger",
    "scroll_count",
    "sharpness",
    "skin",
    "smarty_pants",
    "smite",
    "smoldering",
    "snipe",
    "speed",
    "spider_kills",
    "strong_mana",
    "sugar_rush",
    "sunder",
    "sword_kills",
    "syphon",
    "tabasco",
    "talisman_enrichment",
    "thunder_charge",
    "thunderbolt",
    "thunderlord",
    "titan_killer",
    "TOPAZ_0",
    "toxophilite",
    "transylvanian",
    "triple_strike",
    "trophy_hunter",
    "true_protection",
    "turbo_cactus",
    "turbo_coco",
    "turbo_potato",
    "turbo_pumpkin",
    "ultimate_bobbin_time",
    "ultimate_chimera",
    "ultimate_combo",
    "ultimate_fatal_tempo",
    "ultimate_flash",
    "ultimate_flowstate",
    "ultimate_habanero_tactics",
    "ultimate_inferno",
    "ultimate_last_stand",
    "ultimate_legion",
    "ultimate_no_pain_no_gain",
    "ultimate_one_for_all",
    "ultimate_refrigerate",
    "ultimate_reiterate",
    "ultimate_rend",
    "ultimate_soul_eater",
    "ultimate_swarm",
    "ultimate_the_one",
    "ultimate_wisdom",
    "ultimate_wise",
    "undead",
    "undead_resistance",
    "UNIVERSAL_0",
    "unlocked_slots",
    "upgrade_level",
    "vampirism",
    "venomous",
    "veteran",
    "vicious",
    "virtual",
    "warrior",
    "winning_bid",
    "wood_singularity_count",
    "yogsKilled",
    "zombie_kills"
    ];
}
