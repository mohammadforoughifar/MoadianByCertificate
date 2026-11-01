using Microsoft.AspNetCore.Mvc;
using MoudianMiddleware.Contracts.Common;

namespace MoudianMiddleware.Api.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected ApiResponse<T> Success<T>(T data, string code = ApiResponseCodes.Ok) => new() { IsSuccess = true, Code = code, Data = data, TraceId = HttpContext.TraceIdentifier };
}
