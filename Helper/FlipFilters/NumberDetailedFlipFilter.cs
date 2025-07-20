
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Coflnet.Sky.Filter;
using Coflnet.Sky.Core;
using System.Globalization;

namespace Coflnet.Sky.Commands.Shared
{
    public class NumberDetailedFlipFilter : DetailedFlipFilter
    {
        public virtual object[] Options => [1, 50_000_000_000];
        public virtual FilterType FilterType => FilterType.NUMERICAL | FilterType.RANGE;
        public virtual bool CanCache => true;
        public virtual Expression<Func<FlipInstance, bool>> GetExpression(FilterContext filters, string content)
        {
            var selector = GetSelector(filters);
            if (content.Contains("-") && (!content.StartsWith("-") || content.Count(c => c == '-') > 1))
            {
                var parts = content.Split(['-'], 2).Select(NumberParser.Double).ToArray();
                var min = parts[0];
                var max = parts[1];
                return ExpressionMinMax(selector, min, max);
            }
            var value = NumberParser.Double(content.Replace("<", "").Replace(">", ""));
            if (content.StartsWith("<"))
                return ExpressionMinMax(selector, 0, value - 0.0000001);
            if (content.StartsWith(">"))
            {
                return ExpressionMinMax(selector, value, int.MaxValue);
            }

            return ExpressionMinMax(selector, value, value);
            //return flip => flip.ProfitPercentage > min;
        }

        protected virtual Expression<Func<FlipInstance, double>> GetSelector(FilterContext filters)
        {
            return (f) => (double)f.Volume;
        }

        public static Expression<Func<T, bool>> ExpressionMinMax<T>(Expression<Func<T, double>> selector, double min, double max)
        {
            return Expression.Lambda<Func<T, bool>>(
                Expression.And(
                    Expression.GreaterThanOrEqual(
                    Expression.Constant(max, typeof(double)),
                    selector.Body),
                    Expression.GreaterThanOrEqual(
                    selector.Body,
                    Expression.Constant(min, typeof(double))
                )), selector.Parameters
            );
        }
    }


}