using System.Security.Claims;
using AiTalent.Application.Features.Staff;
using AiTalent.Domain.Enums;

namespace AiTalent.Api.Endpoints;

internal static class StaffEndpoints
{
    public static IEndpointRouteBuilder MapStaffEndpoints(this IEndpointRouteBuilder app)
    {
        var staff = app.MapGroup("/api/staff");

        staff.MapGet("", async (ClaimsPrincipal user, StaffService service, CancellationToken ct) =>
                (await service.ListAsync(user.ToActor(), ct)).ToHttpResult(members => Results.Ok(
                    members.Select(m => new StaffResponse(m.UserId, m.Name, ApiExtensions.RoleName(m.Role), m.Active)))))
            .WithName("ListStaff")
            .WithSummary("List staff")
            .WithDescription("Lists the staff of the caller's company, ordered by name. Admins only, and the session must have completed MFA.")
            .Produces<IEnumerable<StaffResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        staff.MapPatch("{userId:guid}", async (Guid userId, UpdateStaffRequest request, ClaimsPrincipal user, StaffService service, CancellationToken ct) =>
            {
                StaffRole? role = null;
                if (request.Role is not null)
                {
                    if (!ApiExtensions.TryParseRole(request.Role, out var parsedRole))
                    {
                        return Results.Problem(detail: "Role must be ADMIN or RECRUITER.", statusCode: StatusCodes.Status400BadRequest);
                    }

                    role = parsedRole;
                }

                return (await service.UpdateAsync(user.ToActor(), userId, role, request.Active, ct)).ToHttpResult(m =>
                    Results.Ok(new StaffResponse(m.UserId, m.Name, ApiExtensions.RoleName(m.Role), m.Active)));
            })
            .WithName("UpdateStaff")
            .WithSummary("Change a staff member's role or status")
            .WithDescription("Changes the role (ADMIN or RECRUITER) and/or sets `active` for a staff member in the caller's company. Both fields are optional. Deactivating someone blocks them immediately, even with a valid login. Admins can't change their own role or status. Staff from other companies return 404. Admins only, and the session must have completed MFA.")
            .Produces<StaffResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }
}
