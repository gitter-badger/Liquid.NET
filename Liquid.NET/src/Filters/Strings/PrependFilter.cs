using System;

using Liquid.NET.Constants;
using Liquid.NET.Utils;

namespace Liquid.NET.Filters.Strings
{
    public class PrependFilter : FilterExpression<IExpressionConstant, StringValue>
    {
        private readonly StringValue _prependedStr;

        public PrependFilter(StringValue prependedStr)
        {
            _prependedStr = prependedStr ?? new StringValue(""); ;
        }

        public override LiquidExpressionResult ApplyTo(ITemplateContext ctx, IExpressionConstant liquidExpression)
        {
            return LiquidExpressionResult.Success(StringUtils.Eval(liquidExpression, x => _prependedStr.StringVal + x));
        }
    }
}
