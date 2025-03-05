using System;
using System.Linq;

namespace Coflnet.Sky.Commands.Shared
{
    public class GenericSettingsUpdater : SettingsUpdater
    {
        public GenericSettingsUpdater() 
        {
            options.Clear();
        }
        public void AddSettings(Type type, string prefix = "")
        {
            AddSettings(type.GetFields(), prefix);
        }

        public object Update(object target, string key, string value)
        {
            if(!options.TryGetValue(key, out SettingDoc doc))
            {
                var closest = options.Keys.OrderBy(k => Fastenshtein.Levenshtein.Distance(k.ToLower(), key.ToLower())).First();
                throw new UnknownSettingException(key, closest);
            }
            return UpdateValueOnObject(value, doc.RealName, target);
        }
    }
}
