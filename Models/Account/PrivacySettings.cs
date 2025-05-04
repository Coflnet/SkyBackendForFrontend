using System;
using System.Collections.Generic;

namespace Coflnet.Sky.Commands.Shared
{
    public class PrivacySettings
    {
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
        [SettingsDoc("Read and upload tab contents when joining server (detect profile type, server and island location)")]
        public bool CollectTab;
        [SettingsDoc("Read and upload scoreboard peridicly to detect purse")]
        public bool CollectScoreboard;
        [SettingsDoc("Allow proxying of requests to api", true)]
        public bool AllowProxy;
        [SettingsDoc("Collect which item was clicked in chest (not actually used)")]
        public bool CollectInvClick;
        [SettingsDoc("Collect clicks on chat messages")]
        public bool CollectChatClicks;
        [SettingsDoc("Collect which server you are on")]
        public bool CollectLobbyChanges;
        [SettingsDoc("Collect entities near you (1.6.x mod feature)")]
        public bool CollectEntities;
        [SettingsDoc("Collect player position and orientation")]
        public bool CollectLocation;
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

        public override bool Equals(object obj)
        {
            return obj is PrivacySettings settings &&
                   ChatRegex == settings.ChatRegex &&
                   CollectChat == settings.CollectChat &&
                   CollectInventory == settings.CollectInventory &&
                   CollectTab == settings.CollectTab &&
                   CollectScoreboard == settings.CollectScoreboard &&
                   AllowProxy == settings.AllowProxy &&
                   CollectInvClick == settings.CollectInvClick &&
                   CollectChatClicks == settings.CollectChatClicks &&
                   CollectLobbyChanges == settings.CollectLobbyChanges &&
                   CollectEntities == settings.CollectEntities &&
                   ExtendDescriptions == settings.ExtendDescriptions &&
                   EqualityComparer<string[]>.Default.Equals(CommandPrefixes, settings.CommandPrefixes) &&
                   AutoStart == settings.AutoStart;
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
            hash.Add(CollectInvClick);
            hash.Add(CollectChatClicks);
            hash.Add(CollectLobbyChanges);
            hash.Add(CollectEntities);
            hash.Add(ExtendDescriptions);
            hash.Add(CommandPrefixes);
            hash.Add(AutoStart);
            return hash.ToHashCode();
        }

        public static PrivacySettings Default => new PrivacySettings()
        {
            CollectInventory = true,
            ExtendDescriptions = true,
            ChatRegex = "^(�r�eSell Offer|�r�6[Bazaar]|�r�cCancelled|�r�6Bazaar!|�r�eYou collected|�6[Auction]|�r�eBIN Auction started|�r�eYou �r�ccancelled|[Test]| - | + |Trade completed).*",
            CollectChat = true,
            CollectScoreboard = true,
            CollectChatClicks = true,
            CommandPrefixes = new string[] { "/cofl", "/colf", "/ch" },
            AutoStart = true
        };
    }
}