using Grpc.Net.Client;
using Swarm.Cluster.Services;

namespace Swarm.Node.Services;

public class HeartBeatService(IConfiguration configuration, GrpcChannel grpcChannel, ILogger<HeartBeatService> logger, RegistrationService registrationService)
{
    private readonly string _apiKey = configuration["ApiKey"] ?? throw new InvalidOperationException("ApiKey is not configured");
    private readonly IConfiguration _configuration = configuration;
    private readonly ILogger<HeartBeatService> _logger = logger;
    private readonly GrpcChannel _grpcChannel = grpcChannel;
    private readonly RegistrationService _registrationService = registrationService;

    // P2-1: NodeId is published into IConfiguration by NodeIdentityResolver
    // before any heartbeat fires. Read on demand so this singleton doesn't
    // capture an empty string at construction time.
    private string NodeId => _configuration["NodeId"]
        ?? throw new InvalidOperationException("NodeId is not yet resolved");

    public async Task<bool> SendHeartBeatAsync()
    {
        _logger.LogInformation("Sending heartbeat to cluster for node {NodeId}", NodeId);
        try
        {
            var client = new NodesService.NodesServiceClient(_grpcChannel);
            var response = await client.RecordHeartbeatAsync(new RecordHeartbeatRequest
            {
                NodeId = NodeId,
                ApiKey = _apiKey,
                IsOnline = true,
            });

            if (!response.Success && response.Message == "NodeNotFound")
            {
                _logger.LogWarning("Cluster does not recognise this node, re-registering");
                await _registrationService.ForceRegisterWithClusterAsync();
            }

            return response.Success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending heartbeat to cluster");
            throw;
        }
    }
}
