
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Coflnet.Sky.Filter;
using Coflnet.Sky.Core;
using System.Diagnostics;

namespace Coflnet.Sky.Commands.Shared
{
    public class FlipFilter
    {
        public static FilterEngine FilterEngine => DiHandler.GetService<FilterEngine>();

        private Func<SaveAuction, bool> Filters;
        private Func<FlipInstance, bool> FlipFilters = null;
        Expression<Func<FlipInstance, bool>> expression = null;
        public bool IsCacheAble { get; } = true;

        public static CamelCaseNameDictionary<DetailedFlipFilter> AdditionalFilters { private set; get; } = new();

        public static IEnumerable<string> AllFilters => FilterEngine.AvailableFilters.Select(f => f.Name).Concat(AdditionalFilters.Keys);
        public static string FilterForName = "ForTag";

        static FlipFilter()
        {
            AdditionalFilters.Add<ForTagDetailedFlipFilter>();
            AdditionalFilters.Add<VolumeDetailedFlipFilter>();
            AdditionalFilters.Add<ProfitDetailedFlipFilter>();
            AdditionalFilters.Add<ProfitPerUnitDetailedFlipFilter>();
            AdditionalFilters.Add<ProfitPercentageDetailedFlipFilter>();
            AdditionalFilters.Add<FlipFinderDetailedFlipFilter>();
            AdditionalFilters.Add<BedFlipDetailedFlipFilter>();
            AdditionalFilters.Add<PreApiDetailedFlipFilter>();
            AdditionalFilters.Add<PremPlusDetailedFlipFilter>();
            AdditionalFilters.Add<CurrentMayorDetailedFlipFilter>();
            AdditionalFilters.Add<LastMayorDetailedFlipFilter>();
            AdditionalFilters.Add<NextMayorDetailedFlipFilter>();
            AdditionalFilters.Add<ActivePerkDetailedFlipFilter>();
            AdditionalFilters.Add<NextPerkDetailedFlipFilter>();
            AdditionalFilters.Add<DoNotOpenDetailedFlipFilter>();
            AdditionalFilters.Add<MinProfitPercentageDetailedFlipFilter>();
            AdditionalFilters.Add<ItemCategoryDetailedFlipFilter>();
            AdditionalFilters.Add<AhCategoryDetailedFlipFilter>();
            AdditionalFilters.Add<IntroductionAgeDaysDetailedFlipFilter>();
            AdditionalFilters.Add<ArmorSetDetailedFlipFilter>();
            AdditionalFilters.Add<ArmorSetNoHelmetDetailedFlipFilter>();
            AdditionalFilters.Add<MinProfitDetailedFlipFilter>();
            AdditionalFilters.Add<MaxCostDetailedFlipFilter>();
            AdditionalFilters.Add<ReferenceAgeDetailedFlipFilter>();
            AdditionalFilters.Add<ForceBlacklistDetailedFlipFilter>();
            AdditionalFilters.Add<AfterMainFilterDetailedFlipFilter>();
            AdditionalFilters.Add<PriorityOpenDetailedFlipFilter>();
            AdditionalFilters.Add<UtcHourOfDayDetailedFlipFilter>();
            AdditionalFilters.Add<UtcDayOfWeekDetailedFlipFilter>();
            AdditionalFilters.Add<CurrentEventDetailedFlipFilter>();
            AdditionalFilters.Add<YearOfTheDetailedFlipFilter>();
            AdditionalFilters.Add<PerfectArmorTierDetailedFlipFilter>();
            AdditionalFilters.Add<RemoveAfterDetailedFlipFilter>();
            AdditionalFilters.Add<VolatilityDetailedFlipFilter>();
            AdditionalFilters.Add<ReferenceCountDetailedFlipFilter>();
            AdditionalFilters.Add<OldestReferenceAgeDetailedFlipFilter>();
            AdditionalFilters.Add<DoNotRelistDetailedFlipFilter>();
            AdditionalFilters.Add<ReduceTargetByDetailedFlipFilter>();
            AdditionalFilters.Add<RelistAtDetailedFlipFilter>();
            AdditionalFilters.Add<TargetPriceDetailedFlipFilter>();
            AdditionalFilters.Add<CapTargetPriceAtDetailedFlipFilter>();
            AdditionalFilters.Add<ListingSlotsLeft>();
            AdditionalFilters.Add<KeepOnImportDetailedFlipFilter>();
            AdditionalFilters.Add<AverageTimeToSellDetailedFlipFilter>();
            AdditionalFilters.Add<FraggedDetailedFlipFilter>();
            AdditionalFilters.Add<CraftCostWeightDetailedFlipFilter>();
            AdditionalFilters.Add<CleanCostDetailedFlipFilter>();
            AdditionalFilters.Add<LegacyReforgeDetailedFlipFilter>();
            AdditionalFilters.Add<HasUuuidFilter>();
            AdditionalFilters.Add<UserPremiumTier>();
            AdditionalFilters.Add<ConnectedMcUserDetailedFlipFilter>();
            AdditionalFilters.Add<PurseDetailedFlipFilter>();
            AdditionalFilters.Add<HasRuneDetailedFlipFilter>();
            AdditionalFilters.Add<IsFarmingItemDetailedFlipFilter>();
            AdditionalFilters.Add<EstProfitPerHourDetailedFlipFilter>();
            AdditionalFilters.Add<ScorpiusDaysDetailedFlipFilter>();
            AdditionalFilters.Add<DerpyDaysDetailedFlipFilter>();
            AdditionalFilters.Add<JerryDaysDetailedFlipFilter>();
        }

