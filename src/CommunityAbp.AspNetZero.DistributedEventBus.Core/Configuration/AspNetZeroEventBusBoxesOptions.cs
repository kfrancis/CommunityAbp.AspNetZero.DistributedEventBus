using System;
using System.Collections.Generic;
using System.Text;
using Abp.Dependency;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration
{
    public class AspNetZeroEventBusBoxesOptions : ISingletonDependency
    {
        public TimeSpan CleanOldEventTimeIntervalSpan { get; set; } = TimeSpan.FromHours(6);
        public int InboxWaitingEventMaxCount { get; set; } = 1000;
    }
}
