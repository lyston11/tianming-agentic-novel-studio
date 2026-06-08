using System;

namespace TM.Services.Framework.AI.SemanticKernel
{
    public sealed class ManualInterventionRequiredException : Exception
    {
        public ManualInterventionRequiredException(string message)
            : base(message) { }

        public ManualInterventionRequiredException(string message, Exception? inner)
            : base(message, inner) { }
    }
}
