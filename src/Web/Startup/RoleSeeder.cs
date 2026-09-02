using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Neura.Web.Startup;

/// <summary>
/// Ensures the four roles referenced by the authorization policies exist
/// before protected requests can be processed. The seed is idempotent and
/// tolerates concurrent application startup against the same database.
/// </summary>
public static class RoleSeeder
{
    private static readonly string[] Roles = { "Admin", "Operator", "User", "Auditor" };

    public static async Task SeedAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        foreach (var roleName in Roles)
        {
            if (await roleManager.RoleExistsAsync(roleName))
                continue;

            try
            {
                var result = await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
                if (!result.Succeeded && !await roleManager.RoleExistsAsync(roleName))
                {
                    throw new InvalidOperationException(
                        $"Unable to seed required role '{roleName}': " +
                        string.Join("; ", result.Errors.Select(e => e.Description)));
                }
            }
            catch (DbUpdateException)
            {
                // Another application instance may have won the race to create
                // this role. Only suppress the exception when that is confirmed.
                if (!await roleManager.RoleExistsAsync(roleName))
                    throw;
            }
        }
    }
}
