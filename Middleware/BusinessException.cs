using System.Net;

namespace Ecommerce.API.Exceptions;

public class BusinessException : BaseException
{
    public BusinessException(string message)
        : base(message, "BUSINESS_ERROR", (int)HttpStatusCode.Conflict)
    {
    }
}