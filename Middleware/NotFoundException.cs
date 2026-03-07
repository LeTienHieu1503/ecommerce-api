using System.Net;

namespace Ecommerce.API.Exceptions;

public class NotFoundException : BaseException
{
    public NotFoundException(string message)
        : base(message, "NOT_FOUND", (int)HttpStatusCode.NotFound)
    {
    }
}