﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Liquid.NET.Tests.Filters.Strings
{
    [TestFixture]
    public class AppendFilterTests
    {
        [Test]
        public void It_Should_Append_Text_To_A_String()
        {
            // Arrange
            var result = RenderingHelper.RenderTemplate("Result : {{ \"test\" | append : \".jpg\" }}");

            // Assert
            Assert.That(result, Is.EqualTo("Result : test.jpg"));
        }

        [Test]
        public void It_Should_Append_Text_To_A_Number()
        {
            // Arrange
            var result = RenderingHelper.RenderTemplate("Result : {{ 123 | append : \".jpg\" }}");

            // Assert
            Assert.That(result, Is.EqualTo("Result : 123.jpg"));
        }

        [Test]
        public void It_Should_Append_Text_To_Nothing()
        {
            // Arrange
            var result = RenderingHelper.RenderTemplate("{%assign x = nil%}Result : {{ x | append : \".jpg\" }}");

            // Assert
            Assert.That(result, Is.EqualTo("Result : .jpg"));
        }



    }
}
