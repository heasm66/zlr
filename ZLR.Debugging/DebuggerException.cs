using System;
using System.Runtime.Serialization;
using JetBrains.Annotations;

namespace ZLR.Debugging
{
    [Serializable]
    internal class DebuggerException : Exception
    {
        public DebuggerException(string message) : base(message)
        {
        }

        public DebuggerException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected DebuggerException([NotNull] SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}