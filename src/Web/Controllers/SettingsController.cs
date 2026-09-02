using Microsoft.AspNetCore.Mvc;
using Neura.Modules.ContextManagement.Domain;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authorization;

namespace Neura.Web.Controllers;

/// <summary>Settings (screen 16): system and orchestration configuration.</summary>
[Authorize(Policy = "ManageSystem")]
public class SettingsController : Controller
{
    private readonly ContextThresholdOptions _thresholds;
    public SettingsController(ContextThresholdOptions thresholds) => _thresholds = thresholds;

    public IActionResult Index() => View(_thresholds);
}
