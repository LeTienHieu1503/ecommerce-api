namespace Ecommerce.Domain.Exceptions;

public class UnauthorizedException(string message = "Unauthorized")
    : BaseException(message, 401, "UNAUTHORIZED");