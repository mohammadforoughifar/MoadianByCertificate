using FluentValidation;
using MoudianMiddleware.Contracts.Common;
using MoudianMiddleware.CoreLayer.Common.Exceptions;

namespace MoudianMiddleware.Api.Infrastructure;

public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            await next(context).ConfigureAwait(false);
            if (!context.Response.HasStarted && context.Response.StatusCode is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden)
                await WriteAsync(context, context.Response.StatusCode, context.Response.StatusCode == 401 ? ApiResponseCodes.Unauthorized : ApiResponseCodes.Forbidden,
                    context.Response.StatusCode == 401 ? "احراز هویت لازم است." : "دسترسی مجاز نیست.").ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { context.Response.StatusCode = 499; }
        catch (Exception exception)
        {
            if (context.Response.HasStarted) throw;
            var (status, code, message, errors) = exception switch
            {
                ValidationException validation => (400, ApiResponseCodes.ValidationFailed, "درخواست نامعتبر است.", (IReadOnlyList<ApiError>?)validation.Errors.Select(error => new ApiError(error.ErrorMessage, error.PropertyName, error.ErrorCode)).ToArray()),
                EntityNotFoundException => (404, ApiResponseCodes.NotFound, "مورد درخواست یافت نشد.", null),
                BusinessConflictException => (409, ApiResponseCodes.BusinessRuleViolation, "درخواست با وضعیت فعلی تعارض دارد.", null),
                UnauthorizedAccessException => (401, ApiResponseCodes.Unauthorized, "احراز هویت ناموفق است.", null),
                InvalidOperationException => (409, ApiResponseCodes.BusinessRuleViolation, "عملیات با وضعیت فعلی قابل انجام نیست.", null),
                _ => (500, ApiResponseCodes.InternalError, "خطای داخلی سرور رخ داد.", null)
            };
            if (status >= 500) logger.LogError("API failure {TraceId} {ExceptionType}", context.TraceIdentifier, exception.GetType().Name);
            else logger.LogInformation("API rejected {TraceId} {Code}", context.TraceIdentifier, code);
            await WriteAsync(context, status, code, message, errors).ConfigureAwait(false);
        }
    }

    private static async Task WriteAsync(HttpContext context, int status, string code, string message, IReadOnlyList<ApiError>? errors = null)
    {
        context.Response.Clear(); context.Response.StatusCode = status; context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsJsonAsync(new ApiResponse<object> { IsSuccess = false, Code = code, Message = message, Errors = errors, TraceId = context.TraceIdentifier }).ConfigureAwait(false);
    }
}
