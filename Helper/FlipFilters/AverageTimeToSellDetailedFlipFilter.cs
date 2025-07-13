
using System;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using Coflnet.Sky.Core;
using Coflnet.Sky.Filter;

namespace Coflnet.Sky.Commands.Shared;
[FilterDescription("average time to sell range, supports 1m,2h,3d,4w x-y <x. Eg. <4d (less than 4 days)")]
public partial class AverageTimeToSellDetailedFlipFilter : VolumeDetailedFlipFilter
{
    public override object[] Options => [];

    public override FilterType FilterType => FilterType.Equal | FilterType.RANGE;

    public override Expression<Func<FlipInstance, bool>> GetExpression(FilterContext filters, string content)
    {
        // replace each number group in content with Convert
        if (content.Contains('-'))
        {
            // switch positions of the two numbers
            content = string.Join('-', content.Split('-').Reverse());
        }
        if (content.StartsWith('<'))
            content = content.Replace('<', '>');
        else if (content.StartsWith('>'))
            content = content.Replace('>', '<');
        var convertedString = TimeSelect().Replace(content, (m) => ConvertToDay(m.Value).ToString(CultureInfo.InvariantCulture));
        return base.GetExpression(filters, convertedString);
    }


    private double ConvertToDay(string content)
    {
        // timespan fromat is 1d, 2h, 3m, 4w
        if (content.Length == 1)
        {
            throw new CoflnetException("invalid_unit", $"{content} is not enough. The last character needs to be one of m,h,d,w (minutes, hours, days, weeks)");
        }
        var number = double.Parse(content.Substring(0, content.Length - 1));
        var unit = content[content.Length - 1];
        switch (unit)
        {
            case 'm':
                return 1 / (number / 60 / 24);
            case 'h':
                return 1 / (number / 24);
            case 'd':
                return number;
            case 'w':
                return 1 / (number * 7);
        }
        throw new CoflnetException("invalid_unit", $"The last character needs to be one of m,h,d,w (minutes, hours, days, weeks)");
    }

    protected override Expression<Func<FlipInstance, double>> GetSelector(FilterContext filters)
    {
        return (f) => (double)GetVolume(f);
    }

    private static float GetVolume(FlipInstance f)
    {
        if (f.Context?.TryGetValue("minToSell", out var minToSell) ?? false)
        {
            var minutes = float.Parse(minToSell);
            return 1 / (minutes / 60 / 24);
        }
        return f.Volume;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"([\d+\.?\d?mhdw]+[^-]?)")]
    private static partial System.Text.RegularExpressions.Regex TimeSelect();
}
