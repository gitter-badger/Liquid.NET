﻿using System;
using Liquid.NET.Constants;
using Liquid.NET.Utils;

namespace Liquid.NET.Filters.Strings
{
    public class TruncateFilter : FilterExpression<IExpressionConstant, StringValue>
    {
        private readonly NumericValue _length;
        private readonly StringValue _truncateString;

        public TruncateFilter(NumericValue length, StringValue truncateString)
        {
            _length = length;
            _truncateString = truncateString == null || truncateString.Value == null ? new StringValue("...") : truncateString;
        }

        public override LiquidExpressionResult ApplyTo(ITemplateContext ctx, IExpressionConstant liquidExpression)
        {
            return LiquidExpressionResult.Success(StringUtils.Eval(liquidExpression, Truncate));
        }

        private string Truncate(string s)
        {
            int requestedLength = _length.IntValue;
            String ellipsis = _truncateString.StringVal;
            if (s == null)
            {
                return "";
            }
            
            // return the string if it doesn't need the ellipsis
            if (s.Length < requestedLength)
            {
                return s;
            }
            // return s if it's shorter than the ellipsis;
            if (ellipsis.Length >= requestedLength)
            {
                return s.Substring(0, requestedLength );
            }
          
            
            return s.Substring(0, requestedLength - ellipsis.Length) + ellipsis;
        }
    }
}
