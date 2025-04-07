using System;
using System.Collections.Generic;
using System.Linq;

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
            if (!options.TryGetValue(key, out SettingDoc doc))
            {
                var closest = options.Keys.OrderBy(k => Fastenshtein.Levenshtein.Distance(k.ToLower(), key.ToLower())).First();
                throw new UnknownSettingException(key, closest);
            }
            if (!string.IsNullOrEmpty(doc.Prefix))
            {
                if (mapper.TryGetValue(doc.Prefix, out var func))
                {
                    target = func(target);
                }
            }
            return UpdateValueOnObject(value, doc.RealName, target);
        }
    }
}
