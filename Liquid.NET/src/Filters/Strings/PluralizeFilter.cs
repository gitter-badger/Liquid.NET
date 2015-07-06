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
    /// https://docs.shopify.com/themes/liquid-documentation/filters/string-filters#pluralize
    /// </summary>
    public class PluralizeFilter : FilterExpression<NumericValue, StringValue>
    {
        private readonly StringValue _single;
        private readonly StringValue _plural;

        public PluralizeFilter(StringValue single, StringValue plural)
        {
            _single = single ?? new StringValue(""); ;
            _plural = plural ?? new StringValue(""); ;
        }

        public override LiquidExpressionResult ApplyTo(ITemplateContext ctx, NumericValue numericValue)
        {
            String numericString = ValueCaster.RenderAsString((IExpressionConstant) numericValue);
            var str = new StringValue(numericString+" ");
            return LiquidExpressionResult.Success(str.Join(numericValue.DecimalValue == 1 ? _single : _plural));
        }
    }
}
