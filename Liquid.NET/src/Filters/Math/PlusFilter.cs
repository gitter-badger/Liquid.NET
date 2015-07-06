﻿using System;
using Antlr4.Runtime.Misc;
using Liquid.NET.Constants;
using Liquid.NET.Utils;

namespace Liquid.NET.Filters.Math
{
    //public class PlusFilter : FilterExpression<NumericLiteral, NumericLiteral>
    public class PlusFilter : FilterExpression<NumericValue, NumericValue>
    {
        private readonly NumericValue _operand;

        public PlusFilter(NumericValue operand) { _operand = operand; }

        public override LiquidExpressionResult ApplyTo(ITemplateContext ctx, NumericValue numericValue)
        {
            if (_operand == null)
            {
                return LiquidExpressionResult.Error("The argument to \""+Name+"\" is missing.");
            }
            var val = numericValue.DecimalValue +  _operand.DecimalValue;

            return MathHelper.GetReturnValue(val, numericValue, _operand);
        }
    }
}
