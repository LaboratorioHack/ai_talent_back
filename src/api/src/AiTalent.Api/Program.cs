using System.Security.Claims;
using System.Threading.RateLimiting;
using AiTalent.Api.Endpoints;
using AiTalent.Api.Extensions;
using AiTalent.Api.OpenApi;
using AiTalent.Application;
using AiTalent.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Local dev: read ai_talent_back/.env.development (same file the AI Engine uses).
builder.AddDotEnvForDevelopment();

// OWASP A06 (Insecure Design): bound request size (CV uploads are capped at 10 MiB).
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = 10 * 1024 * 1024);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

// OWASP A07 (Authentication Failures): only JWTs issued by our Supabase project are accepted.
// Keys come from {SUPABASE_URL}/auth/v1/.well-known/jwks.json (asymmetric signing keys).
var supabaseUrl = builder.Configuration["SUPABASE_URL"]?.TrimEnd('/');
if (string.IsNullOrWhiteSpace(supabaseUrl) && !builder.Environment.IsDevelopment())
{
    // OWASP A02 (Misconfiguration): fail fast instead of running with auth half-configured.
    throw new InvalidOperationException("SUPABASE_URL is required outside Development.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        if (string.IsNullOrWhiteSpace(supabaseUrl))
        {
            return;
        }

        options.Authority = $"{supabaseUrl}/auth/v1";
        // Local Supabase (`supabase start`) serves plain http; production must be https.
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.MapInboundClaims = false;
        options.TokenValidationParameters.ValidIssuer = $"{supabaseUrl}/auth/v1";
        options.TokenValidationParameters.ValidAudience = "authenticated";
        options.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
    });

// OWASP A01 (Broken Access Control): deny by default. Every endpoint requires an authenticated
// user unless it explicitly opts out with .AllowAnonymous().
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// OWASP A06/A07: rate limit per user (or per IP when anonymous). Protects the LLM budget too.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue("sub") ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 100, Window = TimeSpan.FromMinutes(1) }));

    // Invite codes are the only secret a user can try to guess, so joining gets a strict limit.
    options.AddPolicy<string>(RateLimitPolicies.InviteJoin, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue("sub") ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(15) }));

    // Public candidate applications: strict per-IP limit to reduce abuse.
    options.AddPolicy<string>(RateLimitPolicies.CandidateApply, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(15) }));

    // Public vacancy pages are read-only but anonymous, so they get a per-IP read limit.
    options.AddPolicy<string>(RateLimitPolicies.PublicRead, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));
});

// Behind Caddy: trust X-Forwarded-* so rate limiting sees the real client IP.
// Safe only because the API is not published to the internet (compose `expose`, not `ports`).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// OWASP A10 (Mishandling of Exceptional Conditions): RFC 7807 responses, no stack traces leaked.
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecurityDocumentTransformer>();
    options.AddOperationTransformer<BearerSecurityOperationTransformer>();
});

// No CORS policy on purpose: the web app calls the API through the same origin (Caddy /api).

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();

// Docs are only served in Development, so production never exposes the API surface.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference(options => options.WithTitle("AI Talent Scout — API")).AllowAnonymous();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthEndpoints();
app.MapAuthEndpoints();
app.MapCompanyEndpoints();
app.MapStaffEndpoints();
app.MapVacancyEndpoints();
app.MapEvaluationEndpoints();
app.MapDashboardEndpoints();

await app.RunAsync();

public partial class Program;
