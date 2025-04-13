using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.Helper
{
    /// <summary>
    /// Takes care of selecting interesting/relevant properties from a flip
    /// </summary>
    public class PropertiesSelector
    {
        [DataContract]
        public class Property
        {
            [DataMember(Name = "val")]
            public string Value;
            /// <summary>
            /// how important is this?
            /// </summary>
            [IgnoreDataMember]
            public int Rating;

            public Property()
            { }
            public Property(string value, int rating)
            {
                Value = value;
                Rating = rating;
            }

        }

        private static Dictionary<Enchantment.EnchantmentType, byte> RelEnchantLookup = null;

        public static IEnumerable<Property> GetProperties(SaveAuction auction)
        {
            var properties = new List<Property>();

            if (RelEnchantLookup == null)
                RelEnchantLookup = Coflnet.Sky.Core.Constants.RelevantEnchants.ToDictionary(r => r.Type, r => r.Level);


            var data = auction.FlatenedNBT;
            var bedEstimate = (auction.Start + TimeSpan.FromSeconds(20) - DateTime.UtcNow).TotalSeconds;
            if (bedEstimate > 0)
            {
                properties.Add(new Property($"Bed: {((int)bedEstimate)}s", 20));
            }
            if (data.TryGetValue("winning_bid", out string winning_bid))
            {
                properties.Add(new Property("Top Bid: " + string.Format("{0:n0}", long.Parse(winning_bid)), 20));
            }
            if (data.TryGetValue("hpc", out string hpcvalue))
                properties.Add(new Property("HPB: " + hpcvalue, 12));
            if (data.ContainsKey("rarity_upgrades"))
                properties.Add(new Property("Recombobulated ", 12));
            if (auction.Count > 1)
                properties.Add(new Property($"Count x{auction.Count}", 12));
            if (data.TryGetValue("heldItem", out string heldItem))
                properties.Add(new Property($"Holds {ItemDetails.TagToName(heldItem)}", 12));
            if (data.TryGetValue("candyUsed", out string candyUsed))
                properties.Add(new Property($"Candy Used {candyUsed}", 11));
            if (data.TryGetValue("farming_for_dummies_count", out string farmingForDummies))
                properties.Add(new Property($"Farming for dummies {farmingForDummies}", 11));
            if (data.TryGetValue("skin", out string skin))
                properties.Add(new Property($"Skin: {ItemDetails.TagToName(skin)}", 15));
            if (data.TryGetValue("spider_kills", out string spider_kills))
                properties.Add(new Property($"Kills: {ItemDetails.TagToName(spider_kills)}", 15));
            if (data.TryGetValue("zombie_kills", out string zombie_kills))
                properties.Add(new Property($"Kills: {ItemDetails.TagToName(zombie_kills)}", 15));
            if (data.TryGetValue("unlocked_slots", out string unlocked_slots))
                properties.Add(new Property($"Unlocked: {(unlocked_slots.Sum(c => c == ',' ? 1 : 0) + 1)}", 15));
            if (data.ContainsKey("ethermerge"))
                properties.Add(new Property($"Ethermerge", 13));
            if (data.TryGetValue("color", out string color) && !string.IsNullOrEmpty(color))
                properties.Add(new Property($"Color: {FormatHex(color)}", 5));

            properties.AddRange(data.Where(p => p.Value == "PERFECT" || p.Value == "FLAWLESS")
                // Jasper0 slot can't be accessed on starred (Fragged) items
                .Where(p => !(auction.Tag?.StartsWith("STARRED_SHADOW_ASSASSIN") ?? false && p.Key.StartsWith("JASPER_0")))
                .Select(p => new Property($"{p.Value} gem", p.Value == "PERFECT" ? 14 : 7)));

            var isBook = auction.Tag == "ENCHANTED_BOOK";

            var enchants = auction.Enchantments?.Where(e => (!RelEnchantLookup.ContainsKey(e.Type) && e.Level >= 6) || (RelEnchantLookup.TryGetValue(e.Type, out byte lvl)) && e.Level >= lvl).Select(e => new Property()
            {
                Value = $"{ItemDetails.TagToName(e.Type.ToString())}: {e.Level}",
                Rating = 2 + e.Level + (e.Type.ToString().StartsWith("ultimate") ? 5 : 0) + (e.Type == Enchantment.EnchantmentType.infinite_quiver ? -3 : 0)
            });
            if (enchants != null)
                properties.AddRange(enchants);

            if (data.TryGetValue("drill_part_engine", out string engine))
                properties.Add(new Property($"Engine: {ItemDetails.TagToName(engine)}", 15));
            if (data.TryGetValue("drill_part_fuel_tank", out string tank))
                properties.Add(new Property($"Tank: {ItemDetails.TagToName(tank)}", 15));
            if (data.TryGetValue("drill_part_upgrade_module", out string module))
                properties.Add(new Property($"Module: {ItemDetails.TagToName(module)}", 15));

            return properties;
        }

        // minecraft java hex color codes to chat code
        private static Dictionary<string, string> HexToColorCodeLookup = new()
        {
            {"AA0000","§4"},
            {"FF5555","§c"},
            {"FFAA00","§6"},
            {"FFFF55","§e"},
            {"00AA00","§2"},
            {"55FF55","§a"},
            {"55FFFF","§b"},
            {"00AAAA","§3"},
            {"0000AA","§1"},
            {"5555FF","§9"},
            {"FF55FF","§d"},
            {"420055","§5"}, // added for 1f0030
            {"501080","§5"}, // adjusted to match 54146e (which also appears purple but is closer to gray)
            {"A020A0","§5"},
            {"FFFFFF","§f"},
            {"AAAAAA","§7"},
            {"555555","§8"},
            {"000000","§0"},
        };

        private static Dictionary<(int, int, int), string> ColorCodeToHexLookup = HexToColorCodeLookup
            .ToDictionary(k => (ParsePart(k.Key.Substring(0, 2)), ParsePart(k.Key.Substring(2, 2)), ParsePart(k.Key.Substring(4, 2))),
                v => v.Value);

        private static int ParsePart(string hex)
        {
            return int.Parse(hex, System.Globalization.NumberStyles.HexNumber);
        }

        public static string FormatHex(string separated)
        {
            try
            {
                // 0:0:0 to hex
                var parts = separated.Split(':').Select(p => int.TryParse(p, out var val) ? val : 0).ToArray();
                var hex = string.Join("", parts.Select(p => p.ToString("X2")));
                var numeric = int.Parse(hex, System.Globalization.NumberStyles.HexNumber);
                var closest = ColorCodeToHexLookup.Keys.Select(k => (k, dist: Math.Sqrt(Math.Pow(parts[0] - k.Item1, 2) + Math.Pow(parts[1] - k.Item2, 2) + Math.Pow(parts[2] - k.Item3, 2)))).OrderBy(k => k.dist).ToList();
                return ColorCodeToHexLookup[closest.First().k] + hex + "§f";
            }
            catch (Exception e)
            {
                throw new Exception($"Failed to parse hex color {separated}", e);
            }
        }
    }
}