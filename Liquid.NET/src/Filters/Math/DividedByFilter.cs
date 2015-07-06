using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Liquid.NET.Constants;
using Liquid.NET.Utils;

namespace Liquid.NET.Filters.Math
{
    public class DividedByFilter : FilterExpression<NumericValue, NumericValue>
    {        
        private readonly NumericValue _divisor;

        public DividedByFilter(NumericValue divisor)
        {
            _divisor = divisor ?? new NumericValue(0);
        }

        public override LiquidExpressionResult ApplyTo(ITemplateContext ctx, NumericValue dividend)
        {
            if (dividend == null)
            {
                dividend = new NumericValue(0);
            }
            if (_divisor == null)
            {
                return LiquidExpressionResult.Error("Liquid error: divided by 0");
            }
            if (_divisor.DecimalValue == 0.0m)
            {
                return LiquidExpressionResult.Error("Liquid error: divided by 0");
            }
            var result = dividend.DecimalValue / _divisor.DecimalValue;
            return MathHelper.GetReturnValue(result, dividend, _divisor);
        }

        public override LiquidExpressionResult ApplyToNil(ITemplateContext ctx)
        {
            return ApplyTo(ctx, new NumericValue(0));
        }
    }

}
