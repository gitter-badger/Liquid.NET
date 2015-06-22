using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Tasks;
using Liquid.NET.Expressions;

namespace Liquid.NET.Utils
{

    // SEE: https://github.com/louthy/csharp-monad/blob/master/CSharpMonad/src/option-strict/OptionStrict.cs
    public class None<T> : Option<T>
    {
        public override bool HasValue { get { return false; } }

        public override T Value
        {
            get
            {
                //return null;
                throw new InvalidOperationException("Option<T>.None has no value");
            }
        }

    }

    public class Some<T> : Option<T>
    {
        private readonly T _val;

        public Some(T val)
        {
            _val = val;
        }

        public override T Value
        {
            get { return _val; }
        }

        public override bool HasValue { get { return true; } }
    }

    public abstract class Option<T>
    {
        public static Option<T> Empty()
        {
            return new None<T>();
        }

        public static Option<T> Apply(T t)
        {
            return new Some<T>(t);
        }

        public abstract T Value { get; }

        public abstract bool HasValue { get; }

    }
}