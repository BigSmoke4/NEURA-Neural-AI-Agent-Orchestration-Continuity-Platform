using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Neura.Web.RateLimiting;
using Microsoft.Extensions.Options;
using Neura.Infrastructure.Persistence;
using Neura.Modules.Observability.Domain;
using Neura.Modules.Orchestration.Application;
using Neura.Web.Security;

namespace Neura.Web.Controllers;

/// <summary>Authentication and role-aware account registration.</summary>
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
    private readonly IEmailSender _emailSender;
    private readonly IdentityOptions _identityOptions;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IAuditLogger audit,
        IConfiguration configuration,
        IEmailSender emailSender,
        IOptions<IdentityOptions> identityOptions,
        ILogger<AccountController> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _audit = audit;
        _configuration = configuration;
        _emailSender = emailSender;
        _identityOptions = identityOptions.Value;
        _logger = logger;
    }

    [HttpGet]
    [EnableRateLimiting(NeuraRateLimitPolicies.Login)]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(NeuraRateLimitPolicies.Login)]
    public async Task<IActionResult> Login(string email, string password, string? returnUrl = null, CancellationToken ct = default)
    {
        var result = await _signInManager.PasswordSignInAsync(email, password, isPersistent: false, lockoutOnFailure: true);
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        if (result.Succeeded)
        {
            var signedInUserId = (await _userManager.FindByEmailAsync(email))?.Id;
            await _audit.LogAsync(signedInUserId, "UserLogin", email, "Success", Guid.NewGuid(), ip, ct);
            return string.IsNullOrEmpty(returnUrl) || !Url.IsLocalUrl(returnUrl)
                ? Redirect("/Brain")
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
    [EnableRateLimiting(NeuraRateLimitPolicies.Login)]
    public IActionResult Register()
    {
        ViewBag.RegistrationRoles = new[] { "User", "Operator", "Auditor", "Admin" };
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(NeuraRateLimitPolicies.Login)]
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
            if (_identityOptions.SignIn.RequireConfirmedAccount)
            {
                await SendEmailConfirmationAsync(user, ct);
                return RedirectToAction(nameof(RegisterConfirmation));
            }

            await _signInManager.SignInAsync(user, isPersistent: false);
            return Redirect("/Brain");
        }

        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);

        ViewBag.RegistrationRoles = new[] { "User", "Operator", "Auditor", "Admin" };
        return View();
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult RegisterConfirmation() => View();

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(NeuraRateLimitPolicies.AccountRecovery)]
    public async Task<IActionResult> ResendConfirmation(string? email, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(email))
        {
            var user = await _userManager.FindByEmailAsync(email.Trim());
            if (user is not null && !user.EmailConfirmed)
                await SendEmailConfirmationAsync(user, ct);
        }

        // Keep the response identical for existing and unknown addresses.
        return RedirectToAction(nameof(RegisterConfirmation));
    }

    [HttpGet]
    [AllowAnonymous]
    [EnableRateLimiting(NeuraRateLimitPolicies.AccountRecovery)]
    public IActionResult ConfirmEmail(Guid userId, string? token)
    {
        PreventSensitiveResponseCaching();
        ViewBag.UserId = userId;
        ViewBag.Token = token;
        ViewBag.CanConfirm = userId != Guid.Empty && !string.IsNullOrWhiteSpace(token);
        return View();
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(NeuraRateLimitPolicies.AccountRecovery)]
    public async Task<IActionResult> ConfirmEmail(Guid userId, string? token, CancellationToken ct)
    {
        PreventSensitiveResponseCaching();
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(token))
        {
            ViewBag.CanConfirm = false;
            ViewBag.ConfirmationFailed = true;
            return View();
        }

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            ViewBag.CanConfirm = false;
            ViewBag.ConfirmationFailed = true;
            return View();
        }

        if (!user.EmailConfirmed)
        {
            var result = await _userManager.ConfirmEmailAsync(user, token);
            if (!result.Succeeded)
            {
                ViewBag.CanConfirm = false;
                ViewBag.ConfirmationFailed = true;
                return View();
            }

            await _audit.LogAsync(user.Id, "UserEmailConfirmed", user.Id.ToString(), "Success", Guid.NewGuid(), HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        }

        return RedirectToAction(nameof(EmailConfirmed));
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult EmailConfirmed() => View();

    [HttpGet]
    [AllowAnonymous]
    [EnableRateLimiting(NeuraRateLimitPolicies.AccountRecovery)]
    public IActionResult ForgotPassword() => View();

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(NeuraRateLimitPolicies.AccountRecovery)]
    public async Task<IActionResult> ForgotPassword(string? email, CancellationToken ct)
    {
        var normalizedEmail = email?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedEmail) || !new EmailAddressAttribute().IsValid(normalizedEmail))
        {
            ModelState.AddModelError(nameof(email), "Enter a valid email address.");
            return View();
        }

        var user = await _userManager.FindByEmailAsync(normalizedEmail!);
        if (user is not null &&
            (!_identityOptions.SignIn.RequireConfirmedAccount || user.EmailConfirmed))
        {
            await SendPasswordResetAsync(user, ct);
        }

        // Return the same response content/status for unknown and eligible addresses.
        return RedirectToAction(nameof(ForgotPasswordConfirmation));
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ForgotPasswordConfirmation() => View();

    [HttpGet]
    [AllowAnonymous]
    [EnableRateLimiting(NeuraRateLimitPolicies.AccountRecovery)]
    public IActionResult ResetPassword(Guid userId, string? token)
    {
        PreventSensitiveResponseCaching();
        ViewBag.UserId = userId;
        ViewBag.Token = token;
        return View();
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(NeuraRateLimitPolicies.AccountRecovery)]
    public async Task<IActionResult> ResetPassword(
        Guid userId,
        string? token,
        string? password,
        string? confirmPassword,
        CancellationToken ct)
    {
        PreventSensitiveResponseCaching();
        ViewBag.UserId = userId;
        ViewBag.Token = token;

        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(token))
            ModelState.AddModelError(string.Empty, "The password reset link is invalid or expired.");
        if (string.IsNullOrEmpty(password))
            ModelState.AddModelError(nameof(password), "Enter a new password.");
        if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
            ModelState.AddModelError(nameof(confirmPassword), "The passwords do not match.");
        if (!ModelState.IsValid)
            return View();

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "The password reset link is invalid or expired.");
            return View();
        }

        var result = await _userManager.ResetPasswordAsync(user, token!, password!);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View();
        }

        await _audit.LogAsync(user.Id, "UserPasswordReset", user.Id.ToString(), "Success", Guid.NewGuid(), HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        return RedirectToAction(nameof(ResetPasswordConfirmation));
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult ResetPasswordConfirmation() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var email = User.Identity?.Name;
        var userId = ResourceScope.UserId(User);
        await _signInManager.SignOutAsync();
        await _audit.LogAsync(userId, "UserLogout", email, "Success", Guid.NewGuid(), HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        return RedirectToAction(nameof(Login));
    }

    private async Task SendEmailConfirmationAsync(ApplicationUser user, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(user.Email))
            return;

        try
        {
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            var callbackUrl = BuildAccountUrl(nameof(ConfirmEmail), new { userId = user.Id, token });
            if (callbackUrl is null)
            {
                _logger.LogError("Could not construct the email-confirmation URL for user {UserId}.", user.Id);
                return;
            }

            await _emailSender.SendAsync(
                user.Email,
                "Confirm your NEURA account",
                $"Confirm your email address by opening this link, then submitting the confirmation form:\n\n{callbackUrl}",
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Email confirmation delivery failed for user {UserId}.", user.Id);
        }
    }

    private async Task SendPasswordResetAsync(ApplicationUser user, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(user.Email))
            return;

        try
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var callbackUrl = BuildAccountUrl(nameof(ResetPassword), new { userId = user.Id, token });
            if (callbackUrl is null)
            {
                _logger.LogError("Could not construct the password-reset URL for user {UserId}.", user.Id);
                return;
            }

            await _emailSender.SendAsync(
                user.Email,
                "Reset your NEURA password",
                $"A password reset was requested for your NEURA account. If you requested this, open the link below and choose a new password. The link expires in three hours.\n\n{callbackUrl}\n\nIf you did not request this, you can ignore this email.",
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Password-reset email delivery failed for user {UserId}.", user.Id);
        }
    }

    private string? BuildAccountUrl(string action, object routeValues)
    {
        var publicOriginText = _configuration["Neura:PublicOrigin"];
        if (Uri.TryCreate(publicOriginText, UriKind.Absolute, out var publicOrigin) &&
            (publicOrigin.Scheme == Uri.UriSchemeHttps || publicOrigin.Scheme == Uri.UriSchemeHttp) &&
            publicOrigin.AbsolutePath == "/" && string.IsNullOrEmpty(publicOrigin.Query) &&
            string.IsNullOrEmpty(publicOrigin.Fragment) && string.IsNullOrEmpty(publicOrigin.UserInfo))
        {
            return Url.Action(action, "Account", routeValues, publicOrigin.Scheme, publicOrigin.Authority);
        }

        return Url.Action(action, "Account", routeValues, Request.Scheme, Request.Host.Value);
    }

    private void PreventSensitiveResponseCaching()
    {
        Response.Headers["Cache-Control"] = "no-store, no-cache";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Referrer-Policy"] = "no-referrer";
    }
}
