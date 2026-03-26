namespace Ecommerce.Domain.Exceptions;

public class ConflictException : BaseException
{
    public ConflictException(string message) : base(message, 409, "CONFLICT")
    {
    }
}
