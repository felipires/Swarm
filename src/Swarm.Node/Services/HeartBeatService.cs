using System.Text.Json;
using Grpc.Net.Client;
using Swarm.Cluster.Services;
using Swarm.Node.Configuration;
using Swarm.Node.Data;

namespace Swarm.Node.Services;

public class HeartBeatService(
    IConfiguration configuration,
    GrpcChannel grpcChannel,
    ILogger<HeartBeatService> logger,
    RegistrationService registrationService,
    NodeTagState tagState,
    EnvSecretsStore envSecrets,
    PlaintextConfigStore plaintextConfig,
    NodeMetricsCollector metricsCollector,
    IServiceProvider serviceProvider)
{
    private readonly string _apiKey = configuration["ApiKey"] ?? throw new InvalidOperationException("ApiKey is not configured");
    private readonly IConfiguration _configuration = configuration;
    private readonly ILogger<HeartBeatService> _logger = logger;
    private readonly GrpcChannel _grpcChannel = grpcChannel;
    private readonly RegistrationService _registrationService = registrationService;
    private readonly NodeTagState _tagState = tagState;
    private readonly EnvSecretsStore _envSecrets = envSecrets;
    private readonly PlaintextConfigStore _plaintextConfig = plaintextConfig;
    private readonly NodeMetricsCollector _metricsCollector = metricsCollector;
    private readonly IServiceProvider _serviceProvider = serviceProvider;

    private readonly List<string> _pendingAcks = new();

    private string NodeId => _configuration["NodeId"]
        ?? throw new InvalidOperationException("NodeId is not yet resolved");

    public async Task<bool> SendHeartBeatAsync()
    {
        _logger.LogDebug("Sending heartbeat to cluster for node {NodeId}", NodeId);
        try
        {
            var client = new NodesService.NodesServiceClient(_grpcChannel);
            var request = new RecordHeartbeatRequest
            {
                NodeId = NodeId,
                ApiKey = _apiKey,
                IsOnline = true,
            };

            // P1-5a: ack ops applied since the previous heartbeat.
            List<string> acksThisTick;
            lock (_pendingAcks)
            {
                acksThisTick = new List<string>(_pendingAcks);
                _pendingAcks.Clear();
            }
            foreach (var id in acksThisTick)
                request.AckedEnvOpIds.Add(id);

            // P5-1: attach live metrics. Best-effort — a sampling failure never
            // fails the heartbeat; the Cluster simply receives no metrics this tick.
            try
            {
                var executor = _serviceProvider.GetService<TaskExecutorService>();
                var liveMetrics = _metricsCollector.ReadMetrics(executor?.InFlightTasks ?? 0);
                request.Metrics = new NodeMetrics
                {
                    CpuPercent = liveMetrics.CpuPercent,
                    MemoryUsedBytes = liveMetrics.MemoryUsedBytes,
                    MemoryAvailableBytes = liveMetrics.MemoryAvailableBytes,
                    InFlightTasks = liveMetrics.InFlightTasks,
                    UptimeSeconds = liveMetrics.UptimeSeconds,
                    Health = liveMetrics.Health switch
                    {
                        NodeHealthStatus.Degraded => HealthStatus.Degraded,
                        NodeHealthStatus.Unhealthy => HealthStatus.Unhealthy,
                        _ => HealthStatus.Healthy,
                    },
                };
                _logger.LogDebug("Metrics sent to cluster {metrics}", JsonSerializer.Serialize(request.Metrics));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Metrics sampling skipped this heartbeat");
            }

            var response = await client.RecordHeartbeatAsync(request);

            if (!response.Success && response.Message == "NodeNotFound")
            {
                _logger.LogWarning("Cluster does not recognise this node, re-registering");
                await _registrationService.ForceRegisterWithClusterAsync();
            }

            // P2-5: refresh overlay tags from the Cluster on every heartbeat.
            _tagState.SetOverlay(response.OverlayTags);

            // P1-5a: apply env ops the Cluster pushed in this response.
            await ApplyEnvOpsAsync(response.PendingEnvOps);

            // P0-3a: reconcile tagged-queue subscriptions.
            try
            {
                var executor = _serviceProvider.GetRequiredService<TaskExecutorService>();
                await executor.EnsureTaggedSubscriptionsAsync(response.TaggedSubscriptions, default);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to reconcile tagged subscriptions");
            }

            return response.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending heartbeat to cluster");
            throw;
        }
    }

    private async Task ApplyEnvOpsAsync(IReadOnlyList<EnvOp> ops)
    {
        if (ops.Count == 0) return;

        foreach (var op in ops)
        {
            try
            {
                if (op.Kind == EnvOpKind.Set)
                {
                    if (op.IsSecret)
                        await _envSecrets.SetAsync(op.Key, op.Value);
                    else
                        await _plaintextConfig.SetAsync(op.Key, op.Value);
                }
                else
                {
                    await _envSecrets.DeleteAsync(op.Key);
                    await _plaintextConfig.DeleteAsync(op.Key);
                }

                lock (_pendingAcks) _pendingAcks.Add(op.Id);
                _logger.LogInformation(
                    "Applied env op {OpId} {Kind} {Key}", op.Id, op.Kind, op.Key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to apply env op {OpId} {Kind} {Key}; will retry on next heartbeat",
                    op.Id, op.Kind, op.Key);
            }
        }
    }
}
