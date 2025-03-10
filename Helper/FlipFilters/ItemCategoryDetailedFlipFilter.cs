
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
                service.GetItemCategory(itemCategory);
            }
            var tags = service.State.itemCategories[itemCategory];
            return flip => tags.Contains(flip.Auction.Tag);
        }
    }
}