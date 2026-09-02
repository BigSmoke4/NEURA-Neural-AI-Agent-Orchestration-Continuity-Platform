using Microsoft.AspNetCore.Identity;
using Neura.Infrastructure.Persistence;

namespace Neura.Web.Startup;

/// <summary>
/// Ensures the four roles referenced by the authorization policies in
/// Program.cs (Admin, Operator, User, Auditor) exist before any
/// [Authorize(Policy=...)] check runs against them. Run once at startup.
/// </summary>
public static class RoleSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in new[] { "Admin", "Operator", "User", "Auditor" })
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole<Guid>(role));
        }
    }
}
