namespace verii_wms_api_v2.Shared.Application.Exceptions;

public sealed class AppException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public static AppException BadRequest(string message) => new(StatusCodes.Status400BadRequest, message);
    public static AppException Unauthorized(string message) => new(StatusCodes.Status401Unauthorized, message);
    public static AppException Forbidden(string message = "Bu işlem için yetkiniz bulunmuyor.") => new(StatusCodes.Status403Forbidden, message);
    public static AppException NotFound(string message) => new(StatusCodes.Status404NotFound, message);
    public static AppException Conflict(string message) => new(StatusCodes.Status409Conflict, message);
    public static AppException BadGateway(string message) => new(StatusCodes.Status502BadGateway, message);
    public static AppException ServiceUnavailable(string message) => new(StatusCodes.Status503ServiceUnavailable, message);
}
