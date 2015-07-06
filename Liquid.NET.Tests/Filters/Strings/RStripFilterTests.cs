﻿using System;

using NUnit.Framework;

namespace Liquid.NET.Tests.Filters.Strings
{
    [TestFixture]
    public class RStripFilterTests
    {
        [Test]
        public void It_Should_Strip_Whitespace_on_The_Right()
        {
            // Act
            var result = RenderingHelper.RenderTemplate("Result : {{ '   too many spaces           ' | rstrip }}");

            // Assert
            Assert.That(result, Is.EqualTo("Result :    too many spaces"));

        }

        [Test]
        public void It_Should_Strip_Whitespace_When_EMpty()
        {
            // Act
            var result = RenderingHelper.RenderTemplate("{% assign x = nil %}Result : {{ x | rstrip }}");

            // Assert
            Assert.That(result, Is.EqualTo("Result : "));

        }

    }
}
