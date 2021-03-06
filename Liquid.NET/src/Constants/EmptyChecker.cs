﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using Liquid.NET.Grammar;

namespace Liquid.NET.Constants
{
    public static class EmptyChecker
    {
        public static bool IsEmpty(IExpressionConstant val)
        {
            if (val == null /* || val.IsNil */)
            {
                return false; // this appears to be the case in liquid?
            }
            //return CheckIsEmpty((dynamic)val);
            var str = val as StringValue;
            if (str != null)
            {
                return CheckIsEmpty(str);
            }
            var arr = val as ArrayValue;
            if (arr != null)
            {
                return CheckIsEmpty(arr);
            }
            var dict = val as DictionaryValue;
            if (dict != null)
            {
                return CheckIsEmpty(dict);
            }
            return CheckIsEmpty(val);
        }

        private static bool CheckIsEmpty(IExpressionConstant _)
        {
            return false; // the only conditions will have been caught by IsEmpty
        }

        private static bool CheckIsEmpty(StringValue val)
        {
            return String.IsNullOrEmpty(val.StringVal);
        }


        private static bool CheckIsEmpty(ArrayValue val)
        {
            return !val.ArrValue.Any();
        }

        private static bool CheckIsEmpty(DictionaryValue val)
        {
            return !val.DictValue.Any();
        }


    }

    public static class BlankChecker
    {
        public static bool IsBlank(IExpressionConstant val)
        {
            if (val == null)
            {
                return true; // regardless of what shopify liquid + activesupport do
            }
            //return CheckIsBlank((dynamic)val);
            var str = val as StringValue;
            if (str != null)
            {
                return CheckIsBlank(str);
            }
            var arr = val as ArrayValue;
            if (arr != null)
            {
                return CheckIsBlank(arr);
            }
            var dict = val as DictionaryValue;
            if (dict != null)
            {
                return CheckIsBlank(dict);
            }
            return CheckIsBlank(val);
        }

        private static bool CheckIsBlank(IExpressionConstant _)
        {
            return false; // the only conditions will have been caught by IsBlank -- other types are never blank
        }

        private static bool CheckIsBlank(StringValue val)
        {
            String stringVal = val.StringVal;
            return String.IsNullOrEmpty(stringVal.Trim());
        }


        private static bool CheckIsBlank(ArrayValue val)
        {
            return !val.ArrValue.Any();
        }

        private static bool CheckIsBlank(DictionaryValue val)
        {
            return !val.DictValue.Any();
        }


    }

}
