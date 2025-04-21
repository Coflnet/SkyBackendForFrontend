using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.Shared
{
    public class GenericSettingsUpdater : SettingsUpdater
    {
        private Dictionary<string, Func<object, object>> mapper = new();
        public GenericSettingsUpdater()
        {
            options.Clear();
        }
        public void AddSettings(Type type, string prefix = "", Func<object, object> mapper = null)
        {
            if (mapper != null)
                this.mapper.Add(prefix, mapper);
            AddSettings(type.GetFields(), prefix);
        }

        public object Update(object target, string key, string value)
        {
            SettingDoc doc = GetTargetAndInfo(ref target, key);
            return UpdateValueOnObject(value, doc.RealName, target);
        }

        private SettingDoc GetTargetAndInfo(ref object target, string key)
        {
            if (!options.TryGetValue(key, out SettingDoc doc))
            {
                var closest = options.Keys.OrderBy(k => Fastenshtein.Levenshtein.Distance(k.ToLower(), key.ToLower())).First();
                throw new UnknownSettingException(key, closest);
            }
            if (!string.IsNullOrEmpty(doc.Prefix))
            {
                if (mapper.TryGetValue(doc.Prefix, out var func))
                {
                    target = func(target);                }
            }

            return doc;
        }

        public object GetCurrent(object target, string key)
        {
            SettingDoc doc = GetTargetAndInfo(ref target, key);
            return GetValueOnObject(doc.RealName, target);
        }
    }
}
