namespace Ecommerce.API.Exceptions;

public abstract class BaseException : Exception
{
    public string ErrorCode { get; }

    public int StatusCode { get; }

    protected BaseException(string message, string errorCode, int statusCode)
        : base(message)
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }
}