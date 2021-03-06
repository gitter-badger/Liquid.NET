﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Liquid.NET.Constants;
using Liquid.NET.Utils;

namespace Liquid.NET.Filters.Strings
{
    /// <summary>
    /// https://docs.shopify.com/themes/liquid-documentation/filters/string-filters#capitalize
    /// </summary>
    public class CapitalizeFilter : FilterExpression<IExpressionConstant, StringValue>
    {
        public override LiquidExpressionResult ApplyTo(ITemplateContext ctx, IExpressionConstant liquidExpression)
        {
            String before = ValueCaster.RenderAsString(liquidExpression);
            if (String.IsNullOrWhiteSpace(before))
            {
                return LiquidExpressionResult.Success(new StringValue(""));
            }
            var nospaces = before.TrimStart();
            String trimmed = before.Substring(0, before.Length - nospaces.Length);
            return LiquidExpressionResult.Success(new StringValue(trimmed + char.ToUpper(nospaces[0]) + nospaces.Substring(1)));
        }
    }
}
