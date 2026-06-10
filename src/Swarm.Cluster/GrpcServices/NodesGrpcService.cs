using System.Text.Json;
using Grpc.Core;
using Swarm.Cluster.Services;
using Swarm.Cluster.Services.Metrics;

namespace Swarm.Cluster.GrpcServices;

public class NodesGrpcService : global::Swarm.Cluster.Services.NodesService.NodesServiceBase
{
    private readonly NodeService _nodeService;
    private readonly NodeMetricsStore _metricsStore;
    private readonly ILogger<NodesGrpcService> _logger;

    public NodesGrpcService(NodeService nodeService, NodeMetricsStore metricsStore, ILogger<NodesGrpcService> logger)
    {
        _nodeService = nodeService;
        _metricsStore = metricsStore;
        _logger = logger;
    }

    public override async Task<RegisterNodeResponse> RegisterNode(RegisterNodeRequest request, ServerCallContext context)
    {
        _logger.LogInformation("gRPC RegisterNode request from: {NodeId}", request.NodeId);

        if (!Guid.TryParse(request.NodeId, out var nodeId))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid node ID format"));
        }

        var staticTags = request.StaticTags?.Count > 0
            ? request.StaticTags.ToDictionary(x => x.Key, x => x.Value)
            : null;

        var capabilities = request.Handlers?
            .Select(h => new Swarm.Cluster.Models.NodeCapability
            {
                TaskType = h.TaskType,
                JsonSchema = string.IsNullOrEmpty(h.JsonSchema) ? "{}" : h.JsonSchema,
                RequiredEnvKeysJson = System.Text.Json.JsonSerializer.Serialize(h.RequiredEnvKeys),
                RequiredParamsJson = System.Text.Json.JsonSerializer.Serialize(h.RequiredParams),
            })
            .ToList();

        int? cpuCores = request.Capacity?.CpuCores > 0 ? request.Capacity.CpuCores : null;
        long? totalMemory = request.Capacity?.TotalMemoryBytes > 0 ? request.Capacity.TotalMemoryBytes : null;

        var response = await _nodeService.RegisterNodeAsync(request.ApiKey, nodeId, staticTags, capabilities, cpuCores, totalMemory);

        return new RegisterNodeResponse
        {
            NodeId = response.NodeId,
            NodeName = response.NodeName,
            QueueParameters = new RemoteParameters
            {
                QueueHost = response.QueueParameters.QueueHost,
                QueuePort = response.QueueParameters.QueuePort,
                QueueUserName = response.QueueParameters.QueueUserName,
                QueuePassword = response.QueueParameters.QueuePassword
            }
        };
    }

    public override async Task<RecordHeartbeatResponse> RecordHeartbeat(RecordHeartbeatRequest request, ServerCallContext context)
    {
        _logger.LogDebug("gRPC RecordHeartbeat from: {NodeId}", request.NodeId);

        if (!Guid.TryParse(request.NodeId, out var nodeId))
        {
            return new RecordHeartbeatResponse
            {
                Success = false,
                Message = "Invalid node ID format"
            };
        }

        var known = await _nodeService.UpdateHeartbeatAsync(nodeId, request.IsOnline);

        if (!known)
            return new RecordHeartbeatResponse { Success = false, Message = "NodeNotFound" };

        // P1-5a: ack ops the Node applied since the last heartbeat, then
        // drain a batch of pending ops to deliver in this response.
        if (request.AckedEnvOpIds is { Count: > 0 })
        {
            var ackedIds = request.AckedEnvOpIds
                .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty)
                .Where(g => g != Guid.Empty)
                .ToList();
            await _nodeService.AckEnvOpsAsync(nodeId, ackedIds);
        }

        var response = new RecordHeartbeatResponse { Success = true, Message = "Heartbeat recorded successfully" };
        foreach (var (k, v) in await _nodeService.GetOverlayTagsAsync(nodeId))
            response.OverlayTags.Add(k, v);

        foreach (var op in await _nodeService.DrainEnvOpsForHeartbeatAsync(nodeId))
        {
            response.PendingEnvOps.Add(new EnvOp
            {
                Id = op.Id.ToString(),
                Kind = op.Op == Models.NodeEnvOp.EnvOpKind.Set ? EnvOpKind.Set : EnvOpKind.DeleteKey,
                Key = op.Key,
                Value = op.Value ?? string.Empty,
                IsSecret = op.IsSecret,
            });
        }

        foreach (var queue in await _nodeService.GetTaggedSubscriptionsAsync(nodeId))
            response.TaggedSubscriptions.Add(queue);

        _logger.LogInformation("{metrics}", JsonSerializer.Serialize(request.Metrics));
        // P5-1: store live metrics best-effort — never fail a heartbeat for this.
        if (request.Metrics is not null)
        {
            var snapshot = new NodeMetricsSnapshot
            {
                RecordedAt = DateTime.UtcNow,
                CpuPercent = request.Metrics.CpuPercent,
                MemoryUsedBytes = request.Metrics.MemoryUsedBytes,
                MemoryAvailableBytes = request.Metrics.MemoryAvailableBytes,
                InFlightTasks = request.Metrics.InFlightTasks,
                UptimeSeconds = request.Metrics.UptimeSeconds,
                Health = request.Metrics.Health.ToString(),
            };
            await _metricsStore.StoreAsync(nodeId, snapshot);
        }

        return response;
    }
}
