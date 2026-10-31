using System.Security.Claims;
using InventoryOps.Api.Hosting;
using InventoryOps.Application.Modules.Identity;
using InventoryOps.Infrastructure.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace InventoryOps.Api.Endpoints;

public sealed record LoginRequest(string Email, string Password);

public sealed record PermissionsResponse(bool CanOperate, bool CanViewActivity, bool CanManageUsers);

public sealed record CurrentUserResponse(string Id, string Email, string DisplayName, string Role, PermissionsResponse Permissions);

internal static class AuthEndpoints
{
    // Hash of a random password, verified for unknown emails so response timing does not reveal
    // whether an account exists.
    private static readonly Lazy<string> DummyHash = new(() =>
        new PasswordHasher<ApplicationUser>().HashPassword(new ApplicationUser(), Guid.NewGuid().ToString()));

    public static void MapAuthEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/auth").WithTags("Auth");

        group.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
            {
                IssueAntiforgeryToken(context, antiforgery);
                return TypedResults.NoContent();
            })
            .AllowAnonymous()
            .WithSummary("Issues the XSRF-TOKEN cookie required for state-changing requests.");

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting(ApiServiceRegistration.LoginRateLimitPolicy)
            .WithSummary("Signs in with email and password and sets the HttpOnly session cookie.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/logout", async (HttpContext context, SignInManager<ApplicationUser> signIn, IAntiforgery antiforgery) =>
            {
                await signIn.SignOutAsync();
                context.User = new ClaimsPrincipal(new ClaimsIdentity());
                IssueAntiforgeryToken(context, antiforgery);
                return TypedResults.NoContent();
            })
            .WithSummary("Ends the session.");

        group.MapGet("/me", async Task<Results<Ok<CurrentUserResponse>, UnauthorizedHttpResult>> (
                HttpContext context, UserManager<ApplicationUser> users, IAntiforgery antiforgery) =>
            {
                var user = await users.GetUserAsync(context.User);
                if (user is null)
                {
                    return TypedResults.Unauthorized();
                }

                IssueAntiforgeryToken(context, antiforgery);
                return TypedResults.Ok(await ToResponseAsync(user, users));
            })
            .WithSummary("Returns the signed-in user, role and permissions; refreshes the CSRF token.");
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext context,
        UserManager<ApplicationUser> users,
        SignInManager<ApplicationUser> signIn,
        IAntiforgery antiforgery,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("InventoryOps.Auth");
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password) || request.Email.Length > 256 || request.Password.Length > 128)
        {
            return InvalidCredentials(context);
        }

        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            users.PasswordHasher.VerifyHashedPassword(new ApplicationUser(), DummyHash.Value, request.Password);
            logger.LogInformation("Login failed: unknown account");
            return InvalidCredentials(context);
        }

        var result = await signIn.PasswordSignInAsync(user, request.Password, isPersistent: false, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            // The client gets one generic message; the reason is only logged (by user id, never password).
            logger.LogWarning(
                "Login failed for user {UserId}: {Reason}", user.Id,
                result.IsLockedOut ? "locked out" : result.IsNotAllowed ? "not allowed (inactive)" : "invalid password");
            return InvalidCredentials(context);
        }

        // Antiforgery tokens are bound to the identity, so a fresh token is issued for the new principal.
        context.User = await signIn.CreateUserPrincipalAsync(user);
        IssueAntiforgeryToken(context, antiforgery);
        logger.LogInformation("User {UserId} signed in", user.Id);
        return TypedResults.Ok(await ToResponseAsync(user, users));
    }

    private static ProblemHttpResult InvalidCredentials(HttpContext context) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Sign-in failed",
            detail: "Invalid email or password, or the account is temporarily locked.",
            extensions: new Dictionary<string, object?> { ["code"] = "invalid_credentials", ["correlationId"] = context.TraceIdentifier });

    internal static void IssueAntiforgeryToken(HttpContext context, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        context.Response.Cookies.Append(
            ApiServiceRegistration.AntiforgeryRequestCookieName,
            tokens.RequestToken!,
            new CookieOptions
            {
                // Readable by the SPA (double-submit); useless to other origins thanks to SameSite=Strict.
                HttpOnly = false,
                SameSite = SameSiteMode.Strict,
                Secure = context.Request.IsHttps,
                Path = "/",
            });
    }

    private static async Task<CurrentUserResponse> ToResponseAsync(ApplicationUser user, UserManager<ApplicationUser> users)
    {
        var roles = await users.GetRolesAsync(user);
        var role = Roles.All.FirstOrDefault(roles.Contains) ?? Roles.Viewer;
        var isAdmin = role == Roles.Admin;
        var canOperate = isAdmin || role == Roles.Manager;
        return new CurrentUserResponse(
            user.Id, user.Email ?? string.Empty, user.DisplayName, role, new PermissionsResponse(canOperate, canOperate, isAdmin));
    }
}
