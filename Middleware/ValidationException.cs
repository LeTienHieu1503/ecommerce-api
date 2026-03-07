using System.Net;

namespace Ecommerce.API.Exceptions;

public class ValidationException : BaseException
{
    public ValidationException(string message)
        : base(message, "VALIDATION_ERROR", (int)HttpStatusCode.BadRequest)
    {
    }
}