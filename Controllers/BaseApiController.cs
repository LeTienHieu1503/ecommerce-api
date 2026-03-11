using Microsoft.AspNetCore.Mvc;
using Ecommerce.API.Responses;

namespace Ecommerce.API.Controllers;

[ApiController]
public class BaseApiController : ControllerBase
{
    protected IActionResult Success<T>(T data, string message = "Get successfully")
    {
        var response = new ApiResponse<T>(
            StatusCodes.Status200OK,
            true,
            message,
            data
        );

        return Ok(response);
    }

    protected IActionResult CreatedSuccess<T>(T data, string message = "Created successfully")
    {
        var response = new ApiResponse<T>(
            StatusCodes.Status201Created,
            true,
            message,
            data
        );

        return StatusCode(StatusCodes.Status201Created, response);

    }

    protected IActionResult UpdateSuccess<T>(T data, string message = "Updated successfully")
    {
        var response = new ApiResponse<T>(
            StatusCodes.Status200OK,
            true,
            message,
            data
        );

        return Ok(response);
    }

    protected IActionResult DeleteSuccess(string message = "Deleted successfully")
    {
        var response = new ApiResponse<object>(
            StatusCodes.Status200OK,
            true,
            message,
            null
        );

        return Ok(response);
    }
}