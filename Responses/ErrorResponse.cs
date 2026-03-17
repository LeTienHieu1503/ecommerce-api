namespace Ecommerce.API.Responses;

public class ErrorResponse
{
    public int statusCode { get; set; }

    public bool Success { get; set; } = false;

    public string ErrorCode { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

}
