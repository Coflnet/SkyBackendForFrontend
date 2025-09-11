using System;
using System.Text.RegularExpressions;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.Shared;

public class BazaarUtils
{
    public static string GetSearchValue(string tag, string name)
    {
        if (tag.StartsWith("ENCHANTMENT_"))
        {
            // remove enchant from end
            name = name.Substring(0, name.Length - 10);
            var number = Regex.Match(tag, @"\d+").Value;
            var converted = Roman.To(int.Parse(number));
            name = $"{name.Trim()} {converted}"
                .Replace("reiterate", "duplex")
                .Replace("pristine", "prismatic"); // renamed enchantment
        }
        if (tag.StartsWith("ENCHANTMENT_ULTIMATE"))
        {
            name = name.Replace("ultimate ", "", StringComparison.OrdinalIgnoreCase);
        }
        return name;
    }
}
