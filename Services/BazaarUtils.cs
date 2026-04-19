using System;
using System.Text.RegularExpressions;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Commands.Shared;

public class BazaarUtils
{
    public static string GetSearchValue(string tag, string name)
    {
        if ((string.IsNullOrWhiteSpace(name) || string.Equals(name, tag, StringComparison.Ordinal))
            && tag.StartsWith("SHARD_", StringComparison.Ordinal))
        {
            return tag["SHARD_".Length..].ToLowerInvariant().Replace("_", " ") + " shard";
        }

        if (tag.StartsWith("ENCHANTMENT_"))
        {
            // Build enchantment names from the item tag to avoid brittle display-name parsing.
            var enchantTag = tag.Replace("ENCHANTMENT_", "");
            var numberMatch = Regex.Match(enchantTag, @"_(\d+)$");
            var number = numberMatch.Success ? numberMatch.Groups[1].Value : null;
            if (numberMatch.Success)
            {
                enchantTag = enchantTag.Substring(0, enchantTag.Length - numberMatch.Value.Length);
            }

            var enchantName = enchantTag.ToLowerInvariant().Replace("_", " ");
            name = number == null
                ? enchantName
                : $"{enchantName} {Roman.To(int.Parse(number))}";

            name = name
                .Replace("reiterate", "duplex")
                .Replace("syphon", "drain")
                .Replace("bobbin time", "bobbin' time")
                .Replace("turbo ", "turbo-")
                .Replace("pristine", "prismatic"); // renamed enchantment

            name = Regex.Replace(name, @"(^|[\s-])[a-z]", m => m.Value.ToUpperInvariant());
        }
        if (tag.StartsWith("ENCHANTMENT_ULTIMATE"))
        {
            name = name.Replace("ultimate ", "", StringComparison.OrdinalIgnoreCase);
        }
        return name;
    }
}
