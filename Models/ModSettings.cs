using System.Runtime.Serialization;

namespace Coflnet.Sky.Commands.Shared
{
    [DataContract]
    public class ModSettings
    {
        /// <summary>
        /// Display only the profit instead of cost and median
        /// </summary>
        [DataMember(Name = "justProfit")]
        [SettingsDoc("Display just the profit")]
        public bool DisplayJustProfit;
        /// <summary>
        /// Play a sound when a flip message is sent
        /// </summary>
        [DataMember(Name = "soundOnFlip")]
        [SettingsDoc("Play a sound when a flip is received")]
        public bool PlaySoundOnFlip;
        /// <summary>
        /// Play a sound when a flip message is sent
        /// </summary>
        [DataMember(Name = "soundOnOutbid")]
        [SettingsDoc("Play a sound when a bazaar order is outbid")]
        public bool PlaySoundOnOutbid;
        [DataMember(Name = "blockOutbidMsg")]
        [SettingsDoc("Block outbid messages from appearing in chat")]
        public bool BlockOutbidMessages;
        /// <summary>
        /// Use M and k to shorten larger numbers
        /// </summary>
        [DataMember(Name = "shortNumbers")]
        [SettingsDoc("Use M and k to shorten numbers", "sn")]
        public bool ShortNumbers;
        [DataMember(Name = "shortNames")]
        [SettingsDoc("Remove reforges etc from item names")]
        public bool ShortNames;
        /// <summary>
        /// Block "flips in 10 seconds" from appearing
        /// </summary>
        [DataMember(Name = "blockTenSecMsg")]
        [SettingsDoc("Hide the flips in 10 seconds message")]
        public bool BlockTenSecondsMsg;
        [DataMember(Name = "format")]
        [SettingsDoc("Custom flip message format")]
        public string Format;
        [DataMember(Name = "blockedFormat")]
        [SettingsDoc("Custom flip message format for blocked flips")]
        public string BlockedFormat;
        [DataMember(Name = "chat")]
        [SettingsDoc("Is the chat enabled")]
        public bool Chat;
        /// <summary>
        /// Should a countdown be displayed till the update
        /// </summary>
        /// <value></value>
        [DataMember(Name = "countdown")]
        [SettingsDoc("Show the timer")]
        public bool DisplayTimer;
        [DataMember(Name = "hideNoBestFlip")]
        [SettingsDoc("Hides the message from the hotkey")]
        public bool HideNoBestFlip;
        [DataMember(Name = "timerX")]
        [SettingsDoc("<---> position in percent")]
        public int TimerX;
        [DataMember(Name = "timerY")]
        [SettingsDoc("up/down position in percent")]
        public int TimerY;
        [DataMember(Name = "timerSeconds")]
        [SettingsDoc("how many seconds before the update the timer should be shown")]
        public int TimerSeconds;
        [DataMember(Name = "timerScale")]
        [SettingsDoc("What scale the timer should be displayed with")]
        public float TimerScale;
        [DataMember(Name = "timerPrefix")]
        [SettingsDoc("Custom text to put in front of the timer")]
        public string TimerPrefix;
        [DataMember(Name = "timerPrecision")]
        [SettingsDoc("How many digits the timer should target (3)")]
        public int TimerPrecision;
        [DataMember(Name = "blockedMsg")]
        [SettingsDoc("How many minutes to have pass before showing the x amounts of flips blocked message again, max is 127", false, "minBetweenBlocked")]
        public sbyte MinutesBetweenBlocked;

        [DataMember(Name = "maxPercentOfPurse")]
        [SettingsDoc("The maximum amount of your purse you are willing to spend on a single flip")]
        public short MaxPercentOfPurse;
        [DataMember(Name = "noBedDelay")]
        [SettingsDoc("Don't delay bed flips, send them imediately instead", "nbd")]
        public bool NoBedDelay;
        [DataMember(Name = "streamerMode")]
        [SettingsDoc("Hide any personal data and reduce sounds")]
        public bool StreamerMode;
        [DataMember(Name = "autoStartFlipper")]
        [SettingsDoc("Start showing flips automatically when joining skyblock", false, "fas")]
        public bool AutoStartFlipper;
        [DataMember(Name = "normalSoldFlips")]
        [SettingsDoc("Don't add [SOLD] to sold flips, send them normally instead")]
        public bool NormalSoldFlips;
        [SettingsDoc("Autmatically add items to the blacklist for 8 hours if they show up more than 5 times in 2 minutes")]
        [DataMember(Name = "tempBlacklistSpam")]
        public bool TempBlacklistSpam;
        [SettingsDoc("don't show flips only add useful data")]
        [DataMember(Name = "dataOnlyMode")]
        public bool AhDataOnlyMode;
        [SettingsDoc("Ah list time target in hours", false, "listHours")]
        [DataMember(Name = "ahListHours")]
        public int AhListTimeTarget;
        [SettingsDoc("Sell items as fast as possible", false, "quicksell")]
        [DataMember(Name = "quickSell")]
        public bool QuickSell;
        [SettingsDoc("The maximum amount of flips to buy and store in inventory", "maxItemsInInventory")]
        [DataMember(Name = "maxItemsInInventory")]
        public int MaxFlipItemsInInventory;
        [SettingsDoc($"Disables spam protection. By default only ~5 most valuable flips are shown that fit the settings. CAUTION: This can lead to spam flips")]
        [DataMember(Name = "disableSpamProtection")]
        public bool DisableSpamProtection;
        [SettingsDoc("Purchasing more than this percenatage of flips on an item will temp blacklist the item, eg if you see 8 and buy 4 the rate is 50", "maxFlipPercentage")]
        [DataMember(Name = "tempBlacklistThreshold")]
        public int TempBlacklistThreshold = 20;
        [DataMember(Name = "useSellerProfileButton")]
        [SettingsDoc("Open the sellers profile instead of their auction house when clicking their name on a flip")]
        public bool UseSellProfileButton;
    }
}