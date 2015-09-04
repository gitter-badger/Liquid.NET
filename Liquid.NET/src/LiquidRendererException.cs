using System;
using System.Collections.Generic;
using System.Linq;

namespace Liquid.NET
{
    public class LiquidRendererException : Exception
    {
        private readonly IList<LiquidError> _liquidErrors;

        public LiquidRendererException(IList<LiquidError> liquidErrors)
        {
            _liquidErrors = liquidErrors;
        }

        public IList<LiquidError> LiquidErrors {
            get { return _liquidErrors; }
        }

        public override string Message { get { return String.Join("; ", _liquidErrors.Select(x => x.Message)); } }
    }
}