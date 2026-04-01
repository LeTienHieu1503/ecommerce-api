using Ecommerce.API.Responses;
using Ecommerce.Application.Exceptions;
using Ecommerce.Domain.Enums;
using Ecommerce.Domain.Exceptions;
using System;
using System.Net;
using System.Text.Json;
using Stripe;

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
            var path = context.Request.Path;
            var traceId = context.TraceIdentifier;

            if (ex is BaseException baseException)
            {
                if (baseException.StatusCode >= 500)
                {
                    _logger.LogError(ex,
                        "Server error. Path: {Path}, TraceId: {TraceId}",
                        path, traceId);
                }
                else
                {
                    _logger.LogWarning(
                        "Client error: {Message}. Path: {Path}, TraceId: {TraceId}",
                        baseException.Message,
                        path,
                        traceId);
                }
            }
            else if (ex is StripeException stripeEx)
            {
                _logger.LogWarning(
                    "Stripe error: {Message}. Path: {Path}, TraceId: {TraceId}",
                    stripeEx.StripeError?.Message ?? stripeEx.Message,
                    path,
                    traceId);
            }
            else
            {
                _logger.LogError(ex,
                    "Unhandled exception. Path: {Path}, TraceId: {TraceId}",
                    path,
                    traceId);
            }

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

        switch (exception)
        {
            case DeviceValidationException dvEx:
                (statusCode, errorCode, message) = dvEx.Reason switch
                {
                    DeviceValidationResult.MissingHeader => (400, "DEVICE_HEADER_MISSING", "X-Device-Id header is required"),
                    DeviceValidationResult.DeviceMismatch => (401, "DEVICE_MISMATCH", "Token used from unrecognized device"),
                    DeviceValidationResult.SessionRevoked => (401, "SESSION_REVOKED", "Session has been revoked. Please login again"),
                    DeviceValidationResult.SessionRotated => (401, "SESSION_ROTATED", "Session was replaced. Please re-authenticate"),
                    _ => (401, "DEVICE_INVALID", "Device validation failed")
                };
                break;

            case BaseException baseException:
                statusCode = baseException.StatusCode;
                errorCode = baseException.ErrorCode;
                message = baseException.Message;
                break;

            case UnauthorizedAccessException:
                statusCode = (int)HttpStatusCode.Forbidden;
                errorCode = "FORBIDDEN";
                message = exception.Message;
                break;

            case ArgumentException:
                statusCode = (int)HttpStatusCode.BadRequest;
                errorCode = "BAD_REQUEST";
                message = exception.Message;
                break;

            case KeyNotFoundException:
                statusCode = (int)HttpStatusCode.NotFound;
                errorCode = "NOT_FOUND";
                message = exception.Message;
                break;

            case StripeException stripeEx:
                statusCode = (int)HttpStatusCode.BadGateway;
                errorCode = "STRIPE_ERROR";
                message = stripeEx.StripeError?.Message ?? stripeEx.Message;
                break;
        }

        var response = new ErrorResponse
        {
            statusCode = statusCode,
            Success = false,
            ErrorCode = errorCode,
            Message = message,
        };

        var json = JsonSerializer.Serialize(response);

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = statusCode;

        return context.Response.WriteAsync(json);
    }
}
