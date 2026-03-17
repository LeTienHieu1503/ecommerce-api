namespace Ecommerce.API.Responses;

public class ApiResponse<T>
{
    public int statusCode { get; set; }

    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public T? Data { get; set; }

    public ApiResponse(int code, bool success, string message, T? data)
    {
        statusCode = code;
        Success = success;
        Message = message;
        Data = data;
    }
}
