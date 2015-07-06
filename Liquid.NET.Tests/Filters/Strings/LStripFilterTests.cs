﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Liquid.NET.Tests.Filters.Strings
{
    [TestFixture]
    public class LStripFilterTests
    {
        [Test]
        public void It_Should_Strip_Whitespace_on_The_Left()
        {
            // Act
            var result = RenderingHelper.RenderTemplate("Result : {{ '   too many spaces           ' | lstrip }}");

            // Assert
            Assert.That(result, Is.EqualTo("Result : too many spaces           "));

        }

        [Test]
        public void It_Should_Strip_Whitespace_When_EMpty()
        {
            // Act
            var result = RenderingHelper.RenderTemplate("{% assign x = nil %}Result : {{ x | lstrip }}");

            // Assert
            Assert.That(result, Is.EqualTo("Result : "));

        }
    }
}
