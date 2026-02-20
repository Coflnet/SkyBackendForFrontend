using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Coflnet.Sky.Commands.MC;
using Coflnet.Sky.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Coflnet.Sky.Commands.Shared;

public class InventoryParser
{
    /* json sample
    {
    "_events": {},
    "_eventsCount": 0,
    "id": 0,
    "type": "minecraft:inventory",
    "title": "Inventory",
    "slots": [
        null,
        null,
        null,
        null,
        null,
        {
            "type": 306,
            "count": 1,
            "metadata": 0,
            "nbt": {
                "type": "compound",
                "name": "",
                "value": {
                    "ench": {
                        "type": "list",
                        "value": {
                            "type": "end",
                            "value": []
                        }
                    },
                    "Unbreakable": {
                        "type": "byte",
                        "value": 1
                    },
                    "HideFlags": {
                        "type": "int",
                        "value": 254
                    },
                    "display": {
                        "type": "compound",
                        "value": {
                            "Lore": {
                                "type": "list",
                                "value": {
                                    "type": "string",
                                    "value": [
                                        "┬º7Defense: ┬ºa+10",
                                        "",
                                        "┬º7Growth I",
                                        "┬º7Grants ┬ºa+15 ┬ºcÔØñ Health┬º7.",
                                        "",
                                        "┬º7┬ºcYou do not have a high enough",
                                        "┬ºcEnchanting level to use some of",
                                        "┬ºcthe enchantments on this item!",
                                        "",
                                        "┬º7┬º8This item can be reforged!",
                                        "┬ºf┬ºlCOMMON HELMET"
                                    ]
                                }
                            },
                            "Name": {
                                "type": "string",
                                "value": "┬ºfIron Helmet"
                            }
                        }
                    },
                    "ExtraAttributes": {
                        "type": "compound",
                        "value": {
                            "id": {
                                "type": "string",
                                "value": "IRON_HELMET"
                            },
                            "enchantments": {
                                "type": "compound",
                                "value": {
                                    "growth": {
                                        "type": "int",
                                        "value": 1
                                    }
                                }
                            },
                            "mined_crops": {
                                "type": "long",
                                "value": [
                                    1,
                                    8314091
                                ]
                            },
                            "uuid": {
                                "type": "string",
                                "value": "0cf52647-c130-43ec-9c46-e2dc162d4894"
                            },
                            "timestamp": {
                                "type": "string",
                                "value": "2/18/23 4:27 AM"
                            }
                        }
                    }
                }
            },
            "name": "iron_helmet",
            "displayName": "Iron Helmet",
            "stackSize": 1,
            "slot": 5
        }
	  ]
}*/
    public IEnumerable<SaveAuction> Parse(dynamic data)
    {
        dynamic full = null;
        if (data is string json)
            full = JsonConvert.DeserializeObject(json);
        else
            full = data;
        if (full is JArray array)
        {
            foreach (var item in ParseChatTriggers(array))
                yield return item;
            yield break;
        }
        if (full.slots == null)
        {
            Activity.Current?.AddEvent(new ActivityEvent("Log", default, new(new Dictionary<string, object>() {
                { "message", "The inventory json at key `jsonNbt` is missing the slots property. Make sure you serialized bot.inventory" },
                { "json", JsonConvert.SerializeObject(data) } })));
            throw new CoflnetException("missing_slots", "The inventory json at key `jsonNbt` is missing the slots property. Make sure you serialized bot.inventory");
        }
        foreach (var item in full.slots)
        {
            if (item == null)
            {
                yield return null;
                continue;
            }

            var extraAttributes = GetExtraAttributes(item);
            if (extraAttributes == null)
            {
                yield return new SaveAuction()
                {
                    Tag = "UNKOWN",
                    Enchantments = new(),
                    Count = 1,
                    ItemName = GetItemName(item),
                    Uuid = Random.Shared.Next().ToString(),
                };
                continue;
            }
            if (GetAttributeValue(extraAttributes, "id") == null) // no tag
            {
                yield return null;
                continue;
            }
            Dictionary<string, object> attributesWithoutEnchantments = null;
            SaveAuction auction = null;
            try
            {
                CreateAuction(item, extraAttributes, out attributesWithoutEnchantments, out auction);
                auction?.SetFlattenedNbt(NBT.FlattenNbtData(attributesWithoutEnchantments).GroupBy(e => e.Key).Select(e => e.First()).ToList());
                FixItemTag(auction);
                if (auction.Tag?.EndsWith("RUNE") ?? false)
                {
                    if (TryGetRuneType(extraAttributes, out string runeType))
                        UpdateRune(auction, runeType);
                }
            }
            catch (System.Exception e)
            {
                Activity.Current?.AddEvent(new ActivityEvent("Log", default, new(new Dictionary<string, object>() { {
                    "message", "Error while parsing inventory" }, { "error", e }, {"item", JsonConvert.SerializeObject(item)} })));
                //     dev.Logger.Instance.Error(e, "Error while parsing inventory");
            }
            yield return auction;
        }
    }

