using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.EventInboxOutbox;

[Table("OutboxMessages")]
[Index(nameof(Status), nameof(CreatedAt), Name = "IX_OutboxMessages_Status_CreatedAt")]
[Index(nameof(CorrelationId), Name = "IX_OutboxMessages_CorrelationId")]
public class OutboxMessage
{
    [Key]
    public Guid Id { get; set; }
    [Required, MaxLength(200)]
    public string EventName { get; set; } = string.Empty;
    [Required, MaxLength(200)]
    public string EventType { get; set; } = string.Empty;
    [Required]
    public byte[] EventData { get; set; } = Array.Empty<byte>();
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    [MaxLength(40)]
    public string Status { get; set; } = "Pending"; // Pending, Sent, Failed
    [MaxLength(100)]
    public string? CorrelationId { get; set; }
    public string? Error { get; set; }
    public int RetryCount { get; set; }
    [Timestamp]
    public byte[]? RowVersion { get; set; }
}
