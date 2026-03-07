using Ecommerce.API.Exceptions;
using Ecommerce.API.Responses;
using System.Net;
using System.Text.Json;
using System.Diagnostics;

namespace Ecommerce.API.Middleware;

public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, ex.Message);

            await HandleExceptionAsync(context, ex);
        }
    }

    private static Task HandleExceptionAsync(
        HttpContext context,
        Exception exception)
    {
        var statusCode = (int)HttpStatusCode.InternalServerError;
        var errorCode = "INTERNAL_SERVER_ERROR";
        var message = "An unexpected error occurred";

        if (exception is BaseException baseException)
        {
            statusCode = baseException.StatusCode;
            errorCode = baseException.ErrorCode;
            message = baseException.Message;
        }

        var traceId = Activity.Current?.Id ?? context.TraceIdentifier;

        var response = new ErrorResponse
        {
            Success = false,
            ErrorCode = errorCode,
            Message = message,
            Path = context.Request.Path,
            TraceId = traceId,
            Timestamp = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(response);

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        return context.Response.WriteAsync(json);
    }
}