    private static void FixItemTag(SaveAuction auction)
    {
        if (auction.Tag == "PET")
        {
            auction.Tag += "_" + auction.FlatenedNBT.FirstOrDefault(e => e.Key == "type").Value;
        }
        else if (auction.Tag == "POTION")
        {
            auction.Tag += "_" + auction.FlatenedNBT.FirstOrDefault(e => e.Key == "potion").Value;
        }
        else if (auction.Tag == "ABICASE")
        {
            auction.Tag += "_" + auction.FlatenedNBT.FirstOrDefault(e => e.Key == "model").Value;
        }
        else if (auction.Tag == "ATTRIBUTE_SHARD")
        {
            if (TryGetShardTagFromName(auction.ItemName, out var shardTag))
            {
                auction.Tag = shardTag;
            }
        }
    }

    private void CreateAuction(dynamic item, JObject extraAttributes, out Dictionary<string, object> attributesWithoutEnchantments, out SaveAuction auction)
    {
        attributesWithoutEnchantments = new Dictionary<string, object>();
        var typedFormat = IsTypedExtraAttributes(extraAttributes);
        if (typedFormat)
            Denest(extraAttributes, attributesWithoutEnchantments);
        else
            DenestPlain(extraAttributes, attributesWithoutEnchantments);

        var enchantments = new Dictionary<string, int>();
        if (typedFormat)
        {
            if (extraAttributes["enchantments"]?["value"] is JObject enchantmentsObj)
            {
                foreach (var enchantment in enchantmentsObj.Properties())
                {
                    enchantments[enchantment.Name] = enchantment.Value?["value"]?.Value<int>() ?? 0;
                }
            }
        }
        else if (extraAttributes["enchantments"] is JObject plainEnchantments)
        {
            foreach (var enchantment in plainEnchantments.Properties())
            {
                enchantments[enchantment.Name] = enchantment.Value.Value<int>();
            }
        }

        string name = item.nbt.value?.display?.value?.Name?.value ?? GetItemName(item);
        if (name?.StartsWith("{") ?? false)
            name = ParseTextComponent(name);
        auction = new SaveAuction
        {
            Tag = GetAttributeValue(extraAttributes, "id"),
            Enchantments = enchantments.Select(e => new Enchantment() { Type = Enum.Parse<Enchantment.EnchantmentType>(e.Key, true), Level = (byte)e.Value }).ToList(),
            Count = item.count,
            ItemName = name,
            Uuid = GetAttributeValue(extraAttributes, "uuid") ?? Random.Shared.Next().ToString(),
        };

        var description = item.nbt.value?.display?.value?.Lore?.value?.value?.ToObject<string[]>() as string[];
        if (description == null)
            description = GetDescription(item);
        if (description != null)
        {
            if (description.FirstOrDefault()?.StartsWith("{") == true)
                description = description.Select(ParseTextComponent).ToArray();

            if (!NBT.GetAndAssignTier(auction, description?.LastOrDefault()?.ToString()))
                // retry auction tier position
                NBT.GetAndAssignTier(auction, description?.Reverse().Skip(7).FirstOrDefault()?.ToString());
            if (auction.Context == null)
                auction.Context = new();
            auction.Context.Add("lore", string.Join("\n", description));
        }
        if (attributesWithoutEnchantments.ContainsKey("modifier"))
        {
            auction.Reforge = Enum.Parse<ItemReferences.Reforge>(attributesWithoutEnchantments["modifier"].ToString(), true);
            attributesWithoutEnchantments.Remove("modifier");
        }
        if (attributesWithoutEnchantments.TryGetValue("unlocked_slots", out var unlockedObj) && unlockedObj is List<object> unlockedList)
        {
            // override format with comma, the default chooses spaces but for some reason this didn't go through to db
            // haven't found where it is so this is an uggly workaround
            attributesWithoutEnchantments["unlocked_slots"] = string.Join(",", unlockedList.OrderBy(a => a).Select(e => e.ToString()));
        }

        // some items encode `unlocked_slots` nested inside the `gems` compound
        if (!attributesWithoutEnchantments.ContainsKey("unlocked_slots") && attributesWithoutEnchantments.TryGetValue("gems", out var gemsObj))
        {
            if (gemsObj is Dictionary<string, object> gemsDict && gemsDict.TryGetValue("unlocked_slots", out var nestedUnlocked) && nestedUnlocked is List<object> nestedList)
            {
                attributesWithoutEnchantments["unlocked_slots"] = string.Join(",", nestedList.OrderBy(a => a).Select(e => e.ToString()));
                // remove from nested to avoid duplicate processing later
                gemsDict.Remove("unlocked_slots");
            }
            else if (gemsObj is Newtonsoft.Json.Linq.JObject gemsJ && gemsJ.TryGetValue("unlocked_slots", out var jToken))
            {
                if (jToken is Newtonsoft.Json.Linq.JArray jArr)
                {
                    var list = jArr.Select(t => t.ToString()).OrderBy(s => s);
                    attributesWithoutEnchantments["unlocked_slots"] = string.Join(",", list);
                    gemsJ.Remove("unlocked_slots");
                }
            }
        }
        if (attributesWithoutEnchantments.ContainsKey("timestamp"))
        {
            try
            {
                AssignCreationTime(attributesWithoutEnchantments, auction);
            }
            catch (System.Exception e)
            {
                Activity.Current?.AddEvent(new ActivityEvent("Log", default, new(new Dictionary<string, object>() { {
                    "message", "Error while parsing timestamp" }, { "error", e }, {"item", JsonConvert.SerializeObject(item)} })));
                dev.Logger.Instance.Error(e, "Error while parsing timestamp");
            }
        }
    }

