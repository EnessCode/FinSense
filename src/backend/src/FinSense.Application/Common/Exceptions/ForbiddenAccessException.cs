namespace FinSense.Application.Common.Exceptions;

public class ForbiddenAccessException : Exception
{
    public string ErrorCode { get; }

    public ForbiddenAccessException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }
}
