using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.EntityFrameworkCore;
using Profitr.Api.Data;
using Profitr.Api.Data.Entities;
using Profitr.Api.Models;

namespace Profitr.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapGet("/login", (string? returnUrl) =>
        {
            var properties = new AuthenticationProperties
            {
                RedirectUri = returnUrl ?? "/api/auth/callback"
            };
            return Results.Challenge(properties, [GoogleDefaults.AuthenticationScheme]);
        });

        group.MapGet("/callback", async (HttpContext ctx, ProfitrDbContext db) =>
        {
            var result = await ctx.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            if (!result.Succeeded)
                return Results.Redirect("/?error=auth_failed");

            var googleId = result.Principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            var email = result.Principal?.FindFirstValue(ClaimTypes.Email) ?? "";
            var name = result.Principal?.FindFirstValue(ClaimTypes.Name);
            var avatar = result.Principal?.FindFirstValue("picture");

            // Find or create user
            var user = await db.Users.Include(u => u.Portfolios)
                .FirstOrDefaultAsync(u => u.GoogleSubjectId == googleId);

            if (user == null)
            {
                user = new User
                {
                    Email = email,
                    Name = name,
                    AvatarUrl = avatar,
                    GoogleSubjectId = googleId
                };
                db.Users.Add(user);

                // Create default portfolio
                var portfolio = new Portfolio
                {
                    UserId = user.Id,
                    Name = "My Portfolio",
                    IsDefault = true
                };
                db.Portfolios.Add(portfolio);

                await db.SaveChangesAsync();
            }
            else
            {
                // Update profile info
                user.Name = name;
                user.AvatarUrl = avatar;
                user.Email = email;
                await db.SaveChangesAsync();
            }

            return Results.Redirect("/dashboard");
        });

        group.MapGet("/me", async (HttpContext ctx, ProfitrDbContext db) =>
        {
            var googleId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(googleId))
                return Results.Unauthorized();

            var user = await db.Users.FirstOrDefaultAsync(u => u.GoogleSubjectId == googleId);
            if (user == null)
                return Results.Unauthorized();

            return Results.Ok(new UserInfo(user.Id, user.Email, user.Name, user.AvatarUrl, user.DisplayCurrency));
        }).RequireAuthorization();

        group.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok();
        });

        group.MapPut("/settings", async (HttpContext ctx, ProfitrDbContext db, UpdateSettingsRequest req) =>
        {
            var googleId = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await db.Users.FirstOrDefaultAsync(u => u.GoogleSubjectId == googleId);
            if (user == null) return Results.Unauthorized();

            user.DisplayCurrency = req.DisplayCurrency;
            await db.SaveChangesAsync();

            return Results.Ok(new UserInfo(user.Id, user.Email, user.Name, user.AvatarUrl, user.DisplayCurrency));
        }).RequireAuthorization();
    }
}

public record UpdateSettingsRequest(string DisplayCurrency);
