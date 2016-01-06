using System;
using System.Runtime.Serialization;

namespace ZLR.Debugging
{
    [Serializable]
    internal class DebuggerException : Exception
    {
        public DebuggerException()
        {
        }

        public DebuggerException(string message) : base(message)
        {
        }

        public DebuggerException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected DebuggerException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}