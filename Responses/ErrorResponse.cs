namespace Ecommerce.API.Responses;

public class ErrorResponse
{
    public bool Success { get; set; } = false;

    public string ErrorCode { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string? Path { get; set; }

    public string? TraceId { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}