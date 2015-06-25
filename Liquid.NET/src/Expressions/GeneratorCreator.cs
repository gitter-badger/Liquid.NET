﻿using System;
using System.Collections.Generic;
using System.Linq;

using Liquid.NET.Constants;
using Liquid.NET.Symbols;
using Liquid.NET.Utils;

namespace Liquid.NET.Expressions
{
    /// <summary>
    /// 
    /// </summary>
    /// 
    public interface IIterableCreator
    {
        IEnumerable<IExpressionConstant> Eval(SymbolTableStack symbolTableStack);
    }

    public class StringValueIterableCreator : IIterableCreator
    {
        private readonly StringValue _stringValue;

        public StringValueIterableCreator(StringValue stringValue)
        {
            _stringValue = stringValue;
        }

        public IEnumerable<IExpressionConstant> Eval(SymbolTableStack symbolTableStack)
        {
            return _stringValue.StringVal
                .ToCharArray()
                .Select(x => new StringValue("" + x));
            
        }
    }

    public class ArrayValueCreator : IIterableCreator
    {
        private readonly TreeNode<LiquidExpression> _arrayValueExpression;

        public ArrayValueCreator(TreeNode<LiquidExpression> arrayValueExpression)
        {
            _arrayValueExpression = arrayValueExpression;
        }

        public IEnumerable<IExpressionConstant> Eval(SymbolTableStack symbolTableStack)
        {            
            var expressionConstant = LiquidExpressionEvaluator.Eval(_arrayValueExpression, symbolTableStack);
            
            var castResult = ValueCaster.Cast<IExpressionConstant, ArrayValue>(expressionConstant.SuccessResult.Value);
            if (castResult.IsError)
            {
                // ??
                return new List<IExpressionConstant>();
            }
            else
            {
                return castResult.SuccessValue<ArrayValue>().Select(x => x.Value);
            }
        }
    }

    public class GeneratorCreator : IIterableCreator
    {
        private readonly TreeNode<LiquidExpression> _startExpression;
        private readonly TreeNode<LiquidExpression> _endExpression;

        public GeneratorCreator(TreeNode<LiquidExpression> start, TreeNode<LiquidExpression> end)
        {
            _startExpression = start;
            _endExpression = end;
        }

        public IEnumerable<IExpressionConstant> Eval(SymbolTableStack symbolTableStack)
        {
            var startValue = ValueAsNumeric(_startExpression, symbolTableStack);
            var endValue = ValueAsNumeric(_endExpression, symbolTableStack);
            //Console.WriteLine("*** Generating sequence from "+ startValue.IntValue+ " to " +endValue.IntValue);
            var generatorValue = new GeneratorValue(startValue, endValue);
            return generatorValue;

        }

        private NumericValue ValueAsNumeric(TreeNode<LiquidExpression> expr, SymbolTableStack symbolTableStack)
        {
            var liquidExpressionResult = LiquidExpressionEvaluator.Eval(expr, symbolTableStack);
            if (liquidExpressionResult.IsError)
            {
                return new NumericValue(0);
            }
            var valueAsNumeric = ValueCaster.Cast<IExpressionConstant, NumericValue>(liquidExpressionResult.SuccessResult.Value);

            return liquidExpressionResult.IsSuccess && liquidExpressionResult.SuccessResult.HasValue ? 
                valueAsNumeric.SuccessValue<NumericValue>()
                : new NumericValue(0);
        }
    }
}
