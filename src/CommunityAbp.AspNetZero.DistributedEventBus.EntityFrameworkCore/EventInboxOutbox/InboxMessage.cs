using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.EventInboxOutbox;

[Table("InboxMessages")]
public class InboxMessage
{
    [Key]
    public Guid Id { get; set; }
    [Required]
    public string MessageId { get; set; } = string.Empty;
    [Required]
    public string EventName { get; set; } = string.Empty;
    [Required]
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
}
