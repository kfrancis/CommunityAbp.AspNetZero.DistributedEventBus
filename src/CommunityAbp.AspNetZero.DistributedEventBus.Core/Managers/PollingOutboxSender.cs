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

// Refactored: defer resolving the outbox until StartAsync using config + factory.
public class PollingOutboxSender : IOutboxSender, ISingletonDependency
{
    private readonly ILogger<PollingOutboxSender> _logger;
    private readonly IDistributedEventBus _bus;
    private readonly AspNetZeroEventBusBoxesOptions _options;
    private readonly IIocResolver _resolver;
    private CancellationTokenSource? _cts;
    private OutboxConfig? _config;
    private IEventOutbox? _outbox; // resolved lazily per StartAsync

    public PollingOutboxSender(
        ILogger<PollingOutboxSender> logger,
        IDistributedEventBus bus,
        AspNetZeroEventBusBoxesOptions options,
        IIocResolver resolver)
    {
        _logger = logger;
        _bus = bus;
        _options = options;
        _resolver = resolver;
    }

    public Task StartAsync(OutboxConfig outboxConfig, CancellationToken cancellationToken = default)
    {
        _config = outboxConfig;
        _outbox = ResolveOutbox(outboxConfig);
        if (_outbox == null)
        {
            _logger.LogWarning("PollingOutboxSender could not resolve outbox for ImplementationType {Type}. Sender will be idle.", outboxConfig.ImplementationType?.FullName ?? "(null)");
            return Task.CompletedTask;
        }
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = RunAsync(_cts.Token);
        return Task.CompletedTask;
    }

    private IEventOutbox? ResolveOutbox(OutboxConfig cfg)
    {
        // Prefer factory if provided
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

        // If concrete registered resolve directly
        if (_resolver.IsRegistered(impl))
        {
            try { return (IEventOutbox)_resolver.Resolve(impl); } catch (Exception ex) { _logger.LogError(ex, "Failed to resolve concrete outbox {Type}", impl.FullName); }
        }

        // If interface registered and only one handler, resolve interface
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

        // Attempt dynamic registration if ctor dependencies resolvable
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
                continue; // idle until resolved (should not happen unless resolution failed)
            }
            try
            {
                var pending = await _outbox.GetPendingAsync(_options.OutboxBatchSize, ct);
                foreach (var evt in pending)
                {
                    var type = Type.GetType(evt.EventName);
                    if (type == null)
                    {
                        await SafeMarkFailed(evt.Id, "Type not found", ct);
                        continue;
                    }
                    try
                    {
                        var obj = System.Text.Json.JsonSerializer.Deserialize(evt.EventData, type);
                        if (obj != null)
                        {
                            await _bus.PublishAsync(type, obj, onUnitOfWorkComplete: false, useOutbox: false);
                            await SafeMarkSent(evt.Id, ct);
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

            await Task.Delay(_options.OutboxPollingInterval, ct);
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

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _cts?.Cancel();
        return Task.CompletedTask;
    }
}
