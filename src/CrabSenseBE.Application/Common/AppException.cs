namespace CrabSenseBE.Application.Common;

public class AppException : Exception
{
    public int StatusCode { get; }

    public AppException(string message, int statusCode = 400) : base(message)
    {
        StatusCode = statusCode;
    }

    public static AppException NotFound(string entity) => new($"{entity} not found.", 404);
    public static AppException NotFoundMessage(string message)=> new(message, 404);
    public static AppException Unauthorized(string message = "Unauthorized.") => new(message, 401);
    public static AppException Forbidden(string message = "Access denied.") => new(message, 403);
    public static AppException Conflict(string message) => new(message, 409);
    public static AppException BadRequest(string message) => new(message, 400);
}
