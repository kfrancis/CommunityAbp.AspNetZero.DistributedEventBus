using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Interfaces;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Models;
using Microsoft.Extensions.Logging;
using Abp.Dependency;
using System.Reflection;
using System.Linq;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Core.Managers;

public class PollingOutboxSender : IOutboxSender, ISingletonDependency
{
    private readonly ILogger<PollingOutboxSender> _logger;
    private readonly IDistributedEventBus _bus;
    private readonly AspNetZeroEventBusBoxesOptions _options;
    private readonly IIocResolver _resolver;
    private readonly IEventSerializer _serializer;
    private CancellationTokenSource? _cts;
    private IEventOutbox? _outbox; // resolved lazily per StartAsync
    private Task? _loop;

    public PollingOutboxSender(
        ILogger<PollingOutboxSender> logger,
        IDistributedEventBus bus,
        AspNetZeroEventBusBoxesOptions options,
        IIocResolver resolver,
        IEventSerializer serializer)
    {
        _logger = logger;
        _bus = bus;
        _options = options;
        _resolver = resolver;
        _serializer = serializer;
    }

    public Task StartAsync(OutboxConfig outboxConfig, CancellationToken cancellationToken = default)
    {
        _outbox = ResolveOutbox(outboxConfig);
        if (_outbox == null)
        {
            _logger.LogWarning("PollingOutboxSender could not resolve outbox for ImplementationType {Type}. Sender will be idle.", outboxConfig.ImplementationType?.FullName ?? "(null)");
            return Task.CompletedTask;
        }
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = RunAsync(_cts.Token);
        return Task.CompletedTask;
    }

    private IEventOutbox? ResolveOutbox(OutboxConfig cfg)
    {
        if (cfg.Factory != null)
        {
            try
            {
                var inst = cfg.Factory(_resolver, cfg);
                if (inst != null) return inst;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox factory failed for {Type}", cfg.ImplementationType?.FullName);
            }
        }

        var impl = cfg.ImplementationType;
        if (impl == null) return TrySingleInterfaceRegistration();
        if (!typeof(IEventOutbox).IsAssignableFrom(impl)) return null;

        if (_resolver.IsRegistered(impl))
        {
            try { return (IEventOutbox)_resolver.Resolve(impl); } catch (Exception ex) { _logger.LogError(ex, "Failed to resolve concrete outbox {Type}", impl.FullName); }
        }

        if (_resolver.IsRegistered<IEventOutbox>())
        {
            try
            {
                var candidate = _resolver.Resolve<IEventOutbox>();
                if (impl.IsInstanceOfType(candidate)) return candidate;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Interface resolution failed for IEventOutbox");
            }
        }

        try
        {
            var ctor = impl.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                            .OrderByDescending(c => c.GetParameters().Length)
                            .FirstOrDefault();
            if (ctor != null)
            {
                var parameters = ctor.GetParameters();
                bool allResolvable = parameters.All(p => _resolver.IsRegistered(p.ParameterType));
                if (allResolvable)
                {
                    if (!_resolver.IsRegistered(impl))
                    {
                        IocManager.Instance.IocContainer.Register(
                            Castle.MicroKernel.Registration.Component
                                .For(typeof(IEventOutbox), impl)
                                .ImplementedBy(impl)
                                .LifestyleSingleton());
                    }
                    return (IEventOutbox)_resolver.Resolve(impl);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Dynamic registration of outbox {Type} failed", impl.FullName);
        }

        return TrySingleInterfaceRegistration();
    }

    private IEventOutbox? TrySingleInterfaceRegistration()
    {
        if (!_resolver.IsRegistered<IEventOutbox>()) return null;
        try
        {
            return _resolver.Resolve<IEventOutbox>();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed fallback interface resolve for IEventOutbox");
            return null;
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_outbox == null)
            {
                await Task.Delay(_options.OutboxPollingInterval, ct);
                continue;
            }
            try
            {
                var pending = await _outbox.GetPendingAsync(_options.OutboxBatchSize, ct);
                foreach (var evt in pending)
                {
                    var type = _serializer.ResolveType(evt.EventName);
                    if (type == null)
                    {
                        await SafeMarkFailed(evt.Id, "Type not found", ct);
                        continue;
                    }
                    try
                    {
                        var obj = _serializer.Deserialize(evt.EventData, type);
                        if (obj != null)
                        {
                            await _bus.PublishAsync(type, obj, onUnitOfWorkComplete: false, useOutbox: false);
                            await SafeMarkSent(evt.Id, ct);
                        }
                        else
                        {
                            await SafeMarkFailed(evt.Id, "Deserialization returned null", ct);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send outbox event {EventId}", evt.Id);
                        await SafeMarkFailed(evt.Id, ex.Message, ct);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox polling failure");
            }

            try
            {
                await Task.Delay(_options.OutboxPollingInterval, ct);
            }
            catch (OperationCanceledException) { }
        }
    }

    private async Task SafeMarkSent(object id, CancellationToken ct)
    {
        try { await _outbox!.MarkSentAsync(id, ct); } catch (Exception ex) { _logger.LogDebug(ex, "MarkSent failed {Id}", id); }
    }
    private async Task SafeMarkFailed(object id, string reason, CancellationToken ct)
    {
        try { await _outbox!.MarkFailedAsync(id, reason, ct); } catch (Exception ex) { _logger.LogDebug(ex, "MarkFailed failed {Id}", id); }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_cts == null) return;
        _cts.Cancel();
        if (_loop != null)
        {
            try { await _loop; } catch (OperationCanceledException) { }
        }
    }
}
