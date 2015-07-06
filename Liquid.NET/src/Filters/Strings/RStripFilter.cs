﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Liquid.NET.Constants;
using Liquid.NET.Utils;

namespace Liquid.NET.Filters.Strings
{
    public class RStripFilter : FilterExpression<IExpressionConstant, StringValue>
    {

        public override LiquidExpressionResult ApplyTo(ITemplateContext ctx, StringValue liquidExpression)
        {
            // TODO: The StringUtils.Eval is now redundant...
            return  LiquidExpressionResult.Success(StringUtils.Eval(liquidExpression, x => x.TrimEnd()));
            //return LiquidExpressionResult.Success(.TrimEnd()));
        }

    }
}
