
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Runtime.Serialization;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;
using System.Collections.Concurrent;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Coflnet.Sky.Commands.Shared
{
    [DataContract]
    public class FlipFilters
    {
        [DataMember(Name = "minProfit")]
        [SettingsDoc("Minimum profit of flips", "mp")]
        public long MinProfit;

        [DataMember(Name = "minProfitPercent")]
        [SettingsDoc("Minimum profit Percentage", "mpp")]
        public int MinProfitPercent;

        [SettingsDoc("The minimum sales per 24 hours (has decimals)", "mv")]
        [DataMember(Name = "minVolume")]
        public double MinVolume = 2;

        [SettingsDoc("Maximium cost of flips", "mc")]
        [DataMember(Name = "maxCost")]
        public long MaxCost;
    }


    [DataContract]
    public class FlipSettings : FlipFilters
    {
        [DataMember(Name = "filters")]
        public Dictionary<string, string> Filters;
        [DataMember(Name = "blacklist")]
        public List<ListEntry> BlackList;
        [DataMember(Name = "whitelist")]
        public List<ListEntry> WhiteList;

        [DataMember(Name = "lbin")]
        [SettingsDoc("Calculate profit based on lowest bin")]
        public bool BasedOnLBin;

        [DataMember(Name = "visibility")]
        public VisibilitySettings Visibility;

        [DataMember(Name = "mod")]
        public ModSettings ModSettings;

        [DataMember(Name = "finders")]
        [SettingsDoc("Which algorithms are selected for price estimation")]
        public LowPricedAuction.FinderType AllowedFinders;

        [DataMember(Name = "fastMode")]
        [SettingsDoc("Use the fast lane flips", true)]
        public bool FastMode;
        [DataMember(Name = "publishedAs")]
        [SettingsDoc("What this settings is published under", true)]
        public string PublishedAs;
        [DataMember(Name = "loadedVersion")]
        [SettingsDoc("What version of the settings is used", true)]
        public int UsedVersion;

        /// <summary>
        /// The initiating party that sent the change
        /// </summary>
        [DataMember(Name = "changer")]
        [SettingsDoc("The last changer of the settings", true)]
        public string Changer;
        [DataMember(Name = "onlyBin")]
        [SettingsDoc("Hide all auctions (not buy item now)")]
        public bool OnlyBin;
        [DataMember(Name = "whitelistAftermain")]
        [SettingsDoc("whitelisted items will only show if they also meet main filters (min profit etc)")]
        public bool WhitelistAfterMain;
        [DataMember(Name = "basedConfig")]
        [SettingsDoc("The config this settings is based on, loads in addition", true)]
        public string BasedConfig;

        private ListMatcher BlackListMatcher;
        private ListMatcher ForcedBlackListMatcher;
        private ListMatcher WhiteListMatcher;
        private ListMatcher AfterMainWhiteListMatcher;

        [SettingsDoc("Stop receiving any flips (just use other features) also stops the timer")]
        public bool DisableFlips;
        [SettingsDoc("Outputs more information to help with debugging issues")]
        public bool DebugMode;

        [DataMember(Name = "lastChange")]
        [SettingsDoc("", true)]
        public string LastChanged { get; set; }
        [DataMember(Name = "blockExport")]
        [SettingsDoc("Block exporting this flip to the public list", true)]
        public bool BlockExport;
        [DataMember(Name = "blockHighCompetition")]
        [SettingsDoc("Block flips that are probably not purchaseable manually")]
        public bool BlockHighCompetitionFlips;
        [IgnoreDataMember]
        public bool IsCompiled => BlackListMatcher != null && filterCompileLock.CurrentCount != 0;
        [IgnoreDataMember]
        public IPlayerInfo PlayerInfo;

        private SemaphoreSlim filterCompileLock = new SemaphoreSlim(1, 1);
        private CancellationTokenSource filterCompileCancel = new CancellationTokenSource();

        public void CancelCompilation()
        {
            filterCompileCancel.Cancel();
            filterCompileCancel = new CancellationTokenSource();
        }

        /// <summary>
        /// Determines if a flip matches a the <see cref="Filters"/> of this instance
        /// </summary>
        /// <param name="flip"></param>
        /// <returns>true if it matches</returns>
        public (bool, string) MatchesSettings(FlipInstance flip)
        {
            MakeSureMatchersAreInitialized();
            if (IsFinderBlocked(flip.Finder))
                return (false, "finder " + flip.Finder.ToString());

            if (OnlyBin && !flip.Auction.Bin)
                return (false, "not bin");

            if (BlackListMatcher == null)
                return (false, "filters currently compiling");

            try
            {
                var forceBlacklistMatch = ForcedBlackListMatcher.IsMatch(flip);
                if (forceBlacklistMatch.Item1)
                    return (false, "forced blacklist " + forceBlacklistMatch.Item2);
            }
            catch (System.Exception e)
            {
                LogError(flip, "forced blacklist", ForcedBlackListMatcher, e);
            }
            try
            {
                var match = WhiteListMatcher.IsMatch(flip);
                if (match.Item1)
                {
                    if ((flip.Context?.TryGetValue("target", out string stringVal) ?? false) && double.TryParse(stringVal, out double target))
                        flip.Target = (long)target;
                    return (true, "whitelist " + match.Item2);
                }
            }
            catch (System.Exception e)
            {
                LogError(flip, "whitelist ", WhiteListMatcher, e);
            }

            var main = MainSettingsMatch(flip);
            if (!main.Item1)
                return main;

            var awmatch = AfterMainWhiteListMatcher?.IsMatch(flip);
            if (awmatch.HasValue && awmatch.Value.Item1)
                return (true, "whitelist am " + awmatch.Value.Item2);

            try
            {
                var match = BlackListMatcher.IsMatch(flip);
                if (match.Item1)
                    return (false, "blacklist " + match.Item2);
            }
            catch (System.Exception e)
            {
                LogError(flip, "blacklist ", BlackListMatcher, e);
            }

            return (true, "general filter");
        }

        private static void LogError(FlipInstance flip, string title, ListMatcher matcher, Exception e)
        {
            Activity.Current?.AddTag("flip", JSON.Stringify(flip));
            Activity.Current?.AddTag("filter", JSON.Stringify(matcher?.FullList?.Where(x => x != null).Select(x => new { x.ItemTag, x.filter })));
            throw new Exception("Error while matching " + title + " " + e?.Message, e);
        }

        private void MakeSureMatchersAreInitialized()
        {
            if (BlackListMatcher != null && WhiteListMatcher != null)
                return;
            // return if already compiling
            if (!filterCompileLock.Wait(0) || filterCompileCancel.Token.IsCancellationRequested)
            {
                return;
            }
            try
            {
                InitializeMatchers();
            }
            finally
            {
                filterCompileLock.Release();
            }
        }

        private void InitializeMatchers()
        {
            Activity.Current.Log("initializing filter matcher");
            var token = filterCompileCancel.Token;
            if (ForcedBlackListMatcher == null && !token.IsCancellationRequested)
                ForcedBlackListMatcher = new ListMatcher(GetForceBlacklist(), PlayerInfo, this, token);
            Activity.Current.Log("initialized force blacklist");
            if (WhiteListMatcher == null && !token.IsCancellationRequested)
                WhiteListMatcher = new ListMatcher(WhiteList?.Except(GetAfterMainWhitelist()).ToList(), PlayerInfo, this, token);
            Activity.Current.Log("initialized whitelist matcher");
            if (AfterMainWhiteListMatcher == null && !token.IsCancellationRequested)
                AfterMainWhiteListMatcher = new ListMatcher(GetAfterMainWhitelist(), PlayerInfo, this, token);
            Activity.Current.Log("initialized after main whitelist matcher");
            if (BlackListMatcher == null && !token.IsCancellationRequested)
                BlackListMatcher = new ListMatcher(BlackList?.Except(GetForceBlacklist()).ToList(), PlayerInfo, this, token);
            Activity.Current.Log("initialized blacklist matcher");
        }

        private (bool, string) MainSettingsMatch(FlipInstance flip)
        {
            if (flip.Volume < MinVolume)
                return (false, "minVolume");
            if (MaxCost != 0 && flip.LastKnownCost > MaxCost)
                return (false, "maxCost");
            if (flip.Profit < MinProfit)
                return (false, "minProfit");
            if (flip.LastKnownCost > 0 && (flip.ProfitPercentage < MinProfitPercent
                || BasedOnLBin && (flip.ProfitPercentage < MinProfitPercent)))
            {
                return (false, "profit Percentage");
            }
            if (flip.Auction == null)
                return (false, "auction not set");
            return (true, null);
        }

        public void CopyListMatchers(FlipSettings other)
        {
            if (other == null)
                return;
            if (other.ForcedBlackListMatcher != null && other.BlackList?.SequenceEqual(BlackList) == true)
                ForcedBlackListMatcher = other.ForcedBlackListMatcher;
            if (other.WhiteListMatcher != null && other.WhiteList?.SequenceEqual(WhiteList) == true)
                WhiteListMatcher = other.WhiteListMatcher;
            if (other.AfterMainWhiteListMatcher != null && other.WhiteList?.SequenceEqual(WhiteList) == true)
                AfterMainWhiteListMatcher = other.AfterMainWhiteListMatcher;
            if (other.BlackListMatcher != null && other.BlackList?.SequenceEqual(BlackList) == true)
                BlackListMatcher = other.BlackListMatcher;
        }

        public void RecompileMatchers()
        {
            ClearListMatchers();
            MakeSureMatchersAreInitialized();
        }

        public void ClearListMatchers()
        {
            ForcedBlackListMatcher = null;
            WhiteListMatcher = null;
            AfterMainWhiteListMatcher = null;
            BlackListMatcher = null;
        }

        public List<ListEntry> GetForceBlacklist()
        {
            return BlackList?.Where(b => b.filter?.Where(f => f.Key == "ForceBlacklist").Any() ?? false).ToList();
        }
        public List<ListEntry> GetAfterMainWhitelist()
        {
            return WhiteList?.Where(b => WhitelistAfterMain || (b.filter?.Where(f => f.Key == "AfterMainFilter").Any() ?? false)).ToList();
        }

        public bool IsFinderBlocked(LowPricedAuction.FinderType finder)
        {
            return finder == LowPricedAuction.FinderType.UNKOWN ||
                    AllowedFinders == LowPricedAuction.FinderType.UNKOWN &&
                        !(LowPricedAuction.FinderType.SNIPERS).HasFlag(finder)
                                                || AllowedFinders != LowPricedAuction.FinderType.UNKOWN && !AllowedFinders.HasFlag(finder)
                                                && (int)finder != 3;
        }

        /// <summary>
        /// Calculates the displayed price and profit
        /// </summary>
        /// <param name="flip"></param>
        /// <param name="targetPrice"></param>
        /// <param name="profit"></param>
        public void GetPrice(FlipInstance flip, out long targetPrice, out long profit)
        {
            targetPrice = (BasedOnLBin || flip.Finder == LowPricedAuction.FinderType.SNIPER ? (flip.LowestBin ?? 0) : flip.MedianPrice);
            if (targetPrice > 1_000_000)
                profit = targetPrice * 98 / 100 - flip.LastKnownCost;
            else
                profit = targetPrice * 99 / 100 - flip.LastKnownCost;
        }

        public override bool Equals(object obj)
        {
            return obj is FlipSettings settings &&
                   EqualityComparer<Dictionary<string, string>>.Default.Equals(Filters, settings.Filters) &&
                   EqualityComparer<List<ListEntry>>.Default.Equals(BlackList, settings.BlackList) &&
                   EqualityComparer<List<ListEntry>>.Default.Equals(WhiteList, settings.WhiteList) &&
                   BasedOnLBin == settings.BasedOnLBin &&
                   MinProfit == settings.MinProfit &&
                   MinProfitPercent == settings.MinProfitPercent &&
                   MinVolume == settings.MinVolume &&
                   MaxCost == settings.MaxCost &&
                   EqualityComparer<VisibilitySettings>.Default.Equals(Visibility, settings.Visibility) &&
                   EqualityComparer<ModSettings>.Default.Equals(ModSettings, settings.ModSettings) &&
                   AllowedFinders == settings.AllowedFinders &&
                   FastMode == settings.FastMode &&
                   Changer == settings.Changer &&
                   OnlyBin == settings.OnlyBin &&
                   EqualityComparer<ListMatcher>.Default.Equals(BlackListMatcher, settings.BlackListMatcher) &&
                   EqualityComparer<ListMatcher>.Default.Equals(WhiteListMatcher, settings.WhiteListMatcher);
        }

        public override int GetHashCode()
        {
            HashCode hash = new HashCode();
            hash.Add(Filters);
            hash.Add(BlackList);
            hash.Add(WhiteList);
            hash.Add(BasedOnLBin);
            hash.Add(MinProfit);
            hash.Add(MinProfitPercent);
            hash.Add(MinVolume);
            hash.Add(MaxCost);
            hash.Add(Visibility);
            hash.Add(ModSettings);
            hash.Add(AllowedFinders);
            hash.Add(FastMode);
            hash.Add(Changer);
            hash.Add(OnlyBin);
            hash.Add(BlackListMatcher);
            hash.Add(WhiteListMatcher);
            return hash.ToHashCode();
        }

        public class ListMatcher
        {
            public List<ListEntry> FullList { get; }
            private HashSet<string> Ids = new HashSet<string>();
            private HashSet<string> Sellers = new HashSet<string>();
            private List<ListEntry> RemainingFilters = new List<ListEntry>();
            Dictionary<string, Func<FlipInstance, bool>> Matchers = [];
            private static ConcurrentDictionary<string, CacheEntry> matcherLookup = new();
            public class CacheEntry
            {
                public Func<FlipInstance, bool> matcher;
                public DateTime lastUsed;
            }

            private string GetCacheKey(List<ListEntry> FullList)
            {
                return FullList?.Select(x => "f:" + x.filter.OrderBy(f => f.Key).Select(f => f.Key + "=" + f.Value).Aggregate((a, b) => a + b) + (x.Disabled ? "disabled" : ""))
                            .Aggregate((a, b) => a + b) ?? "";
            }

            public ListMatcher(List<ListEntry> BlackList, IPlayerInfo playerInfo, FlipSettings settings, CancellationToken token)
            {
                if (BlackList == null || BlackList.Count == 0)
                    return;
                this.FullList = new(BlackList.Select(b => b.Clone()));
                foreach (var item in FullList.ToList())
                {
                    if (item.filter == null || item.filter.Count == 0)
                        continue;
                    foreach (var filter in item.filter)
                    {
                        if (!filter.Value.Contains("{{"))
                            continue;
                        item.filter[filter.Key] = ExpressionParser.Evaluate(filter.Value, settings);
                    }
                }
                ConcurrentDictionary<string, List<ListEntry>> forTags = ExtractFiltersForTags();
                foreach (var item in FullList.ToList())
                {
                    // preprocessing
                    AddFiltersBasedOnTags(forTags, item);
                    ExpandFilters(FullList, item);
                }
                foreach (var item in FullList.ToList())
                {
                    AddElement(item);
                }
                foreach (var item in RemainingFilters.GroupBy(g => GetGroupKey(g)))
                {
                    var cacheKey = GetCacheKey(item.ToList());
                    if (matcherLookup.TryGetValue(cacheKey, out var cache))
                    {
                        Addmatcher(item.Key, cache.matcher);
                        cache.lastUsed = DateTime.Now;
                        continue;
                    }
                    ConcurrentDictionary<string, Expression<Func<FlipInstance, bool>>> isMatch = new();
                    var cacheAble = true;
                    foreach (var element in item)
                    {
                        try
                        {
                            isMatch.AddOrUpdate(item.Key, element.GetExpression(playerInfo), (k, old) => old.Or(element.GetExpression(playerInfo)));
                            if (!element.IsCacheAble())
                            {
                                cacheAble = false;
                            }
                        }
                        catch (Exception e)
                        {
                            DiHandler.GetService<ILogger<FlipFilters>>()?.LogError(e, "Error while compiling filter {filter}", element.DisplayName ?? element.ItemTag);
                            throw new CoflnetException("compile_error", $"Error for filter {element.DisplayName ?? element.ItemTag}: " + e.Message);
                        }
                    }
                    var matcher = isMatch[item.Key].Compile();
                    Addmatcher(item.Key, matcher);
                    if (!cacheKey.Contains(nameof(UserPremiumTier), StringComparison.CurrentCultureIgnoreCase)
                        && !cacheKey.Contains(nameof(ConnectedMcUserDetailedFlipFilter).Replace(nameof(DetailedFlipFilter), ""), StringComparison.CurrentCultureIgnoreCase)
                        && !cacheKey.Contains(nameof(ListingSlotsLeft), StringComparison.CurrentCultureIgnoreCase)
                        && cacheAble)
                        matcherLookup.TryAdd(cacheKey, new CacheEntry { matcher = matcher, lastUsed = DateTime.Now });
                }
                if (matcherLookup.Count > 10_000)
                {
                    var toRemove = matcherLookup.OrderBy(x => x.Value.lastUsed).Take(1000).Select(x => x.Key).ToList();
                    foreach (var key in toRemove)
                    {
                        matcherLookup.TryRemove(key, out _);
                    }
                }
                return;
            }

            /// <summary>
            /// Expands group filters like ArmorSet into single filters that can be handled via lookup
            /// </summary>
            /// <param name="fullList"></param>
            /// <param name="item"></param>
            /// <exception cref="NotImplementedException"></exception>
            private void ExpandFilters(List<ListEntry> fullList, ListEntry item)
            {
                if (item.filter == null || !item.filter.Any(f => f.Key == "ArmorSet" && !ArmorSetDetailedFlipFilter.ExtraArmorSets.ContainsKey(f.Value.ToLower())))
                    return;
                FullList.Remove(item);
                string[] parts = ["Helmet", "Chestplate", "Leggings", "Boots"];
                var armorSet = item.filter.Where(f => f.Key == "ArmorSet").Select(f => f.Value).FirstOrDefault();
                foreach (var part in parts)
                {
                    var clone = item.Clone();
                    clone.filter.Remove("ArmorSet");
                    clone.ItemTag = (armorSet + "_" + part).ToUpper();
                    fullList.Add(clone);
                }
            }

            private void Addmatcher(string key, Func<FlipInstance, bool> compiled)
            {
                Matchers[key] = compiled;
                if (key != string.Empty)
                    Matchers["STARRED_" + key] = compiled;
            }

            private ConcurrentDictionary<string, List<ListEntry>> ExtractFiltersForTags()
            {
                var forTags = new ConcurrentDictionary<string, List<ListEntry>>();
                foreach (var item in FullList.Where(b => b.filter != null && b.filter.Any(f => f.Key == FlipFilter.FilterForName)).ToList())
                {
                    var tags = item.filter.Where(f => f.Key == FlipFilter.FilterForName).Select(f => f.Value).First().Split(',');
                    foreach (var tag in tags)
                    {
                        var element = forTags.GetOrAdd(tag, new List<ListEntry>());
                        element.Add(item);
                    }
                    FullList.Remove(item);
                }

                return forTags;
            }

            /// <summary>
            /// Meta filter "forTag" can add filter elements
            /// </summary>
            /// <param name="forTags"></param>
            /// <param name="item"></param>
            private static void AddFiltersBasedOnTags(ConcurrentDictionary<string, List<ListEntry>> forTags, ListEntry item)
            {
                if (item.Tags == null || !item.Tags.Any())
                {
                    return;
                }
                foreach (var tag in item.Tags)
                {
                    if (!forTags.TryGetValue(tag, out var list))
                        continue;
                    if (item.filter == null)
                        item.filter = new();
                    foreach (var element in list)
                    {
                        foreach (var filter in element.filter)
                        {
                            if (item.filter.Any(f => f.Key == filter.Key) || filter.Key == FlipFilter.FilterForName)
                                continue;
                            item.filter.Add(filter.Key, filter.Value);
                        }
                    }
                }
            }

            private static string GetGroupKey(ListEntry entry)
            {
                if (entry.ItemTag != null)
                    return entry.ItemTag.Replace("STARRED_", "");
                if (entry.filter != null && entry.filter.Count > 0 && entry.filter.Any(f => f.Key.Contains("Color")))
                    return "color";
                if (entry.filter != null && entry.filter.Count > 0 && entry.filter.Any(f => f.Key.StartsWith("Pet") || f.Key == "ItemCategory" && f.Value.Equals("PET", StringComparison.CurrentCultureIgnoreCase)))
                    return "pets";
                return string.Empty;
            }

            private void AddElement(ListEntry item)
            {
                if (item.filter == null || item.filter.Count == 0 || (item.filter.Count == 1 && item.filter.First().Key == "ForceBlacklist"))
                {
                    Ids.Add(item.ItemTag);
                    return;
                }
                if (item.ItemTag == null && item.filter != null)
                {
                    var seller = item.filter.Where(f => f.Key.Equals("seller", StringComparison.CurrentCultureIgnoreCase)).Select(f => f.Value).FirstOrDefault();
                    var other = item.filter.Where(f => !f.Key.Equals("ForceBlacklist", StringComparison.CurrentCultureIgnoreCase)).Count();
                    if (other == 1 && seller != null)
                    {
                        Sellers.Add(seller);
                        return;
                    }
                }
                RemainingFilters.Add(item);
            }

            public (bool, string) IsMatch(FlipInstance flip)
            {
                if (Ids.Contains(flip.Auction.Tag))
                    return (true, "for " + flip.Auction.Tag);
                if (Sellers.Contains(flip.Auction.AuctioneerId))
                    return (true, "for " + flip.Auction.AuctioneerId);

                if (flip.Auction.Tag != null && Matchers.TryGetValue(flip.Auction.Tag, out Func<FlipInstance, bool> matcher) && matcher(flip))
                    return (true, "matched filter for item");
                // general filters without a tag
                if (flip.Auction.FlatenedNBT.ContainsKey("color") && Matchers.TryGetValue("color", out matcher) && matcher(flip))
                    return (true, "matched color filter");
                if (flip.Auction.Tag != null && flip.Auction.Tag.StartsWith("PET_") && Matchers.TryGetValue("pets", out matcher) && matcher(flip))
                    return (true, "matched pet filter");
                if (Matchers.TryGetValue(string.Empty, out matcher) && matcher(flip))
                    return (true, "matched general filter");
                /*foreach (var item in RemainingFilters)
                {
                    if (item.MatchesSettings(flip))
                        return (true, $"filter for {item.filter.Keys.First()}: {item.filter.Values.First()}");
                }*/
                return (false, "no match");
            }
        }

    }

}


