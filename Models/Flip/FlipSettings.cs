
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using hypixel;
using Newtonsoft.Json;

namespace Coflnet.Sky.Commands.Shared
{

    [DataContract]
    public class FlipSettings
    {
        [DataMember(Name = "filters")]
        public Dictionary<string, string> Filters;
        [DataMember(Name = "blacklist")]
        public List<ListEntry> BlackList;
        [DataMember(Name = "whitelist")]
        public List<ListEntry> WhiteList;

        [DataMember(Name = "lbin")]
        public bool BasedOnLBin;

        [DataMember(Name = "minProfit")]
        public int MinProfit;

        [DataMember(Name = "minProfitPercent")]
        public int MinProfitPercent;

        [DataMember(Name = "minVolume")]
        public int MinVolume;

        [DataMember(Name = "maxCost")]
        public int MaxCost;

        [DataMember(Name = "visibility")]
        public VisibilitySettings Visibility;

        [DataMember(Name = "mod")]
        public ModSettings ModSettings;

        [DataMember(Name = "finders")]
        public LowPricedAuction.FinderType AllowedFinders;

        [DataMember(Name = "fastMode")]
        public bool FastMode;

        /// <summary>
        /// The initiating party that sent the change
        /// </summary>
        [DataMember(Name = "changer")]
        public string Changer;


        private FlipFilter filter;
        private List<FlipFilter> blackListFilters;
        private ListMatcher BlackListMatcher;
        private ListMatcher WhiteListMatcher;

        /// <summary>
        /// Determines if a flip matches a the <see cref="Filters"/>> of this instance
        /// </summary>
        /// <param name="flip"></param>
        /// <returns>true if it matches</returns>
        public (bool, string) MatchesSettings(FlipInstance flip)
        {
            GetPrice(flip, out long targetPrice, out long profit);
            if (AllowedFinders != LowPricedAuction.FinderType.UNKOWN && flip.Finder != LowPricedAuction.FinderType.UNKOWN
                                    && !AllowedFinders.HasFlag(flip.Finder)
                                    && (int)flip.Finder != 3)
                return (false,"finder " + flip.Finder.ToString());
            
            if (profit < MinProfit)
                return (false, "minProfit");
            if (flip.Volume < MinVolume)
                return (false, "minVolume");
            if (MaxCost != 0 && flip.LastKnownCost > MaxCost)
                return (false, "maxCost");
            if (flip.LastKnownCost > 0 && (profit * 100 / flip.LastKnownCost) < MinProfitPercent)
            {
                return (false, "profit Percentage");
            }
            if (flip.Auction == null)
                return (false, "auction not set");

            if (WhiteList != null)
            {
                if (WhiteListMatcher == null)
                    WhiteListMatcher = new ListMatcher(WhiteList);
                var match = WhiteListMatcher.IsMatch(flip);
                if (match.Item1)
                    return (true, "whitelist " + match.Item2);
            }

            if (BlackList != null)
            {
                if (BlackListMatcher == null)
                    BlackListMatcher = new ListMatcher(BlackList);
                var match = BlackListMatcher.IsMatch(flip);
                if (match.Item1)
                    return (false, "blacklist " + match.Item2);
            }

            if (filter == null)
                filter = new FlipFilter(this.Filters);
            return (filter.IsMatch(flip), "general filter");
        }

        /// <summary>
        /// Calculates the displayed price and profit
        /// </summary>
        /// <param name="flip"></param>
        /// <param name="targetPrice"></param>
        /// <param name="profit"></param>
        public void GetPrice(FlipInstance flip, out long targetPrice, out long profit)
        {
            targetPrice = (BasedOnLBin ? (flip.LowestBin ?? 0) : flip.MedianPrice);
            profit = targetPrice * 98 / 100 - flip.LastKnownCost;
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
                   EqualityComparer<FlipFilter>.Default.Equals(filter, settings.filter) &&
                   EqualityComparer<List<FlipFilter>>.Default.Equals(blackListFilters, settings.blackListFilters) &&
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
            hash.Add(filter);
            hash.Add(blackListFilters);
            hash.Add(BlackListMatcher);
            hash.Add(WhiteListMatcher);
            return hash.ToHashCode();
        }

        public class ListMatcher
        {
            private HashSet<string> Ids = new HashSet<string>();
            private List<ListEntry> RemainingFilters = new List<ListEntry>();

            public ListMatcher(List<ListEntry> BlackList)
            {
                foreach (var item in BlackList)
                {
                    if (item.filter == null || item.filter.Count == 0)
                        Ids.Add(item.ItemTag);
                    else
                        RemainingFilters.Add(item);
                }
            }

            public (bool, string) IsMatch(FlipInstance flip)
            {
                if (Ids.Contains(flip.Tag))
                    return (true, "for " + flip.Tag);
                foreach (var item in RemainingFilters)
                {
                    if (item.MatchesSettings(flip))
                        return (true, $"filter for {item.filter.Keys.First()}: {item.filter.Values.First()}");
                }
                return (false, "no match");
            }
        }

        
    }

}
