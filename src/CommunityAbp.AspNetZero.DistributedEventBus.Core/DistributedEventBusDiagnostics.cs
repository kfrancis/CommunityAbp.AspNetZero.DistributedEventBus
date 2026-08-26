using System.Diagnostics;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core;

public static class DistributedEventBusDiagnostics
{
    public const string ActivitySourceName = "CommunityAbp.AspNetZero.DistributedEventBus";
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
