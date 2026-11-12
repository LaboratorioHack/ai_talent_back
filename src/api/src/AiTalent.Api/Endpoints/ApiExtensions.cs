using System.Security.Claims;
using AiTalent.Application.Common;
using AiTalent.Domain.Enums;

namespace AiTalent.Api.Endpoints;

internal static class RateLimitPolicies
{
    public const string InviteJoin = "invite-join";
    public const string CandidateApply = "candidate-apply";
    public const string PublicRead = "public-read";
}

internal static class ApiExtensions
{
    public static Guid ToUserId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue("sub") ?? throw new InvalidOperationException("Access token has no subject."));

    public static Actor ToActor(this ClaimsPrincipal user) =>
        new(user.ToUserId(), user.FindFirstValue("aal") == "aal2");

    public static string RoleName(StaffRole role) => role.ToString().ToUpperInvariant();

    public static bool TryParseRole(string value, out StaffRole role) =>
        Enum.TryParse(value, ignoreCase: true, out role) && Enum.IsDefined(role);

    public static bool TryParseVacancyStatus(string value, out JobOpeningStatus status) =>
        Enum.TryParse(value, ignoreCase: true, out status) && Enum.IsDefined(status);

    public static bool TryParseRequirementType(string value, out RequirementType type) =>
        Enum.TryParse(value, ignoreCase: true, out type) && Enum.IsDefined(type);

    public static CompanyResponse ToResponse(this Membership membership) =>
        new(membership.CompanyId, membership.CompanyName, membership.Sector, membership.Size, RoleName(membership.Role));

    public static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult> onSuccess)
    {
        if (result.IsSuccess)
        {
            return onSuccess(result.Value!);
        }

        var error = result.Error!;
        var statusCode = error.Kind switch
        {
            ErrorKind.Invalid => StatusCodes.Status400BadRequest,
            ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            ErrorKind.TooManyRequests => StatusCodes.Status429TooManyRequests,
            ErrorKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError,
        };

        return Results.Problem(detail: error.Message, statusCode: statusCode);
    }
}
