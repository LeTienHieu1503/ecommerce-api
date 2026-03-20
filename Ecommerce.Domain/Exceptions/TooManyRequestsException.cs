namespace Ecommerce.Domain.Exceptions;

public sealed class TooManyRequestsException : BaseException
{
    public TooManyRequestsException(string? message = null)
        : base(message ?? "Too many requests", 429, "TOO_MANY_REQUESTS")
    { }
}