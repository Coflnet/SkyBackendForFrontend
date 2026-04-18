
using System;

namespace Coflnet.Sky.Commands.Shared
{
    /// <summary>
    /// FlipFilters need to be cammel case for frontend https://github.com/Coflnet/hypixel-react/issues/676#issuecomment-1032063637
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class CamelCaseNameDictionary<T> : ClassNameDictonary<T>
    {
        public void Add<TDerived>() where TDerived : T
        {
            var filter = Activator.CreateInstance<TDerived>();
            this.Add(GetCleardName<TDerived>(), filter);
        }

        public new static string GetCleardName<TDerived>() where TDerived : T
        {
            return typeof(TDerived).Name.Replace(typeof(T).Name, "");
        }
    }

}