using System.Net;

namespace AiTalent.Application.Common;

/// <summary>Maps AI Engine failures to API errors: 400/422 from the engine are Invalid, anything else Unavailable.</summary>
internal static class EngineCall
{
    public static async Task<Result<T>> RunAsync<T>(Func<Task<T>> call, string invalidMessage, CancellationToken ct)
    {
        try
        {
            return Result<T>.Success(await call());
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity)
        {
            return Result<T>.Failure(ErrorKind.Invalid, invalidMessage);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Result<T>.Failure(ErrorKind.Unavailable, "The AI engine is not available right now. Try again later.");
        }
    }
}
