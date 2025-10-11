using System.Collections.Generic;
using Coflnet.Sky.Commands.Shared;
using Coflnet.Sky.Core;

namespace Coflnet.Sky.Api.Models.Mod;

/// <summary>
/// Custom settings of what modifications to include in the response
/// </summary>
public class DescriptionSetting
{
    /// <summary>
    /// Lines and which elements to put into these lines
    /// </summary>
    public List<List<DescriptionField>> Fields { get; set; }
    /// <summary>
    /// If black and whitelist matches should be highlighted
    /// </summary>
    [SettingsDoc("Highlight items in ah and trade windows when matching black or whitelist filter")]
    public bool HighlightFilterMatch;
    [SettingsDoc("What is the minimum profit for highlighting best flip on page")]
    public long MinProfitForHighlight;
    [SettingsDoc("Disable all highlighting")]
    public bool DisableHighlighting;
    [SettingsDoc("Disable all sign input suggestions", "nosuggest")]
    public bool DisableSuggestions;
    [SettingsDoc("Disable side info display in these menus, will add any menu you type into this setting, to remove prefix with `rm `, `clear` is also an option")]
    public HashSet<string> DisableInfoIn;

    public static DescriptionSetting Default => new DescriptionSetting()
    {
        Fields = new List<List<DescriptionField>>() {
                    new() { DescriptionField.LBIN, DescriptionField.BazaarBuy, DescriptionField.BazaarSell },
                    new() { DescriptionField.MEDIAN, DescriptionField.VOLUME },
                    new() { DescriptionField.FullCraftCost } }
    };

    [SettingsDoc("If the extra lore should be displayed or not")]
    public bool Disabled;
    [SettingsDoc("Mow many percent to undercut the median price when lowballing, the lower of median and lbin will be used, setting this setting to 1 or more will hide the note in the lowballing info", "medUndercut")]
    public byte LowballMedUndercut;
    [SettingsDoc("Mow many percent to undercut the lbin price when lowballing, for items below 10m this is increased by 2% for items above 100m this is decreased by 2%, under 1 volume will also increase this by another 3%", "lbinUndercut")]
    public byte LowballLbinUndercut = 10;
    [SettingsDoc("Prefer current lbin for suggestions over stable median", "suggestLbin")]
    public bool PreferLbinInSuggestions;
    [SettingsDoc("Suggest quicksell prices on listing", "suggestQuicksell")]
    public bool SuggestQuicksell;
    [SettingsDoc("Replace lore color gray with this color code")]
    public string ReplaceGrayWith;
    [SettingsDoc("Replace lore color aqua with this color code")]
    public string ReplaceAquaWith;
    [SettingsDoc("Replace lore color yellow with this color code")]
    public string ReplaceYellowWith;
    [SettingsDoc("Replace lore color gold with this color code")]
    public string ReplaceGoldWith;
    [SettingsDoc("Replace lore color white with this color code")]
    public string ReplaceWhiteWith;
    [SettingsDoc("Enables no cookie workarounds, can be incompatible with other mods and considered a macro", "nocookie")]
    public bool? NoCookie;

    public HighlightInfo HighlightInfo { get; set; }
}

public class HighlightInfo
{
    public BlockPos Position { get; set; }
    public string HexColor { get; set; } = "#00FF00";
    public int SlotId { get; set; } = -1;
    public string Chestname { get; set; } = "Highlight";
}