﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Liquid.NET.Filters;
using Liquid.NET.Filters.Array;
using Liquid.NET.Symbols;
using NUnit.Framework;

namespace Liquid.NET.Tests.Symbols
{
    [TestFixture]
    public class SymbolTableStackFactoryTests
    {
        [Test]
        public void It_Should_Initialize_The_Filter_Registry()
        {
            // Arrange
            var ctx = new TemplateContext().WithFilter<SortFilter>("abcde");
            
            // Act
            var result = ctx.SymbolTableStack;

            // Assert
            Assert.That(result.HasFilter("abcde"));

        }

    }
}
