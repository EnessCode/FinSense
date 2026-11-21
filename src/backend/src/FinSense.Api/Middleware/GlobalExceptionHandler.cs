using System.Diagnostics;
using FinSense.Application.Common.Exceptions;
using FinSense.Domain.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FinSense.Api.Middleware;

public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var isEnglish = IsEnglishLocale(httpContext);

        var (statusCode, errorCode, title, detail, errors) = MapException(exception, isEnglish);

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception occurred for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            _logger.LogWarning("Handled {ErrorCode} ({StatusCode}) on {Method} {Path}: {Detail}", errorCode, statusCode, httpContext.Request.Method, httpContext.Request.Path, detail);
        }

        var slug = errorCode.ToLowerInvariant().Replace('_', '-');
        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        var problemDetails = new ProblemDetails
        {
            Type = $"https://finsense.app/errors/{slug}",
            Title = title,
            Status = statusCode,
            Detail = detail,
            Instance = httpContext.Request.Path
        };

        problemDetails.Extensions["errorCode"] = errorCode;
        problemDetails.Extensions["traceId"] = traceId;
        problemDetails.Extensions["errors"] = errors ?? new Dictionary<string, string[]>();

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/problem+json; charset=utf-8";

        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
        return true;
    }

    private static (int StatusCode, string ErrorCode, string Title, string Detail, IDictionary<string, string[]>? Errors)
        MapException(Exception exception, bool isEnglish)
    {
        return exception switch
        {
            ApplicationValidationException validationEx => (
                StatusCodes.Status400BadRequest,
                validationEx.ErrorCode,
                isEnglish ? "Validation Error" : "Doğrulama Hatası",
                isEnglish ? "One or more validation errors occurred." : validationEx.Message,
                validationEx.Errors),

            ValidationException fluentValidationEx => (
                StatusCodes.Status400BadRequest,
                "VALIDATION_FAILED",
                isEnglish ? "Validation Error" : "Doğrulama Hatası",
                isEnglish ? "One or more validation errors occurred." : "Bir veya daha fazla doğrulama hatası oluştu.",
                fluentValidationEx.Errors
                    .GroupBy(e => e.PropertyName, e => e.ErrorMessage)
                    .ToDictionary(g => g.Key, g => g.Distinct().ToArray())),

            DomainException domainEx => (
                StatusCodes.Status400BadRequest,
                domainEx.ErrorCode,
                isEnglish ? "Business Rule Violation" : "İş Kuralı İhlali",
                domainEx.Message,
                null),

            ConflictException conflictEx => (
                StatusCodes.Status409Conflict,
                conflictEx.ErrorCode,
                isEnglish ? "Conflict" : "Çakışma Hatası",
                conflictEx.Message,
                null),

            UnauthorizedException unauthorizedEx => (
                StatusCodes.Status401Unauthorized,
                unauthorizedEx.ErrorCode,
                isEnglish ? "Unauthorized" : "Kimlik Doğrulama Hatası",
                unauthorizedEx.Message,
                null),

            UnauthorizedAccessException => (
                StatusCodes.Status401Unauthorized,
                "UNAUTHORIZED",
                isEnglish ? "Unauthorized" : "Kimlik Doğrulama Hatası",
                isEnglish ? "Authentication is required to access this resource." : "Bu kaynağa erişmek için oturum açmalısınız.",
                null),

            ForbiddenAccessException forbiddenEx => (
                StatusCodes.Status403Forbidden,
                forbiddenEx.ErrorCode,
                isEnglish ? "Forbidden" : "Yetkisiz İşlem",
                forbiddenEx.Message,
                null),

            NotFoundException notFoundEx => (
                StatusCodes.Status404NotFound,
                notFoundEx.ErrorCode,
                isEnglish ? "Resource Not Found" : "Kaynak Bulunamadı",
                notFoundEx.Message,
                null),

            _ => (
                StatusCodes.Status500InternalServerError,
                "INTERNAL_SERVER_ERROR",
                isEnglish ? "Unexpected Server Error" : "Beklenmeyen Sunucu Hatası",
                isEnglish ? "An unexpected error occurred while processing your request." : "İsteğiniz işlenirken beklenmeyen bir hata oluştu.",
                null)
        };
    }

    private static bool IsEnglishLocale(HttpContext httpContext)
    {
        var acceptLanguage = httpContext.Request.Headers.AcceptLanguage.ToString();
        return !string.IsNullOrWhiteSpace(acceptLanguage) &&
               acceptLanguage.StartsWith("en", StringComparison.OrdinalIgnoreCase);
    }
}
