﻿using System;
using System.Collections.Generic;
using System.Linq;

using Liquid.NET.Constants;
using Liquid.NET.Tests.Helpers;
using NUnit.Framework;

namespace Liquid.NET.Tests
{
    [TestFixture]
    public class CapacityTests
    {
        [Test]
        public void It_Should_Parse_Includes()
        {
            DateTime start = DateTime.Now;
            // Arrange
            int includes = 100;
            //String templateFragment = "{% for i in (1..10) %}{{ array[i] }}";
            String template = "<html><body>\r\n";
            for (int i = 0; i < includes; i++)
            {
                template += "{% include \"test" + i + "\" %}";
            }

            var virtualFs = Enumerable.Range(1, includes).ToDictionary(k => "test" + k.ToString(), CreateInclude);
            template += "</body>\r\n</html>";
            ITemplateContext ctx = new TemplateContext()
                .WithAllFilters()
                .WithFileSystem(new TestFileSystem(virtualFs))
                .DefineLocalVariable("array", CreateArrayValues());


            // Act

            var result = RenderingHelper.RenderTemplate(template, ctx);

            //Logger.Log(result);

            // Assert
            var end = DateTime.Now;
            TimeSpan timeDiff = end - start;
            Logger.Log("ELAPSED: " + timeDiff.TotalMilliseconds);
            Assert.That(timeDiff.Milliseconds < 500);
            //Assert.Fail("Not Implemented Yet");

        }

        [Test]
        // https://github.com/antlr/antlr4/issues/192#issuecomment-15238595
        public void It_Should_Not_Bog_On_Raw_Text_With_Adaptive_Prediction()
        {
            // Arrange

            DateTime start = DateTime.Now;
            Logger.Log("STARTING...");
            int iterations = 100;

            String template = "<html><body>\r\n";
            for (int i = 0; i < iterations; i++)
            {
                template += "{% for x in (1..10) %} Test {{ x }}{% endfor %}";
                for (int j = 0; j < 100; j++)
                {
                    template += " Raw text "+j;
                }
            }
            template += "</body>\r\n</html>";
            ITemplateContext ctx = new TemplateContext()
                .WithAllFilters();

            // Act

            var result = RenderingHelper.RenderTemplate(template, ctx);

            Logger.Log(result);

            // Assert
            var end = DateTime.Now;
            TimeSpan timeDiff = end - start;
            Logger.Log("ELAPSED: " + timeDiff.TotalMilliseconds);
            Assert.That(timeDiff.Milliseconds < 1000);
            //Assert.Fail("Not Implemented Yet");

        }


        private string CreateInclude(int i)
        {
            return "{% for i in (1..10) %}{{ array[i] }}{% endfor %}";
        }

        private static ITemplateContext CreateContext(Dictionary<String, String> dict)
        {
            return new TemplateContext().WithFileSystem(new TestFileSystem(dict));
        }



        private ArrayValue CreateArrayValues()
        {
            var list = new List<IExpressionConstant>
            {
                new StringValue("a string"),
                NumericValue.Create(123),
                NumericValue.Create(456m),
                new BooleanValue(false),
                new StringValue("a string 2"),
                NumericValue.Create(999999.0m ),
                NumericValue.Create(456m),
                new BooleanValue(true),
                new ArrayValue(new List<IExpressionConstant>{NumericValue.Create(1), NumericValue.Create(2), NumericValue.Create(3)}),
                new DictionaryValue(new Dictionary<String,IExpressionConstant>{{"1", NumericValue.Create(1)}, {"2", NumericValue.Create(2)}})
            };
            return new ArrayValue(list);
        }

    }
}