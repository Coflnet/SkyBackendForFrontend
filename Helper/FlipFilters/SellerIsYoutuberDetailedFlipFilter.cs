
using System;
using System.Linq.Expressions;

namespace Coflnet.Sky.Commands.Shared;

public class SellerIsYoutuberDetailedFlipFilter : DetailedFlipFilter
{
    public object[] Options => ["Yes"];

    public Filter.FilterType FilterType => Filter.FilterType.Equal;

    public Expression<Func<FlipInstance, bool>> GetExpression(FilterContext filters, string val)
    {
        var service = DiHandler.GetService<FilterStateService>(); ;
        return f => service.State.KnownYoutuberUuids.Contains(f.Auction.AuctioneerId);
    }
}
