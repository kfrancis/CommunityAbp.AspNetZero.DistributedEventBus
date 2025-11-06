using System;
using Abp.Dependency;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration
{
    public class AspNetZeroEventBusBoxesOptions : ISingletonDependency
    {
        public TimeSpan CleanOldEventTimeIntervalSpan { get; set; } = TimeSpan.FromHours(6);
        public int InboxWaitingEventMaxCount { get; set; } = 1000;

        // New: polling intervals and batch sizes
        public TimeSpan OutboxPollingInterval { get; set; } = TimeSpan.FromSeconds(2);
        public TimeSpan InboxPollingInterval { get; set; } = TimeSpan.FromSeconds(2);
        public int OutboxBatchSize { get; set; } = 50;
        public int InboxBatchSize { get; set; } = 50;

        public int MaxRetryCount { get; set; } = 5;
        public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(7);
    }
}
