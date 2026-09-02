using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Neura.Infrastructure.Persistence;
using Neura.Modules.Observability.Domain;

namespace Neura.Web.Controllers;

/// <summary>Authentication and role-aware account registration.</summary>
[EnableRateLimiting("login")]
public class AccountController : Controller
{
    private static readonly HashSet<string> SelfServiceRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "User", "Operator", "Auditor"
    };

    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogger _audit;
    private readonly IConfiguration _configuration;

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IAuditLogger audit,
        IConfiguration configuration)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _audit = audit;
        _configuration = configuration;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string email, string password, string? returnUrl = null, CancellationToken ct = default)
    {
        var result = await _signInManager.PasswordSignInAsync(email, password, isPersistent: false, lockoutOnFailure: true);
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        if (result.Succeeded)
        {
            await _audit.LogAsync(null, "UserLogin", email, "Success", Guid.NewGuid(), ip, ct);
            return string.IsNullOrEmpty(returnUrl) || !Url.IsLocalUrl(returnUrl)
                ? RedirectToAction("Index", "Brain")
                : LocalRedirect(returnUrl);
        }

        await _audit.LogAsync(null, "UserLogin", email, result.IsLockedOut ? "LockedOut" : "Failed", Guid.NewGuid(), ip, ct);
        ModelState.AddModelError(string.Empty, result.IsLockedOut
            ? "Account locked due to repeated failed attempts. Try again later."
            : "Invalid email or password.");
        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [HttpGet]
    [AllowAnonymous]
    [DisableRateLimiting]
    public IActionResult AccessDenied(string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [HttpGet]
    public IActionResult Register()
    {
        ViewBag.RegistrationRoles = new[] { "User", "Operator", "Auditor", "Admin" };
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(
        string email,
        string password,
        string role = "User",
        string? roleCode = null,
        CancellationToken ct = default)
    {
        role = string.IsNullOrWhiteSpace(role) ? "User" : role.Trim();
        var normalizedRole = SelfServiceRoles.Contains(role) ?
            SelfServiceRoles.First(r => r.Equals(role, StringComparison.OrdinalIgnoreCase)) : role;

        if (!SelfServiceRoles.Contains(normalizedRole) && !string.Equals(normalizedRole, "Admin", StringComparison.OrdinalIgnoreCase))
            ModelState.AddModelError("role", "Unknown registration role.");

        // Privileged self-service roles require an explicit server-side code.
        // This keeps role-based signup useful for controlled/demo deployments
        // without allowing an anonymous visitor to create an Admin account.
        if (!string.Equals(normalizedRole, "User", StringComparison.OrdinalIgnoreCase))
        {
            var expectedCode = _configuration[$"Neura:Registration:RoleCodes:{normalizedRole}"];
            if (string.IsNullOrWhiteSpace(expectedCode) || !CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(expectedCode),
                    System.Text.Encoding.UTF8.GetBytes(roleCode ?? string.Empty)))
            {
                ModelState.AddModelError("roleCode", $"A valid registration code is required for the {normalizedRole} role.");
            }
        }

        if (!ModelState.IsValid)
        {
            ViewBag.RegistrationRoles = new[] { "User", "Operator", "Auditor", "Admin" };
            return View();
        }

        var user = new ApplicationUser { UserName = email, Email = email };
        var result = await _userManager.CreateAsync(user, password);

        if (result.Succeeded)
        {
            var roleResult = await _userManager.AddToRoleAsync(user, normalizedRole);
            if (!roleResult.Succeeded)
            {
                await _userManager.DeleteAsync(user);
                foreach (var error in roleResult.Errors)
                    ModelState.AddModelError(string.Empty, error.Description);
                ViewBag.RegistrationRoles = new[] { "User", "Operator", "Auditor", "Admin" };
                return View();
            }

            await _audit.LogAsync(user.Id, "UserRegistered", $"{email}; role={normalizedRole}", "Success", Guid.NewGuid(), HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
            await _signInManager.SignInAsync(user, isPersistent: false);
            return RedirectToAction("Index", "Brain");
        }

        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);

        ViewBag.RegistrationRoles = new[] { "User", "Operator", "Auditor", "Admin" };
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var email = User.Identity?.Name;
        await _signInManager.SignOutAsync();
        await _audit.LogAsync(null, "UserLogout", email, "Success", Guid.NewGuid(), HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        return RedirectToAction(nameof(Login));
    }
}
