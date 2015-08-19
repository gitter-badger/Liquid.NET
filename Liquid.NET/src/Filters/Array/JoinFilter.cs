﻿using System;

using System.Linq;
using Liquid.NET.Constants;
using Liquid.NET.Utils;

namespace Liquid.NET.Filters.Array
{
    public class JoinFilter : FilterExpression<ExpressionConstant, StringValue>
    {
        private readonly StringValue _separator;

        public JoinFilter(StringValue separator)
        {
            _separator = separator;
        }

        public override LiquidExpressionResult ApplyTo(ITemplateContext ctx, ArrayValue liquidArrayExpression)
        {
            var vals = liquidArrayExpression
                .ArrValue
                .Select(ValueCaster.RenderOptionAsString);
                //.Select(ValueCaster.Cast<IExpressionConstant, StringValue>)
                //.Select(x => x.Value);
            return LiquidExpressionResult.Success(new StringValue(String.Join(_separator.StringVal, vals)));
        }

        public override LiquidExpressionResult ApplyTo(ITemplateContext ctx, StringValue liquidStringExpression)
        {
            if (String.IsNullOrEmpty(liquidStringExpression.StringVal))
            {
                return LiquidExpressionResult.Success(Option<IExpressionConstant>.None());
            }

            return LiquidExpressionResult.Success(new StringValue(String.Join(_separator.StringVal,
                liquidStringExpression.StringVal.ToCharArray().Select(c => c.ToString()))));

        }
    }
}
