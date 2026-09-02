using System;

namespace Coflnet.Sky.Commands.Shared;

public class LastMayorDetailedFlipFilter : CurrentMayorDetailedFlipFilter
{
    protected override Func<string> TargetMayor(FilterStateService.FilterState state)
    {
        return () => state.PreviousMayor;
    }
}
