namespace Ecommerce.Domain.Exceptions;

public class BusinessException : BaseException
{
    public BusinessException(string message) : base(message, 400, "BAD_REQUEST")
    {
    }
}
