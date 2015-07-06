﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Liquid.NET.Constants;
using Liquid.NET.Utils;

namespace Liquid.NET.Filters.Strings
{
    public class RemoveFirstFilter : FilterExpression<IExpressionConstant, StringValue>
    {
        private readonly StringValue _stringToRemove;

        public RemoveFirstFilter(StringValue stringToRemove)
        {
            _stringToRemove = stringToRemove ?? new StringValue(""); ;
        }

        public override LiquidExpressionResult ApplyTo(ITemplateContext ctx, IExpressionConstant liquidExpression)
        {
            return LiquidExpressionResult.Success(StringUtils.Eval(liquidExpression, x => StringUtils.ReplaceFirst(x, _stringToRemove.StringVal, "")));            
        }

       
    }
}
