using System.ComponentModel.DataAnnotations;

namespace Neura.Web.ViewModels;

public sealed class MemoryEditViewModel
{
    public Guid MemoryId { get; set; }

    public Guid ProjectId { get; set; }

    [Required]
    public string Type { get; set; } = string.Empty;

    [Required]
    [StringLength(4_000, MinimumLength = 1)]
    public string Content { get; set; } = string.Empty;

    [Range(0d, 1d)]
    public double Importance { get; set; }

    [Range(0d, 1d)]
    public double Confidence { get; set; }

    [StringLength(512)]
    public string? Tags { get; set; }

    /// <summary>When selected, editing preserves the precise existing expiry.</summary>
    public bool KeepExpiration { get; set; }

    public int? RetentionDays { get; set; }

    public DateTime? CurrentExpirationUtc { get; set; }
}
