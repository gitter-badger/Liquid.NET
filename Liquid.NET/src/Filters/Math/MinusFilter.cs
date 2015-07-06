using Liquid.NET.Constants;
using Liquid.NET.Utils;

namespace Liquid.NET.Filters.Math
{
    public class MinusFilter : FilterExpression<NumericValue, NumericValue>
    {
        private readonly NumericValue _operand;

        public MinusFilter(NumericValue operand)
        {
            _operand = operand ?? new NumericValue(0);
        }

        public override LiquidExpressionResult ApplyTo(ITemplateContext ctx, NumericValue liquidExpression)
        {
            var result = liquidExpression.DecimalValue - _operand.DecimalValue;
            return MathHelper.GetReturnValue(result, liquidExpression, _operand);
        }

        public override LiquidExpressionResult ApplyToNil(ITemplateContext ctx)
        {
            return ApplyTo(ctx, new NumericValue(0));
        }
    }
}
