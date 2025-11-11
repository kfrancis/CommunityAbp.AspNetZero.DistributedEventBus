using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.EventInboxOutbox;

[Table("InboxMessages")]
[Index(nameof(Status), nameof(ReceivedAt), Name = "IX_InboxMessages_Status_ReceivedAt")]
[Index(nameof(CorrelationId), Name = "IX_InboxMessages_CorrelationId")]
public class InboxMessage
{
 [Key]
 public Guid Id { get; set; }
 [Required]
 public string MessageId { get; set; } = string.Empty;
 [Required, MaxLength(200)]
 public string EventName { get; set; } = string.Empty;
 [Required, MaxLength(200)]
 public string EventType { get; set; } = string.Empty;
 [Required]
 public byte[] EventData { get; set; } = Array.Empty<byte>();
 public DateTime ReceivedAt { get; set; }
 public DateTime? ProcessedAt { get; set; }
 [MaxLength(40)]
 public string Status { get; set; } = "Pending"; // Pending, Processed, Failed
 [MaxLength(100)]
 public string? CorrelationId { get; set; }
 public string? Error { get; set; }
 public int RetryCount { get; set; }
 [Timestamp]
 public byte[]? RowVersion { get; set; }
}
