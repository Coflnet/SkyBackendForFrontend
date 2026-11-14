
using System;
using System.Linq.Expressions;
using System.Linq;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;
using Coflnet.Sky.Items.Client.Model;
using System.Diagnostics;

namespace Coflnet.Sky.Commands.Shared
{
    public class ItemCategoryDetailedFlipFilter : DetailedFlipFilter
    {
        public object[] Options => Enum.GetValues<ItemCategory>().Select(t => (object)t).ToArray();

        public FilterType FilterType => FilterType.Equal;

        public Expression<Func<FlipInstance, bool>> GetExpression(FilterContext filters, string val)
        {
            if (!Enum.TryParse<ItemCategory>(val, true, out ItemCategory itemCategory))
                throw new CoflnetException("invalid_category", $"the specified category {val} does not exist");

            var service = DiHandler.GetService<FilterStateService>();
            if (!service.State.itemCategories.ContainsKey(itemCategory))
            {
                Activity.Current.Log($"Got itemCategory {itemCategory}, updating cache");
                service.GetItemCategory(itemCategory).Wait();
            }
            // resolve the set on every evaluation, refreshes publish a new set instance
            var categories = service.State.itemCategories;
            return flip => InCategory(categories, itemCategory, flip.Auction.Tag);
        }

        private static bool InCategory(System.Collections.Concurrent.ConcurrentDictionary<ItemCategory, System.Collections.Generic.HashSet<string>> categories, ItemCategory category, string tag)
        {
            return categories.TryGetValue(category, out var tags) && tags.Contains(tag);
        }
    }
}
