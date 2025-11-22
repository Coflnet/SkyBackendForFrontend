using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Coflnet.Sky.Core;
using System.Reflection;
using Coflnet.Sky.Api.Models.Mod;

namespace Coflnet.Sky.Commands.Shared
{
    /// <summary>
    /// Mapps key value air of settings to object
    /// </summary>
    public class SettingsUpdater
    {
        protected Dictionary<string, SettingDoc> options = new Dictionary<string, SettingDoc>(StringComparer.OrdinalIgnoreCase);

        public class SettingDoc
        {
            public string RealName { get; set; }
            public string Info { get; set; }
            public bool Hide { get; set; }
            public string ShortHand { get; set; }
            public string Type { get; set; }

            public string Prefix { get; set; }
        }

        public SettingsUpdater()
        {
            AddSettings(typeof(FlipSettings).GetFields(), "");
            AddSettings(typeof(ModSettings).GetFields(), "mod");
            AddSettings(typeof(VisibilitySettings).GetFields(), "show");
            AddSettings(typeof(PrivacySettings).GetFields(), "privacy");
            AddSettings(typeof(DescriptionSetting).GetFields(), "lore");
        }

        protected void AddSettings(System.Reflection.FieldInfo[] fields, string prefix = "")
        {
            foreach (var item in fields)
            {
                var isDictionary = item.FieldType.IsGenericType && item.FieldType.GetGenericTypeDefinition() == typeof(Dictionary<,>);
                var isHashSet = item.FieldType.IsGenericType && item.FieldType.GetGenericTypeDefinition() == typeof(HashSet<>);
                if (!item.FieldType.IsPrimitive && item.FieldType != typeof(string) && !item.FieldType.IsEnum && !item.FieldType.IsArray
                    && !isDictionary && !isHashSet)
                {
                    continue;
                }
                var commandSlug = (item.GetCustomAttributes(typeof(DataMemberAttribute), true).FirstOrDefault() as DataMemberAttribute)?.Name;
                if (commandSlug == null)
                    commandSlug = item.Name;
                var doc = item.GetCustomAttributes(typeof(SettingsDocAttribute), true).FirstOrDefault() as SettingsDocAttribute;
                options.Add(prefix + commandSlug, GetDesc(item, doc, prefix));
                if (doc?.ShortHand != null)
                    options.Add(doc?.ShortHand, GetDesc(item, doc, prefix, true));
            }
        }

        private SettingDoc GetDesc(FieldInfo item, SettingsDocAttribute doc, string prefix, bool isShortHand = false)
        {
            return new SettingDoc()
            {
                RealName = item.Name,
                Prefix = prefix,
                Info = doc?.Description,
                Hide = (doc?.Hide ?? false) || isShortHand,
                ShortHand = doc?.ShortHand,
                Type = item.FieldType.Name
            };
        }

        public string[] Options()
        {
            return options.Keys.ToArray();
        }

        public IEnumerable<KeyValuePair<string, SettingDoc>> ModOptions => options;

