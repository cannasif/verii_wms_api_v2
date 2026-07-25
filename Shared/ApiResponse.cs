namespace verii_wms_api_v2.Shared;
public sealed record ApiResponse<T>(bool Success, T? Data, string? Message = null, string? TraceId = null)
{
    public static ApiResponse<T> Ok(T data, string? message = null) => new(true, data, message);
    public static ApiResponse<T> Error(string message, string? traceId = null) => new(false, default, message, traceId);
}
