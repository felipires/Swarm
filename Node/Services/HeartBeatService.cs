using Grpc.Net.Client;
using Swarm.Cluster.Services;

namespace Swarm.Node.Services;

public class HeartBeatService(IConfiguration configuration, GrpcChannel grpcChannel, ILogger<HeartBeatService> logger)
{
    private readonly string _nodeId = configuration["NodeId"] ?? throw new InvalidOperationException("NodeId is not configured");
    private readonly string _apiKey = configuration["ApiKey"] ?? throw new InvalidOperationException("ApiKey is not configured");
    private readonly ILogger<HeartBeatService> _logger = logger;
    private readonly GrpcChannel _grpcChannel = grpcChannel;

    public async Task<bool> SendHeartBeatAsync()
    {
        _logger.LogInformation("Sending heartbeat to cluster for node {NodeId}", _nodeId);
        try
        {
            var client = new NodesService.NodesServiceClient(_grpcChannel);
            var request = new RecordHeartbeatRequest
            {
                NodeId = _nodeId,
                ApiKey = _apiKey,
                IsOnline = true
            };

            var response = await client.RecordHeartbeatAsync(request);
            
            _logger.LogInformation("Heartbeat response: Success={Success}, Message={Message}", 
                response.Success, response.Message);
            
            return response.Success;
        } 
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending heartbeat to cluster");
            throw;
        }
    }
}
