//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
//
//     Source: errors.txt
// </auto-generated>
//------------------------------------------------------------------------------

// Generated from Liquid.Ruby\writetest.rb
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Liquid.NET.Constants;
using NUnit.Framework;

namespace Liquid.NET.Tests.Ruby
{
    [TestFixture]
    public class ErrorTests {

        [Test]
        [TestCase(@"", @"")]
        [TestCase(@"{{ ""1"" | divided_by: ""0"" }}", @"Liquid error: divided by 0")]
        [TestCase(@"{% if 1 == woeifj %}EQUAL{% else %}NOT EQUAL{% endif %}", @"NOT EQUAL")]
        [TestCase(@"{% if user.payments == wfwefewf %}you never paid !{% endif %}", @"you never paid !")]
        [TestCase(@"{% if user == wfwefewf %}you never paid !{% endif %}", @"you never paid !")]
        [TestCase(@"{% if ""user"" == wfwefewf %}you never paid !{% endif %}", @"")]
        [TestCase(@"{% assign x = true %}{% if x == true %}TRUE{% else %}FALSE{% endif %}", @"TRUE")]
        [TestCase(@"{% assign x = false %}{% if x == true %}TRUE{% else %}FALSE{% endif %}", @"FALSE")]
        [TestCase(@"{% assign x = true %}{% if x %}TRUE{% else %}FALSE{% endif %}", @"TRUE")]
        [TestCase(@"{% assign x = false %}{% if x  %}TRUE{% else %}FALSE{% endif %}", @"FALSE")]
        [TestCase(@"{% if myundefined == true %}TRUE{% else %}FALSE{% endif %}", @"FALSE")]
        [TestCase(@"{% if myundefined == false %}TRUE{% else %}FALSE{% endif %}", @"FALSE")]
        [TestCase(@"{% if myundefined %}TRUE{% else %}FALSE{% endif %}", @"FALSE")]
        [TestCase(@"{% if myundefined %}TRUE{% else %}FALSE{% endif %}", @"FALSE")]
        [TestCase(@"{% if myundefined == null %}TRUE{% else %}FALSE{% endif %}", @"TRUE")]
        [TestCase(@"{% if ""myundefined"" == null %}TRUE{% else %}FALSE{% endif %}", @"FALSE")]
        [TestCase(@"{% assign x = """" %}{% if x == null %}TRUE{% else %}FALSE{% endif %}", @"FALSE")]
        [TestCase(@"{% assign x = null %}{% if x == null %}TRUE{% else %}FALSE{% endif %}", @"TRUE")]
        [TestCase(@"{% assign x = 0 %}{% if x == null %}TRUE{% else %}FALSE{% endif %}", @"FALSE")]
        [TestCase(@"{% if myundefined != null %}TRUE{% else %}FALSE{% endif %}", @"FALSE")]
        [TestCase(@"{% if ""myundefined"" != null %}TRUE{% else %}FALSE{% endif %}", @"TRUE")]
        public void It_Should_Match_Ruby_Output(String input, String expected) {

            // Arrange
            ITemplateContext ctx = new TemplateContext().WithAllFilters();
            var template = LiquidTemplate.Create(input);
        
            // Act
            String result = template.Render(ctx);
        
            // Assert
            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        [TestCase(@"{% unknown_tag %}", @"Liquid syntax error: Unknown tag 'unknown_tag'")]
        public void It_Should_Generate_An_Exception(String input, String expectedMessage) {

            // Arrange
            ITemplateContext ctx = new TemplateContext().WithAllFilters();

            try
            {
                var result = RenderingHelper.RenderTemplate(input);
                Assert.Fail("Expected exception: "+expectedMessage);
            }
            catch (LiquidParserException ex)
            {
                // Assert
                Assert.That(ex.LiquidErrors[0].ToString(), Is.StringContaining(expectedMessage));
            }
            catch (LiquidRendererException ex)
            {
                // Assert
                Assert.That(ex.LiquidErrors[0].ToString(), Is.StringContaining(expectedMessage));
            }
        }
        
    }
}
