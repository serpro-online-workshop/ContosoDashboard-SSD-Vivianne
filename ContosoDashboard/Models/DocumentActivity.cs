using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ContosoDashboard.Models;

public class DocumentActivity
{
    [Key]
    public int DocumentActivityId { get; set; }

    public int DocumentId { get; set; }

    [Required]
    [MaxLength(255)]
    public string DocumentTitle { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string FileType { get; set; } = string.Empty;

    [Required]
    public int ActorUserId { get; set; }

    [Required]
    [MaxLength(30)]
    public string Action { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Details { get; set; }

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(ActorUserId))]
    public virtual User ActorUser { get; set; } = null!;
}

public static class DocumentActions
{
    public const string Upload = "Upload";
    public const string Preview = "Preview";
    public const string Download = "Download";
    public const string MetadataEdit = "MetadataEdit";
    public const string Replace = "Replace";
    public const string Delete = "Delete";
    public const string Share = "Share";
    public const string RevokeShare = "RevokeShare";
    public const string AttachToTask = "AttachToTask";
    public const string ScanRejected = "ScanRejected";
}