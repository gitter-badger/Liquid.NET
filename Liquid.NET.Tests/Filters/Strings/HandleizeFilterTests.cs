﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Liquid.NET.Tests.Filters.Strings
{
    [TestFixture]
    public class HandleizeFilterTests
    {
        [Test]
        public void It_SHould_Handleize_Text()
        {
            // Act
            var result = RenderingHelper.RenderTemplate("Result : {{ '100% M & Ms!!!' | handleize }}");

            // Assert
            Assert.That(result, Is.EqualTo("Result : 100-m-ms"));

        }
    }
}
