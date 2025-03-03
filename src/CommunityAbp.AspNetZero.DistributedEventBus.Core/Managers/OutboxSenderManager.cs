using System;
using System.Threading.Tasks;
using System.Threading;
using Abp.Threading.BackgroundWorkers;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Abp.Threading;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core.Managers;

public interface IOutboxSender
{
    Task StartAsync(OutboxConfig outboxConfig, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}

public interface IOutboxSenderManager : IBackgroundWorker
{
    void Start();
    void Stop();
    void WaitToStop();
}

public class OutboxSenderManager : IOutboxSenderManager
{
    public OutboxSenderManager(IOptions<DistributedEventBusOptions> options, IServiceProvider serviceProvider)
    {
        ServiceProvider = serviceProvider;
        Options = options.Value;
        Senders = new List<IOutboxSender>();
    }

    protected IServiceProvider ServiceProvider { get; }

    protected DistributedEventBusOptions Options { get; }
    protected List<IOutboxSender> Senders { get; }

    public void Start()
    {
        foreach (var outboxConfig in Options.Outboxes.Values)
        {
            if (!outboxConfig.IsSendingEnabled)
            {
                continue;
            }

            var sender = ServiceProvider.GetRequiredService<IOutboxSender>();
            AsyncHelper.RunSync(() => sender.StartAsync(outboxConfig));
            Senders.Add(sender);
        }
    }

    public void Stop()
    {
        foreach (var sender in Senders)
        {
            AsyncHelper.RunSync(() => sender.StopAsync());
        }
    }

    public void WaitToStop()
    {
        throw new NotImplementedException();
    }
}
