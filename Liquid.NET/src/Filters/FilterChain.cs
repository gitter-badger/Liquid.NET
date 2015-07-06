using System;
using System.Collections.Generic;
using System.Linq;
using Liquid.NET.Constants;
using Liquid.NET.Utils;

namespace Liquid.NET.Filters
{
    public static class FilterChain
    {

        /// <summary>
        /// Create a chain of functions that returns a value of type resultType.  If function #1 doesn't
        /// return a T, a casting function will be interpolated into the chain.
        /// </summary>
        /// <returns></returns>
        public static Func<Option<IExpressionConstant>, LiquidExpressionResult> CreateChain(
            Type objExprType,
            ITemplateContext ctx,
            IEnumerable<IFilterExpression> filterExpressions)
        {
            var expressions = filterExpressions.ToList();
            if (!expressions.Any())
            {
                return x => new LiquidExpressionResult(x);
            }

            // create the casting filter which will cast the incoming object to the input type of filter #1
            
            Func<Option<IExpressionConstant>, LiquidExpressionResult> castFn = 
                objExpr => objExpr.HasValue
                    ? CreateCastFilter(objExpr.GetType().GenericTypeArguments[0], expressions[0].SourceType).Apply(ctx, objExpr) 
                    : LiquidExpressionResult.Success(new None<IExpressionConstant>());
            //Func<Option<IExpressionConstant>, LiquidExpressionResult> castFn = 
                //objExpr => CreateCastFilter(objExpr.GetType(), expressions[0].SourceType).Apply(objExpr);

            // put the casting filter in between the object and the chain
//            return objExpression => objExpression.Bind(x => castFn(objExpression))
//                                                 .Bind(CreateChain(expressions));      
            //return optionExpression => optionExpression.HasValue ? castFn(optionExpression.Value) : LiquidExpressionResult.Success(new None<IExpressionConstant>()).Bind(CreateChain(expressions));

            //return objExpression => objExpression.Bind(x => castFn(objExpression))
            //    .Bind(CreateChain(expressions));    

            // TODO: Figure out how to do this.  It should call ApplyNil() or something.
            //return optionExpression => (castFn(optionExpression.HasValue ? optionExpression.Value : null)).Bind(CreateChain(ctx, expressions));
            return optionExpression => (castFn(optionExpression)).Bind(CreateChain(ctx, expressions));


        }


        public static Func<Option<IExpressionConstant>, LiquidExpressionResult> CreateChain(
            ITemplateContext ctx,
            IEnumerable<IFilterExpression> filterExpressions)
        {
            return x =>
            {
                var bindAll = BindAll(ctx, InterpolateCastFilters(filterExpressions));
                return bindAll(LiquidExpressionResult.Success(x)); // TODO: Is this the best way to kick off the chain?
            };
        }

        public static Func<LiquidExpressionResult, LiquidExpressionResult> BindAll(
            ITemplateContext ctx,
            IEnumerable<IFilterExpression> filterExpressions)
        {
            return initValue => filterExpressions.Aggregate(initValue, (current, filter) =>
            {
                if (initValue.IsError)
                {
                    return initValue;
                }
                if (current.IsError)
                {
                    return current;
                }
                LiquidExpressionResult result;
                if (current.SuccessResult.HasValue)
                {
                    result = filter.Apply(ctx, (dynamic) current.SuccessResult);
                    //result = filter.Apply(ctx, current.SuccessResult);
                }
                else
                {
                    result = filter.ApplyToNil(ctx);
                }
                return result;

            });
        }

        /// <summary>
        /// Make a list of functions, each of which has the input of the previous function.  Interpolate a casting
        /// function if the input of one doesn't fit with the value of the next.
        /// 
        /// TODO: I think this should be part of the bind function.
        /// </summary>
        /// <param name="filterExpressions"></param>
        /// <returns></returns>
        public static IEnumerable<IFilterExpression> InterpolateCastFilters(IEnumerable<IFilterExpression> filterExpressions)
        {

            var result = new List<IFilterExpression>();

            Type expectedInputType = null;
            foreach (var filterExpression in filterExpressions)
            {
                // TODO: The expectedInputType might be a superclass of the output (not just equal type)
                //if (expectedInputType != null && filterExpression.SourceType != expectedInputType)
                if (expectedInputType != null && !filterExpression.SourceType.IsAssignableFrom(expectedInputType))
                {
                    //Console.WriteLine("Creating cast from " + filterExpression +" TO "+expectedInputType);
                    result.Add(CreateCastFilter(expectedInputType, filterExpression.SourceType));
                }
                result.Add(filterExpression);
                expectedInputType = filterExpression.ResultType;
            }

            return result;
        }

        public static IFilterExpression CreateCastFilter(Type sourceType, Type resultType)
            //where sourceType: IExpressionConstant
        {
            // TODO: Move this to FilterFactory.Instantiate

            //var wrappedResultType = resultType.GenericTypeArguments[0];
            Type genericClass = typeof(CastFilter<,>);
            // MakeGenericType is badly named
            //Console.WriteLine("FilterChain Creating Converter from " + sourceType + " to " + resultType);
            Type constructedClass = genericClass.MakeGenericType(sourceType, resultType);
            return (IFilterExpression)Activator.CreateInstance(constructedClass);
        }

    }
}
