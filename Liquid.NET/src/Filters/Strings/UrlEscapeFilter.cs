﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Liquid.NET.Constants;
using Liquid.NET.Utils;

namespace Liquid.NET.Filters.Strings
{
    public class UrlEscapeFilter : FilterExpression<IExpressionConstant, StringValue>
    {

        public override LiquidExpressionResult ApplyTo(ITemplateContext ctx, IExpressionConstant liquidExpression)
        {
            //return StringUtils.Eval(liquidStringExpression, x => WebUtility.UrlEncode(x));
            // Dunno if this is right but it seems to do the same as CGI::escape
            return LiquidExpressionResult.Success(StringUtils.Eval(liquidExpression, Uri.EscapeUriString));
        }

    }
}
