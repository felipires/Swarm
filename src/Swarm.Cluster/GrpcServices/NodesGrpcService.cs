using Grpc.Core;
using Swarm.Cluster.Services;

namespace Swarm.Cluster.GrpcServices;

public class NodesGrpcService : global::Swarm.Cluster.Services.NodesService.NodesServiceBase
{
    private readonly NodeService _nodeService;
    private readonly ILogger<NodesGrpcService> _logger;

    public NodesGrpcService(NodeService nodeService, ILogger<NodesGrpcService> logger)
    {
        _nodeService = nodeService;
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

        var response = await _nodeService.RegisterNodeAsync(request.ApiKey, nodeId, staticTags, capabilities);

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

        var response = new RecordHeartbeatResponse { Success = true, Message = "Heartbeat recorded successfully" };
        foreach (var (k, v) in await _nodeService.GetOverlayTagsAsync(nodeId))
            response.OverlayTags.Add(k, v);
        return response;
    }
}
