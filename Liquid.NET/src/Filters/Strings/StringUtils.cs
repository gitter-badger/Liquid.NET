﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Liquid.NET.Constants;

namespace Liquid.NET.Filters.Strings
{
    public static class StringUtils
    {
        public static StringValue Eval(IExpressionConstant liquidExpression, Func<String, String> f)
        {
            String before = ValueCaster.RenderAsString(liquidExpression);

            if (before == null)
            {
                return new StringValue("");
            }
            //if (String.IsNullOrWhiteSpace(before))
            //{
                //return new StringValue("");
            //}
            return new StringValue(f(before));
        }

        public static String ReplaceFirst(string origStr, String removeStr, String replaceStr)
        {
            var pos = origStr.IndexOf(removeStr, StringComparison.Ordinal);
            return pos < 0
                ? origStr
                : origStr.Substring(0, pos) + replaceStr + origStr.Substring(pos + removeStr.Length);
        }

    }
}
