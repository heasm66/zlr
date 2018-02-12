using System;
using System.Runtime.Serialization;
using JetBrains.Annotations;

namespace ZLR.Interfaces.SystemConsole.Debugger
{
    [Serializable]
    internal class DebuggerException : Exception
    {
        public DebuggerException(string message) : base(message)
        {
        }

        protected DebuggerException([NotNull] SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}