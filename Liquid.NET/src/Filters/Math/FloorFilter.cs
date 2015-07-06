using Liquid.NET.Constants;
using Liquid.NET.Utils;

namespace Liquid.NET.Filters.Math
{
    public class FloorFilter : FilterExpression<NumericValue, NumericValue>
    {

        public override LiquidExpressionResult ApplyTo(ITemplateContext ctx, NumericValue val)
        {
            var floor = (int) System.Math.Floor(val.DecimalValue);
            return LiquidExpressionResult.Success(new NumericValue(floor));
        }

        public override LiquidExpressionResult ApplyToNil(ITemplateContext ctx)
        {
            return ApplyTo(ctx, new NumericValue(0));
        }
    }
}
