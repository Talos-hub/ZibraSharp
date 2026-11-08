using System;

namespace ZibraSharp.Internal.Exceptions
{
    /// <summary>
    /// Error codes mirror the codes used by the original ZibraGo application.
    /// </summary>
    public enum AppErrorCode
    {
        Fatal,
        Read,
        EmptyDirectory,
        Open,
        Create,
        Add,
        Upload,
        Api,
        Parse,
        Config
    }

    /// <summary>
    /// Application-level exception with an operation name and a stable error code.
    /// </summary>
    public sealed class AppException : Exception
    {
        public AppErrorCode Code { get; }
        public string Operation { get; }

        public AppException(AppErrorCode code, string operation, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            Code = code;
            Operation = operation;
        }
    }
}