        public async Task<object> Update(IFlipConnection con, string key, string value)
        {
            if (key.Equals("blockexport", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Blockexport {value} on {con.UserId}");
            }
            if (key == "blacklist")
                con.Settings.BlackList = GetOrderedFilters(value);
            else if (key == "whitelist")
                con.Settings.WhiteList = GetOrderedFilters(value);
            else if (key == "filter")
                con.Settings.Filters = JsonConvert.DeserializeObject<Dictionary<string, string>>(value);

            else if (!options.TryGetValue(key, out SettingDoc doc))
            {
                var closest = options.Keys.Where(k => !options[k].Hide)
                    .OrderBy(k => Fastenshtein.Levenshtein.Distance(k.ToLower(), key.ToLower())).First();
                throw new UnknownSettingException(key, closest);
            }
            else if (doc.Prefix == "privacy")
            {
                var settingsService = DiHandler.GetService<SettingsService>();
                var current = await settingsService.GetCurrentValue(con.UserId.ToString(), "privacySettings", () => PrivacySettings.Default);
                var newVal = UpdateValueOnObject(value, doc.RealName, current);
                await settingsService.UpdateSetting(con.UserId.ToString(), "privacySettings", current);
                return newVal;
            }
            else if (doc.Prefix == "lore")
            {
                var settingsService = DiHandler.GetService<SettingsService>();
                var current = await settingsService.GetCurrentValue(con.UserId.ToString(), "description", () =>
                {
                    return DescriptionSetting.Default;
                });
                var newVal = UpdateValueOnObject(value, doc.RealName, current);
                await settingsService.UpdateSetting(con.UserId.ToString(), "description", current);
                return newVal;
            }
            else
                return NewMethod(con.Settings, value, doc);
            return value;
        }

        public object Update(FlipSettings con, string key, string value)
        {
            if (!options.TryGetValue(key, out SettingDoc doc))
            {
                var closest = options.Keys.OrderBy(k => Fastenshtein.Levenshtein.Distance(k.ToLower(), key.ToLower())).First();
                throw new UnknownSettingException(key, closest);
            }
            return NewMethod(con, value, doc);
        }

        private static object NewMethod(FlipSettings con, string value, SettingDoc doc)
        {
            if (doc.Prefix == "show")
            {
                if (con.Visibility == null)
                    con.Visibility = new VisibilitySettings();
                return UpdateValueOnObject(value, doc.RealName, con.Visibility);
            }
            else if (doc.Prefix == "mod")
            {
                if (con.ModSettings == null)
                    con.ModSettings = new ModSettings();
                if (doc.RealName == "Format")
                {
                    try
                    {
                        var formatted = string.Format(value, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20);
                    }
                    catch (Exception e)
                    {
                        dev.Logger.Instance.Error(e, "format check");
                        throw new CoflnetException("invalid_format", "Format update rejected. \nMake sure any squirly brackets are closed again or prefixed with another bracket to escape it, eg {{ will result in {.");
                    }
                }
                return UpdateValueOnObject(value, doc.RealName, con.ModSettings);
            }
            else
            {
                return UpdateValueOnObject(value, doc.RealName, con);
            }
        }

        private static List<ListEntry> GetOrderedFilters(string value)
        {
            return JsonConvert.DeserializeObject<List<ListEntry>>(value)
                .OrderByDescending(x => x.filter != null && x.filter.ContainsKey("ForTag") ? 1 : 0)
                .ToList();
        }

        public async Task<object> GetCurrentValue(IFlipConnection con, string key)
        {
            if (key == "blacklist")
                return con.Settings.BlackList;
            else if (key == "whitelist")
                return con.Settings.WhiteList;
            else if (key == "filter")
                return con.Settings.Filters;
            else if (!options.TryGetValue(key, out SettingDoc doc))
            {
                var closest = options.Keys.OrderBy(k => Fastenshtein.Levenshtein.Distance(k.ToLower(), key.ToLower())).First();
                throw new UnknownSettingException(key, closest);
            }
            else if (doc.Prefix == "show")
            {
                if (con.Settings.Visibility == null)
                    con.Settings.Visibility = new VisibilitySettings();
                return GetValueOnObject(doc.RealName, con.Settings.Visibility);
            }
            else if (doc.Prefix == "mod")
            {
                if (con.Settings.ModSettings == null)
                    con.Settings.ModSettings = new ModSettings();
                return GetValueOnObject(doc.RealName, con.Settings.ModSettings);
            }
            else if (doc.Prefix == "privacy")
            {
                var settingsService = DiHandler.GetService<SettingsService>();
                var current = await settingsService.GetCurrentValue(con.UserId.ToString(), "privacySettings", () => PrivacySettings.Default);
                return GetValueOnObject(doc.RealName, current);
            }
            else if (doc.Prefix == "lore")
            {
                var settingsService = DiHandler.GetService<SettingsService>();
                var current = await settingsService.GetCurrentValue(con.UserId.ToString(), "description", () =>
                {
                    return DescriptionSetting.Default;
                });
                return GetValueOnObject(doc.RealName, current);
            }
            else
            {
                return GetValueOnObject(doc.RealName, con.Settings);
            }
        }

        protected object GetValueOnObject(string realKey, object obj)
        {
            if (obj == null)
                return null;
            var field = obj?.GetType()?.GetField(realKey);
            return field?.GetValue(obj);
        }


        public SettingDoc GetDocFor(string key)
        {
            options.TryGetValue(key, out SettingDoc doc);
            return doc;
        }

        public class UnknownSettingException : CoflnetException
        {
            public string Passed;
            public string Closest;

            public UnknownSettingException(string passed, string closest) : base("invalid_setting", $"the setting {passed} doesn't exist, most similar is {closest}")
            {
                Passed = passed;
                Closest = closest;
            }
        }

        protected static object UpdateValueOnObject(string value, string realKey, object obj)
        {
            var field = obj.GetType().GetField(realKey);
            object newValue;
            if (field == null)
            {
                throw new CoflnetException("invalid_setting", $"the setting {realKey} could not be found");
            }
            // if no value is provided and its a bool toggle it
            if (string.IsNullOrEmpty(value) && field.FieldType == typeof(bool))
            {
                newValue = !(bool)field.GetValue(obj);
            }
            else if (field.FieldType.IsEnum)
                try
                {
                    newValue = Enum.Parse(field.FieldType, value, true);
                }
                catch (System.Exception)
                {
                    var values = Enum.GetNames(field.FieldType);
                    if (field.FieldType == typeof(LowPricedAuction.FinderType))
                        throw new CoflnetException("parse", "Invalid selection. Use either one or more of flipper,sniper,sniper_median,user,CraftCost");
                    throw new CoflnetException("parse", "Your input could not be parsed in any of " + String.Join(',', values));
                }
            else if (field.FieldType.IsPrimitive && field.FieldType != typeof(bool))
                try
                {
                    newValue = Convert.ChangeType(NumberParser.Double(value), field.FieldType);
                }
                catch (System.Exception)
                {
                    throw new CoflnetException("parse", "This setting has to be a number. This includes 1.6M or 3.2k");
                }
            else if (field.FieldType.IsArray)
            {
                var values = value?.Split(',') ?? [];
                var array = Array.CreateInstance(field.FieldType.GetElementType(), values.Length);
                for (int i = 0; i < values.Length; i++)
                {
                    array.SetValue(Convert.ChangeType(values[i], field.FieldType.GetElementType()), i);
                }
                newValue = array;
            }
            else if (field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                var dict = (Dictionary<string, string>)field.GetValue(obj);
                var parts = value.Split(' ', 2);
                if (parts.Length == 1)
                {
                    dict.Remove(parts[0]);
                    return null;
                }
                else
                {
                    dict[parts[0]] = parts[1];
                    return parts[1];
                }
            }
            else if (field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(HashSet<>))
            {
                var set = (HashSet<string>)field.GetValue(obj);
                if (set == null)
                {
                    set = new HashSet<string>();
                    field.SetValue(obj, set);
                }
                if (value == "clear")
                {
                    set.Clear();
                    return null;
                }
                if (value.StartsWith("rm "))
                {
                    set.Remove(value.Substring(3));
                    return null;
                }
                set.Add(value);
                return value;
            }
            else
                try
                {
                    newValue = Convert.ChangeType(value, field.FieldType);
                }
                catch (System.Exception)
                {
                    if (field.FieldType == typeof(bool))
                        throw new CoflnetException("parse", "This setting can only be set to true or false. No value toggles it.");
                    throw;
                }

            field.SetValue(obj, newValue);

            return newValue;
        }
    }
}
