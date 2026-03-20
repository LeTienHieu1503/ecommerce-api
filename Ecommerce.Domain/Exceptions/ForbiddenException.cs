namespace Ecommerce.Domain.Exceptions;

public class ForbiddenException(string message = "Forbidden")
    : BaseException(message, 403, "FORBIDDEN");