    private static void AssignCreationTime(Dictionary<string, object> attributesWithoutEnchantments, SaveAuction auction)
    {
        // format for 2/18/23 4:27 AM
        var format = "M/d/yy h:mm tt";
        if (!attributesWithoutEnchantments.TryGetValue("timestamp", out var timestamp))
            return;
        var stringDate = timestamp.ToString();
        if (long.TryParse(stringDate, out var milliseconds))
        {
            auction.ItemCreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).DateTime;
            attributesWithoutEnchantments.Remove("timestamp");
            return;
        }
        var parsedDate = DateTime.ParseExact(stringDate, format, System.Globalization.CultureInfo.InvariantCulture);
        auction.ItemCreatedAt = parsedDate;
        attributesWithoutEnchantments.Remove("timestamp");
    }

    public class TextElement
    {
        public string Text { get; set; }
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public string Color { get; set; }
    }
    public class TextLine
    {
        private static Dictionary<string, string> colorList;
        static TextLine()
        {
            colorList = typeof(McColorCodes)
              .GetFields(BindingFlags.Public | BindingFlags.Static)
              .Where(f => f.FieldType == typeof(string))
              .ToDictionary(f => f.Name.ToLower(),
                            f => (string)f.GetValue(null));
        }
        public List<TextElement> Extra { get; set; }

        public string To1_08()
        {
            if (Extra == null)
                return string.Empty;
            return string.Join("", Extra.Select(e => $"{(e.Bold ? McColorCodes.BOLD : String.Empty)}{(e.Italic ? McColorCodes.ITALIC : String.Empty)}{(e.Color != null && colorList.TryGetValue(e.Color, out var c) ? c : String.Empty)}{e.Text}"));
        }
    }


    private IEnumerable<SaveAuction> ParseChatTriggers(JArray full)
    {
        foreach (var item in full)
        {
            var extraAttributes = item["tag"]["ExtraAttributes"];
            if (extraAttributes == null)
            {
                yield return new SaveAuction()
                {
                    Count = (int)item["Count"],
                    ItemName = item["display"]["Name"].ToString(),
                    Enchantments = new(),
                };
                continue;
            }
            var enchants = extraAttributes["enchantments"]?.ToObject<Dictionary<string, int>>()?
                    .Select(e => new Enchantment() { Type = Enum.Parse<Enchantment.EnchantmentType>(e.Key, true), Level = (byte)e.Value })
                    .ToList();

            var flatNbt = NBT.FlattenNbtData(extraAttributes.ToObject<Dictionary<string, object>>()
                        .Where(e => e.Key != "enchantments").ToDictionary(e => e.Key, e => e.Value))
                            .ToDictionary(n => n.Key, n => n.Value);
            var auction = new SaveAuction()
            {
                Count = (int)item["Count"],
                ItemName = item["tag"]["display"]["Name"].ToString(),
                Enchantments = enchants,
                Tag = extraAttributes["id"].ToString(),
                Uuid = extraAttributes["uuid"]?.ToString() ?? Random.Shared.Next().ToString(),
            };
            NBT.GetAndAssignTier(auction, item["tag"]["display"]["Lore"]?.LastOrDefault()?.ToString());
            if (auction.Tier == Tier.UNKNOWN)
                foreach (var line in item["tag"]["display"]["Lore"].Reverse())
                {
                    if (NBT.TryFindTierInString(line.ToString(), out Tier tier))
                        auction.Tier = tier;
                }
            AssignCreationTime(flatNbt, auction);
            auction.SetFlattenedNbt(flatNbt.ToList());
            FixItemTag(auction);
            if (auction.Tag?.EndsWith("RUNE") ?? false)
            {
                var rune = extraAttributes["runes"] as JObject;
                var type = rune?.Properties().FirstOrDefault()?.Name;
                UpdateRune(auction, type);
            }

            yield return auction;
        }
    }

    private static void UpdateRune(SaveAuction auction, string type)
    {
        if (type == null)
            return;
        auction.Tag += $"_{type}";
        // replace the element in nbt
        if (auction.FlatenedNBT.TryGetValue(type, out var value))
        {
            auction.FlatenedNBT.Remove(type);
            auction.FlatenedNBT.Add("RUNE_" + type, value);
        }
    }

    public static bool TryGetShardTagFromName(string name, out string tag)
    {
        var clearedname = System.Text.RegularExpressions.Regex.Replace(name, "§[0-9a-fklmnor]|SELL |BUY | Shard", "").Replace(' ', '_');
        if (Constants.ShardNames.TryGetValue(clearedname, out var shardTag))
            tag = "SHARD_" + shardTag.ToUpper();
        else
        {
            Console.WriteLine($"unknown shard name {name}, {clearedname}");
            var closestDistance = Constants.ShardNames
                .Select(s => (s, Distance: Fastenshtein.Levenshtein.Distance(clearedname, s.Key)))
                .OrderBy(x => x.Distance)
                .FirstOrDefault();
            if (closestDistance.Distance < 3)
            {
                tag = "SHARD_" + closestDistance.s.Value.ToUpper();
                Console.WriteLine($"using closest shard name {tag} for {name}");
                Constants.ShardNames[clearedname] = closestDistance.s.Value;
            }
            else
            {
                Console.WriteLine($"unknown shard name {name}, not using it");
                tag = null;
                return false;
            }
        }
        return true;
    }

    private static void Denest(dynamic ExtraAttributes, Dictionary<string, object> attributesWithoutEnchantments)
    {
        foreach (JProperty attribute in ExtraAttributes)
        {
            if (attribute.Name == "enchantments")
                continue;

            // p.Name
            var type = attribute.Value["type"].ToString();
            if (type == "list")
            {
                var list = new List<object>();
                if (((string)attribute.Value["value"]["type"]) == "compound")
                {
                    foreach (var item in attribute.Value["value"]["value"])
                    {
                        var dict = new Dictionary<string, object>();
                        Denest(item, dict);
                        list.Add(dict);
                    }
                }
                else
                    foreach (var item in attribute.Value["value"]["value"])
                    {
                        list.Add(item.ToString());
                    }
                attributesWithoutEnchantments[attribute.Name] = list;
            }
            else if ((attribute.Name.EndsWith("_0") || attribute.Name.EndsWith("_1") || attribute.Name.EndsWith("_2") || attribute.Name.EndsWith("_3") || attribute.Name.EndsWith("_4"))
                        && type == "compound")
            {
                // has uuid
                var values = attribute.Value["value"];
                attributesWithoutEnchantments[attribute.Name] = values["quality"]["value"].ToString();
                attributesWithoutEnchantments[attribute.Name + ".uuid"] = values["uuid"]["value"].ToString();
            }
            else if (type == "compound" && attribute.Value["value"] is JObject)
            {
                var dict = new Dictionary<string, object>();
                Denest(attribute.Value["value"], dict);
                attributesWithoutEnchantments[attribute.Name] = dict;
            }
            else if (type == "compound")
                Denest(attribute.Value["value"], attributesWithoutEnchantments);
            else if (type == "long")
                attributesWithoutEnchantments[attribute.Name] = ((long)attribute.Value["value"][0] << 32) + (int)attribute.Value["value"][1];
            else
                attributesWithoutEnchantments[attribute.Name] = attribute.Value["value"];
        }
    }

    private static JObject GetExtraAttributes(dynamic item)
    {
        var token = item as JToken ?? JToken.FromObject(item);
        return token.SelectToken("nbt.value.ExtraAttributes.value") as JObject
            ?? token["ExtraAttributes"] as JObject
            ?? token.SelectToken("nbt['minecraft:custom_data']") as JObject;
    }

    private static bool IsTypedExtraAttributes(JObject extraAttributes)
    {
        return extraAttributes.Properties().Any(p => p.Value is JObject o && o["type"] != null && o["value"] != null);
    }

    private static string GetAttributeValue(JObject extraAttributes, string key)
    {
        var token = extraAttributes?[key];
        if (token == null)
            return null;
        if (token is JObject obj && obj["value"] != null)
            return obj["value"]?.ToString();
        return token.Type == JTokenType.Null ? null : token.ToString();
    }

    private static string GetItemName(dynamic item)
    {
        var token = item as JToken ?? JToken.FromObject(item);
        var mineflayerName = token.SelectToken("nbt.value.display.value.Name.value")?.ToString();
        if (!string.IsNullOrEmpty(mineflayerName))
            return ParseTextComponent(mineflayerName);

        var azaleaName = token.SelectToken("nbt['minecraft:custom_name']");
        if (azaleaName != null)
            return ParseTextComponent(azaleaName);

        return token["displayName"]?.ToString();
    }

    private static string[] GetDescription(dynamic item)
    {
        var token = item as JToken ?? JToken.FromObject(item);
        var mineflayerLore = token.SelectToken("nbt.value.display.value.Lore.value.value") as JArray;
        if (mineflayerLore != null)
            return mineflayerLore.Select(ParseTextComponent).ToArray();

        var azaleaLore = token.SelectToken("nbt['minecraft:lore']") as JArray;
        if (azaleaLore != null)
            return azaleaLore.Select(ParseTextComponent).ToArray();

        return null;
    }

    private static string ParseTextComponent(JToken token)
    {
        if (token == null || token.Type == JTokenType.Null)
            return null;

        if (token.Type == JTokenType.String)
        {
            var value = token.ToString();
            if (!(value?.StartsWith("{") ?? false))
                return value;
            var textLine = JsonConvert.DeserializeObject<TextLine>(value);
            return textLine?.To1_08() ?? value;
        }

        var text = token.ToString(Formatting.None);
        var line = JsonConvert.DeserializeObject<TextLine>(text);
        return line?.To1_08() ?? text;
    }

    private static string ParseTextComponent(string value)
    {
        if (!(value?.StartsWith("{") ?? false))
            return value;
        var textLine = JsonConvert.DeserializeObject<TextLine>(value);
        return textLine?.To1_08() ?? value;
    }

    private static bool TryGetRuneType(JObject extraAttributes, out string runeType)
    {
        runeType = null;
        var runesToken = extraAttributes?["runes"];
        if (runesToken is JObject typedRunes && typedRunes["value"] is JObject typedRunesValue)
        {
            runeType = typedRunesValue.Properties().FirstOrDefault()?.Name;
            return runeType != null;
        }
        if (runesToken is JObject plainRunes)
        {
            runeType = plainRunes.Properties().FirstOrDefault()?.Name;
            return runeType != null;
        }
        return false;
    }

    private static void DenestPlain(JObject extraAttributes, Dictionary<string, object> attributesWithoutEnchantments)
    {
        foreach (var attribute in extraAttributes.Properties())
        {
            if (attribute.Name == "enchantments")
                continue;

            if ((attribute.Name.EndsWith("_0") || attribute.Name.EndsWith("_1") || attribute.Name.EndsWith("_2") || attribute.Name.EndsWith("_3") || attribute.Name.EndsWith("_4"))
                        && attribute.Value is JObject keyedCompound && keyedCompound["quality"] != null)
            {
                attributesWithoutEnchantments[attribute.Name] = keyedCompound["quality"]?.ToString();
                if (keyedCompound["uuid"] != null)
                    attributesWithoutEnchantments[attribute.Name + ".uuid"] = keyedCompound["uuid"]?.ToString();
                continue;
            }

            if (attribute.Value is JObject obj)
            {
                var dict = new Dictionary<string, object>();
                DenestPlain(obj, dict);
                attributesWithoutEnchantments[attribute.Name] = dict;
            }
            else if (attribute.Value is JArray array)
            {
                var list = new List<object>();
                foreach (var item in array)
                {
                    if (item is JObject listObj)
                    {
                        var dict = new Dictionary<string, object>();
                        DenestPlain(listObj, dict);
                        list.Add(dict);
                    }
                    else if (item is JValue val)
                        list.Add(val.Value);
                    else
                        list.Add(item.ToString());
                }
                attributesWithoutEnchantments[attribute.Name] = list;
            }
            else if (attribute.Value is JValue value)
                attributesWithoutEnchantments[attribute.Name] = value.Value;
            else
                attributesWithoutEnchantments[attribute.Name] = attribute.Value?.ToString();
        }
    }
}
