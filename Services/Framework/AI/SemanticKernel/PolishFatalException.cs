using System;

namespace TM.Services.Framework.AI.SemanticKernel
{
    public sealed class PolishFatalException : Exception
    {
        public PolishFatalException(string message)
            : base(message) { }

        public PolishFatalException(string message, Exception? inner)
            : base(message, inner) { }
    }
}
