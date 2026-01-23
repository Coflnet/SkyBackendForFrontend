using System;
using System.Collections.Generic;

namespace Coflnet.Sky.Commands.Shared
{
    public class PrivacySettings
    {
        public const string DefaultChatRegex =
                @"^(You cannot view this auction!|You claimed|\[Bazaar\]|\[NPC\] Kat|Cancelled"
                + @"|You collected|\[Auction\]|BIN Auction started|You cancelled|You purchased "
                + @"|Profile ID: |You placd a Trap|\+\d+ .* Attribute \(Level "
                + @"|You caught |\s+Chameleon" // catching shards
                + @"|Added items|Removed items" // stash adding notification
                + @"|You donated your" // museum donation
                + @"|: \d+m$" // chat lowballing discussion
                + @"|You sold " // npc sell for limit detection
                + @"|can't find a player by the name of|That player is not online, try another user" // autotip corrections
                + @"| - | \+ |Trade completed|Bid of|\nClick the link to |\nClick th' li|You must set it to at least).*";
        public const string DefaultChatBlockRegex =
            @"^(You tipped |You were tipped|You've already tipped someone).*";

        [SettingsDoc("Which lines should be collected from chat", true)]
        public string ChatRegex;
        [SettingsDoc("Allow collection of limited amount of chat content to track eg. trades, drops, ah and bazaar events ")]
        public bool CollectChat;
        [SettingsDoc("Upload chest and inventory content (required for trade tracking)")]
        public bool CollectInventory;
        [SettingsDoc("Stop trades from being stored")]
        public bool DisableTradeStoring;
        [SettingsDoc("Stop kuudra profit from being calculated", "noKuudra")]
        public bool DisableKuudraTracking;
        [SettingsDoc("Stop bazaar orders from being stored")]
        public bool DisableBazaarTracking;
        [SettingsDoc("Read and upload tab contents when joining server (detect profile type, server and island location)")]
        public bool CollectTab;
        [SettingsDoc("Read and upload scoreboard peridicly to detect purse")]
        public bool CollectScoreboard;
        [SettingsDoc("Allow proxying of requests to api", true)]
        public bool AllowProxy;
        [SettingsDoc("Collect clicks on chat messages")]
        public bool CollectChatClicks;
        /// <summary>
        /// Wherever or not to send item descriptions for extending to the server
        /// </summary>
        [SettingsDoc("Extend item descriptions (configure with /cofl lore)")]
        public bool ExtendDescriptions;
        /// <summary>
        /// Chat input starting with one of these prefixes is sent to the server
        /// </summary>
        [SettingsDoc("Extension for adding command aliases", true)]
        public string[] CommandPrefixes;
        [SettingsDoc("Autostart when joining skyblock")]
        public bool AutoStart;
        [SettingsDoc("Which lines should be blocked from being collected from chat", true)]
        public string ChatBlockRegex;
        [SettingsDoc("Disable hypixel message blocking")]
        public bool NoMessageBlocking;

        public override bool Equals(object obj)
        {
            return obj is PrivacySettings settings &&
                   ChatRegex == settings.ChatRegex &&
                   CollectChat == settings.CollectChat &&
                   CollectInventory == settings.CollectInventory &&
                   CollectTab == settings.CollectTab &&
                   CollectScoreboard == settings.CollectScoreboard &&
                   AllowProxy == settings.AllowProxy &&
                   CollectChatClicks == settings.CollectChatClicks &&
                   ExtendDescriptions == settings.ExtendDescriptions &&
                   EqualityComparer<string[]>.Default.Equals(CommandPrefixes, settings.CommandPrefixes) &&
                   AutoStart == settings.AutoStart &&
                   ChatBlockRegex == settings.ChatBlockRegex;
        }

        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(ChatRegex);
            hash.Add(CollectChat);
            hash.Add(CollectInventory);
            hash.Add(CollectTab);
            hash.Add(CollectScoreboard);
            hash.Add(AllowProxy);
            hash.Add(CollectChatClicks);
            hash.Add(ExtendDescriptions);
            hash.Add(CommandPrefixes);
            hash.Add(AutoStart);
            hash.Add(ChatBlockRegex);
            return hash.ToHashCode();
        }

        public static PrivacySettings Default => new PrivacySettings()
        {
            CollectInventory = true,
            ExtendDescriptions = true,
            ChatRegex = DefaultChatRegex,
            CollectChat = true,
            CollectScoreboard = true,
            CollectTab = true,
            CollectChatClicks = true,
            CommandPrefixes = new string[] { "/cofl", "/colf", "/ch" },
            AutoStart = true,
            ChatBlockRegex = DefaultChatBlockRegex
        };

    }
}