
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Coflnet.Sky.Core;
using System.Linq;
using Coflnet.Sky.Mayor.Client;
using Newtonsoft.Json;
using Coflnet.Sky.Mayor.Client.Model;

namespace Coflnet.Sky.Commands.Shared;
public class CurrentMayorDetailedFlipFilter : DetailedFlipFilter
{
    public object[] Options => new object[] {
            "Aatrox", "Cole", "Diana", "Diaz", "Foxy", "Finnegan", "Marina", "Paul", "Derpy", "Jerry", "Scorpius", "Dante", "Barry", "Aura"
            };

    public Expression<Func<FlipInstance, bool>> GetExpression(FilterContext filters, string val)
    {
        // normalize the name
        val = Options.FirstOrDefault(t => t.ToString().ToLower() == val.ToLower())?.ToString().ToLower();
        if (val == null)
            throw new CoflnetException("invalid_mayor", "The specified mayor does not exist");
        var current = GetCurrentMayor();
        return (f) => val == current();
    }

    public Func<string> GetCurrentMayor()
    {
        var current = TargetMayor(DiHandler.GetService<FilterStateService>().State);
        if (current == null)
            throw new CoflnetException("no_mayor", "Current mayor could not be retrieved");
        return current;
    }

    protected virtual Func<string> TargetMayor(FilterStateService.FilterState state)
    {
        return () => state.CurrentMayor;
    }

    public Filter.FilterType FilterType => Filter.FilterType.Equal;
}
