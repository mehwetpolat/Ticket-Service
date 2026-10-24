namespace TickeTask.Application.Common;

public class ApiException : Exception
{
    public int StatusCode { get; }

    public ApiException(string message, int statusCode = 500) : base(message)
    {
        StatusCode = statusCode;
    }

    public static ApiException BadRequest(string message) => new(message, 400);
    public static ApiException Unauthorized(string message = "Oturum gerekli.") => new(message, 401);
    public static ApiException Forbidden(string message = "Bu işlem için yönetici yetkisi gerekli.") => new(message, 403);
    public static ApiException NotFound(string message) => new(message, 404);
    public static ApiException Conflict(string message) => new(message, 409);
    public static ApiException TooLarge(string message) => new(message, 413);
    public static ApiException Unsupported(string message) => new(message, 503);
}
