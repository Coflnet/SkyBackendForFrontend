using System;

namespace Coflnet.Sky.Commands.Shared;

public class NextMayorDetailedFlipFilter : CurrentMayorDetailedFlipFilter
{
    protected override Func<string> TargetMayor(FilterStateService.FilterState state)
    {
        return () => state.NextMayor;
    }
}
