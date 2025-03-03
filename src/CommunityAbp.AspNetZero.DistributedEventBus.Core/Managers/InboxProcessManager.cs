using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Abp.Threading;
using Abp.Threading.BackgroundWorkers;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core.Managers;

public interface IInboxProcessor
{
    Task StartAsync(InboxConfig inboxConfig, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}

public class InboxProcessManager : IBackgroundWorker
{
    public InboxProcessManager(IOptions<DistributedEventBusOptions> options, IServiceProvider serviceProvider)
    {
        ServiceProvider = serviceProvider;
        Options = options.Value;
        Processors = new List<IInboxProcessor>();
    }

    protected IServiceProvider ServiceProvider { get; }

    protected DistributedEventBusOptions Options { get; }
    protected List<IInboxProcessor> Processors { get; }

    public void Start()
    {
        foreach (var inboxConfig in Options.Inboxes.Values)
        {
            if (!inboxConfig.IsProcessingEnabled)
            {
                continue;
            }

            var processor = ServiceProvider.GetRequiredService<IInboxProcessor>();
            AsyncHelper.RunSync(() => processor.StartAsync(inboxConfig));
            Processors.Add(processor);
        }
    }

    public void Stop()
    {
        foreach (var processor in Processors)
        {
            AsyncHelper.RunSync(() => processor.StopAsync());
        }
    }

    public void WaitToStop()
    {
        throw new NotImplementedException();
    }
}
