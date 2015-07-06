using System;
using System.Runtime.InteropServices.ComTypes;
using Liquid.NET.Constants;
using Liquid.NET.Utils;

namespace Liquid.NET.Filters
{
    public interface IFilterExpression
    {
        String Name { get; set; }

        Type SourceType { get; }
        Type ResultType { get; }

        LiquidExpressionResult Apply(ITemplateContext ctx, Option<IExpressionConstant> liquidExpression);
        
        LiquidExpressionResult ApplyToNil(ITemplateContext ctx);

    };

    public interface IFilterExpression<TSource, out TResult> : IFilterExpression
        where TSource : IExpressionConstant
        where TResult : IExpressionConstant
    {
        TResult Apply(ITemplateContext ctx, Option<TSource> liquidExpression);
    };

    public abstract class FilterExpression<TSource, TResult> : IFilterExpression
        where TSource :  IExpressionConstant
        where TResult : IExpressionConstant
    {
        public String Name {get; set;}


        public virtual LiquidExpressionResult Apply(ITemplateContext ctx, Option<TSource> option)
        {
            return ApplyTo(ctx, (dynamic) option.Value);
        }

        public LiquidExpressionResult Apply(ITemplateContext ctx, Option<IExpressionConstant> liquidExpression)
        {
            if (liquidExpression.HasValue)
            {
                return Apply(ctx, new Some<TSource>((TSource) liquidExpression.Value));
            }
            else
            {
                return Apply(ctx, new None<TSource>());    
            }

        }

        /* override some or all of these ApplyTo functions */
        public virtual LiquidExpressionResult ApplyTo(ITemplateContext ctx, IExpressionConstant liquidExpression) // todo: make this fallback abtstract
        {
            throw new NotImplementedException();
        }

        public virtual LiquidExpressionResult ApplyTo(ITemplateContext ctx, NumericValue val)
        {
            return ApplyTo(ctx,  (IExpressionConstant)val);
        }

        public virtual LiquidExpressionResult ApplyTo(ITemplateContext ctx, StringValue val)
        {
            return ApplyTo(ctx, (IExpressionConstant)val);
        }

        public virtual LiquidExpressionResult ApplyTo(ITemplateContext ctx, DictionaryValue val)
        {
            return ApplyTo(ctx, (IExpressionConstant)val);
        }

        public virtual LiquidExpressionResult ApplyTo(ITemplateContext ctx, ArrayValue val)
        {
            return ApplyTo(ctx, (IExpressionConstant)val);
        }

        public virtual LiquidExpressionResult ApplyTo(ITemplateContext ctx, BooleanValue val)
        {
            return ApplyTo(ctx, (IExpressionConstant)val);
        }

        public virtual LiquidExpressionResult ApplyTo(ITemplateContext ctx, DateValue val)
        {
            return ApplyTo(ctx, (IExpressionConstant)val);
        }


        public virtual LiquidExpressionResult ApplyTo(ITemplateContext ctx, GeneratorValue val)
        {
            return ApplyTo(ctx, (IExpressionConstant)val);
        }

        public virtual LiquidExpressionResult ApplyToNil(ITemplateContext ctx)
        {
            return LiquidExpressionResult.Success(new None<IExpressionConstant>());
        }

        public Type SourceType
        {
            get { return typeof(TSource); }
        }

        public Type ResultType
        {
            get { return typeof(TResult); }
        }

       
    }
}