        public FlipFilter(Dictionary<string, string> originalf, IPlayerInfo playerInfo)
        {
            if (originalf == null || originalf.Count == 0)
            {
                expression = f => true;
                return;
            }
            var filters = new Dictionary<string, string>(originalf);
            var filtersToPass = new Dictionary<string, string>(filters);
            foreach (var item in AdditionalFilters.Keys)
            {
                var match = filters.Where(f => f.Key.ToLower() == item.ToLower()).FirstOrDefault();
                if (match.Key != default)
                {
                    try
                    {
                        filters.Remove(match.Key);
                        var context = new FilterContext(filtersToPass, playerInfo);
                        var newPart = AdditionalFilters[item].GetExpression(context, match.Value);
                        if (expression == null)
                            expression = newPart;
                        else if (newPart != null)
                            expression = newPart.And(expression);
                        if (AdditionalFilters[item] is NumberDetailedFlipFilter numFilter && !numFilter.CanCache)
                        {
                            IsCacheAble = false;
                        }
                    }
                    catch (CoflnetException)
                    {
                        throw; // forward error
                    }
                    catch (Exception e)
                    {
                        using var errorAct = DiHandler.GetService<ActivitySource>().StartActivity("error");
                        errorAct?.SetTag("error", "filter_parsing");
                        errorAct.AddEvent(new ActivityEvent("error", DateTimeOffset.UtcNow, new ActivityTagsCollection { { "error", e.Message } }));
                        Console.WriteLine($"{errorAct?.Id} {e}");
                        throw new CoflnetException("filter_parsing", $"Error in filter {item} with value {match.Value} : {e.Message}");
                    }
                }
            }
            var filterExpression = FilterEngine.GetMatchExpression(filters);
            Expression<Func<FlipInstance, SaveAuction>> flipToAuction = f => f.Auction;
            var invoke = Expression.Invoke(filterExpression, flipToAuction.Body);
            Expression<Func<FlipInstance, bool>> auctionMatcher = Expression.Lambda<Func<FlipInstance, bool>>(invoke, flipToAuction.Parameters[0]);

            if (auctionMatcher == null)
                throw new Exception("matcher is null");
            if (expression == null)
                expression = auctionMatcher;
            else
                expression = auctionMatcher.And(expression);
            Filters = FilterEngine.GetMatcher(filters);
            if (expression != null)
                FlipFilters = expression.Compile();
        }

        public static void CopyRelevantToNew(FlipSettings newSettings, FlipSettings old)
        {
            if (old == null)
                return;
            var onImportName = CamelCaseNameDictionary<DetailedFlipFilter>.GetCleardName<KeepOnImportDetailedFlipFilter>();
            CopyIfFlagged(old.BlackList, newSettings.BlackList);
            CopyIfFlagged(old.WhiteList, newSettings.WhiteList);

            void CopyIfFlagged(List<ListEntry> oldList, List<ListEntry> newList)
            {
                //remove all in newList
                foreach (var item in newList)
                {
                    if (item.filter?.All(f => f.Key != onImportName) ?? true)
                        continue;
                    item.filter.Remove(onImportName);
                }
                if (oldList == null)
                    return;
                foreach (var filter in oldList)
                {
                    if (filter?.filter?.All(f => f.Key != onImportName) ?? true)
                        continue;
                    newList.Add(filter);
                }
            }
        }

        public bool IsMatch(FlipInstance flip)
        {
            return Filters == null || Filters(flip.Auction) && (FlipFilters == null || FlipFilters(flip));
        }

        public Expression<Func<FlipInstance, bool>> GetExpression()
        {
            return expression;
        }
    }

}