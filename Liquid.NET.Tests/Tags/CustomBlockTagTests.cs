﻿using System;
using System.Collections.Generic;
using System.Linq;

using Liquid.NET.Constants;
using Liquid.NET.Expressions;
using Liquid.NET.Symbols;
using Liquid.NET.Tags.Custom;
using Liquid.NET.Utils;
using NUnit.Framework;

namespace Liquid.NET.Tests.Tags
{
    [TestFixture]
    public class CustomBlockTagTests
    {
        [Test]
        public void It_Should_Parse_A_Custom_BlockTag()
        {
            try
            {
                // Act
                var templateContext =
                    new TemplateContext().WithAllFilters().WithCustomTagBlockRenderer<WordReverserBlockTag>("echoargs");

                var result =
                    RenderingHelper.RenderTemplate("Result : {% echoargs \"hello\" 123 true %}echo{% endechoargs %}",
                        templateContext);
                Logger.Log(result);

                // Assert
                Assert.That(result, Is.EqualTo("Result : ohce"));
            }
            catch (LiquidRendererException ex)
            {
                Assert.Fail(String.Join(",", ex.LiquidErrors.Select(x => x.Message)));
            }
        }

        [Test]
        public void It_Should_Not_Parse_A_Custom_BlockTag_With_No_End()
        {
            // Act
            var templateContext = new TemplateContext().WithAllFilters().WithCustomTagBlockRenderer<WordReverserBlockTag>("echoargs");
            try
            {
                var result = RenderingHelper.RenderTemplate(
                    "Result : {% echoargs \"hello\" 123 true %}echo{% endsomethingelse %}", templateContext);
                Assert.Fail("This should have thrown an error.");
            }
            catch (LiquidParserException ex)
            {
                var allErrors = String.Join(",", ex.LiquidErrors.Select(x => x.ToString()));
                Logger.Log(allErrors);
                Assert.That(allErrors, Is.StringContaining("There was no opening tag for the ending tag 'endsomethingelse'"));
            }
        }


        [Test]
        public void It_Should_Parse_A_Custom_BlockTag_With_Nested_Liquid()
        {
            // Act
            var templateContext = new TemplateContext().WithAllFilters().WithCustomTagBlockRenderer<WordReverserBlockTag>("echoargs");
            var result = RenderingHelper.RenderTemplate("Result : {% echoargs \"hello\" 123 true %}{% if true %}IT IS TRUE{% endif %}{% endechoargs %}", templateContext);

            // Assert
            Assert.That(result, Is.EqualTo("Result : TI SI EURT"));
               
        }

        [Test]
        public void It_Should_Parse_Very_Nested_Tags()
        {
            // Act
            var templateContext = new TemplateContext().WithAllFilters().WithCustomTagBlockRenderer<WordReverserBlockTag>("reverse");
            var result = RenderingHelper.RenderTemplate("Result : {% reverse %}{% if true %}TRUE{% endif %}{% reverse %}DEF{% reverse %}ABC{% endreverse %}{% endreverse %}{% endreverse %}", templateContext);

            // Assert
            Assert.That(result, Is.EqualTo("Result : DEFCBAEURT"));

        }

        [Test]
        public void It_Should_Parse_A_Custom_BlockTag_Along_With_A_Custom_Tag() 
        {
            // Act
            var templateContext = new TemplateContext()
                .WithAllFilters()
                .WithCustomTagBlockRenderer<WordReverserBlockTag>("echoargs")
                .WithCustomTagRenderer<CustomTagTests.EchoArgsTagRenderer>("echoargs2");
            var result = RenderingHelper.RenderTemplate("Result : {% echoargs2 \"Test\" %}{% echoargs \"hello\" 123 true %}{% if true %}IT IS TRUE{% endif %}{% endechoargs %}", templateContext);

            // Assert
            Assert.That(result, Is.EqualTo("Result : I heard string:TestTI SI EURT"));

        }


        [Test]
        public void It_Should_Create_A_For_Like_Loop()
        {
            // Act
            var templateContext = new TemplateContext()
                .WithAllFilters()
                .DefineLocalVariable("array", new ArrayValue(new List<IExpressionConstant>{NumericValue.Create(10),NumericValue.Create(11)} ))
                .WithCustomTagBlockRenderer<ForLikeBlockTag>("forcustom");
            var result = RenderingHelper.RenderTemplate("Result : {% forcustom \"item\" array %}{{item}}{% endforcustom %}", templateContext);

            // Assert
            Assert.That(result, Is.EqualTo("Result : START CUSTOM FOR LOOP1011END CUSTOM FOR LOOP"));

        }

        /// <summary>
        /// Reverse each word
        /// </summary>
        public class WordReverserBlockTag : ICustomBlockTagRenderer
        {

            public StringValue Render(
                    RenderingVisitor renderingVisitor,
                    ITemplateContext templatecontext,
                    TreeNode<IASTNode> liquidBlock,
                    IList<Option<IExpressionConstant>> args)
            {
                var result = EvalLiquidBlock(renderingVisitor, liquidBlock);

                return Reverse(result);
            }

            private static StringValue Reverse(string result)
            {
                var words = result.Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries);

                return new StringValue(String.Join(" ",
                    words.Select(x => new String(x.Reverse().ToArray()))));
            }

            private static String EvalLiquidBlock(RenderingVisitor renderingVisitor, TreeNode<IASTNode> liquidBlock)
            {
                String result = "";
                Action<String> accumulator = str => result += str;
                renderingVisitor.PushTextAccumulator(accumulator);
                renderingVisitor.StartWalking(liquidBlock);
                renderingVisitor.PopTextAccumulator();
                return result;
            }

        }


        /// <summary>
        /// Reverse each word
        /// </summary>
        public class ForLikeBlockTag : ICustomBlockTagRenderer
        {

            public StringValue Render(
                RenderingVisitor renderingVisitor,
                ITemplateContext templateContext,
                TreeNode<IASTNode> liquidBlock,
                IList<Option<IExpressionConstant>> args)
            {
                var localBlockScope = new SymbolTable();
                templateContext.SymbolTableStack.Push(localBlockScope);
                StringValue result;

                try
                {
                    // normally you would need to verify that arg0 and arg1 exists and are the correct value types.
                    String varname = args[0].Value.ToString();

                    var iterableFactory = new ArrayValueCreator(
                        new TreeNode<LiquidExpression>(
                            new LiquidExpression { Expression = args[1].Value }));

                    var iterable = iterableFactory.Eval(templateContext).ToList();

                    result = IterateBlock(renderingVisitor, varname, templateContext, iterable, liquidBlock);
                }
                finally
                {
                    templateContext.SymbolTableStack.Pop();
                }
                return new StringValue("START CUSTOM FOR LOOP" + result.StringVal + "END CUSTOM FOR LOOP");

            }

            private StringValue IterateBlock(
                RenderingVisitor renderingVisitor,
                String varname,
                ITemplateContext templateContext,
                List<IExpressionConstant> iterable,
                TreeNode<IASTNode> liquidBlock)
            {
                String result = "";
                renderingVisitor.PushTextAccumulator(str => result += str);
                foreach (var item in iterable)
                {
                    templateContext.SymbolTableStack.Define(varname, item);
                    renderingVisitor.StartWalking(liquidBlock);
                }
                renderingVisitor.PopTextAccumulator();
                return new StringValue(result);
            }

        }
    }
}
