using FourC.Cmms.Core.Common;

namespace FourC.Cmms.Core.Imports;

public enum ImportJobStatus { Queued, Running, Done, Failed }

/// <summary>
/// A check or import running in the background (0.26.0): queued on upload, run by one server's worker, with its progress and
/// result. The uploaded file is kept for 30 days after it finishes (for the problems workbook); the history line stays.
/// </summary>
public class ImportJob : Entity, ITenantOwned
{
    public Guid TenantId { get; set; }
    /// <summary>Who started it (a person or a service account) and the language its result is written in.</summary>
    public Guid? UserId { get; set; }
    public string? UserName { get; set; }
    public string Language { get; set; } = "en";
    public required string FileName { get; set; }
    /// <summary>Where the uploaded file is kept; null once it has been removed.</summary>
    public string? StorageKey { get; set; }
    /// <summary>An import (true) or a check (false, nothing saved).</summary>
    public bool Save { get; set; }
    public ImportJobStatus Status { get; set; }
    /// <summary>The sheet being read now and how far it is.</summary>
    public string? Sheet { get; set; }
    public int RowsDone { get; set; }
    public int RowsTotal { get; set; }
    public string? ResultJson { get; set; }
    public string? Error { get; set; }
    /// <summary>The starter's sites and branches, so the job sees what they see.</summary>
    public string? AccessJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public DateTimeOffset? HeartbeatAt { get; set; }
}
