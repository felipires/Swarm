using Microsoft.Data.Sqlite;
using Swarm.Cluster.Services;
using Swarm.Node.Configuration;
using Swarm.Node.Data;
using Swarm.Node.Extensions;
using Swarm.Sdk.Abstractions;
using Grpc.Net.Client;

namespace Swarm.Node.Services;

/// <summary>
/// Manages initial registration with cluster and periodic heartbeat
/// </summary>
public class RegistrationService(
    ILogger<RegistrationService> logger,
    IConfiguration configuration,
    AppDbConnection dbConnection,
    GrpcChannel grpcChannel,
    NodeTagState tagState,
    IEnumerable<ITaskHandler> handlers)
{
    private readonly string _apiKey = configuration["ApiKey"] ?? throw new InvalidOperationException("ApiKey is not configured");
    private readonly ILogger<RegistrationService> _logger = logger;
    private readonly IConfiguration _configuration = configuration;
    private readonly AppDbConnection _dbConnection = dbConnection;
    private readonly GrpcChannel _grpcChannel = grpcChannel;
    private readonly NodeTagState _tagState = tagState;
    private readonly IReadOnlyList<ITaskHandler> _handlers = handlers.ToList();

    // NodeId is resolved by NodeIdentityResolver in StartupService and
    // written into IConfiguration before any method here runs. Read it
    // on demand so this singleton doesn't capture an empty string at
    // construction time (which happens before StartupService.StartAsync).
    private string NodeId => _configuration["NodeId"]
        ?? throw new InvalidOperationException("NodeId is not yet resolved");

    public async Task<bool> RegisterWithClusterAsync()
    {
        _logger.LogDebug("Registering node with cluster");

        try
        {
            using var dbConnection = new SqliteConnection(_dbConnection.GetConnectionString());

            dbConnection.Open();
            var command = dbConnection.CreateCommand();

            command.CommandText = "SELECT Registered FROM Configuration LIMIT 1";

            var registered = (long?)command.ExecuteScalar() == 1;

            if (registered)
            {
                _logger.LogDebug("Node is already registered, notifying cluster it is back online");

                command.CommandText = "UPDATE Configuration SET Online = 1 WHERE NodeId = (SELECT NodeId FROM Configuration LIMIT 1)";
                command.ExecuteNonQuery();

                // Tell the cluster immediately — otherwise it stays Offline until the next heartbeat timeout check
                var reconnectClient = new NodesService.NodesServiceClient(_grpcChannel);
                await reconnectClient.RecordHeartbeatAsync(new RecordHeartbeatRequest
                {
                    NodeId = NodeId,
                    ApiKey = _apiKey,
                    IsOnline = true
                });

                return true;
            }

            var client = new NodesService.NodesServiceClient(_grpcChannel);

            // P2-5: static_tags is the Node's deploy-time identity, discovered
            // from SWARM_TAG_* env vars and the Swarm:Tags appsettings section.
            // P0-3b: handlers carry the Node's executable capability set — the
            // Cluster uses this for dispatch-time validation (P1-7).
            var capacity = NodeMetricsCollector.ReadCapacity();
            var request = new RegisterNodeRequest
            {
                ApiKey = _apiKey,
                NodeId = NodeId,
                Capacity = new NodeCapacity
                {
                    CpuCores = capacity.CpuCores,
                    TotalMemoryBytes = capacity.TotalMemoryBytes,
                },
            };
            foreach (var (k, v) in _tagState.Static)
                request.StaticTags.Add(k, v);
            foreach (var handler in _handlers)
            {
                var capability = new HandlerCapability
                {
                    TaskType = handler.TaskType,
                    JsonSchema = handler.Schema.JsonSchema,
                };
                foreach (var key in handler.Schema.RequiredEnvKeys)
                    capability.RequiredEnvKeys.Add(key);
                foreach (var key in handler.Schema.RequiredParams)
                    capability.RequiredParams.Add(key);
                request.Handlers.Add(capability);
            }

            _logger.LogDebug("Sending registration request for node: {NodeId}", NodeId);
            
            var response = await client.RegisterNodeAsync(request);

            _logger.LogDebug("Registration response: NodeId={NodeId}, NodeName={NodeName}", 
                response.NodeId, response.NodeName);

            if (response?.NodeId.IsNullOrEmpty() ?? true)
            {
                throw new InvalidOperationException("Node failed while retrieving configuration. NodeId is missing in response.");
            }
            
            command.CommandText = """
                INSERT OR REPLACE INTO Configuration (Registered, Online, NodeId, NodeName) VALUES (1, 1, $1, $2);
                INSERT OR REPLACE INTO RemoteParameters (NodeId, QueueHost, QueuePort, QueueUserName, QueuePassword)
                    VALUES ($1, $3, $4, $5, $6)
                """;

            command.Parameters.Add(new SqliteParameter("$1", response.NodeId));
            command.Parameters.Add(new SqliteParameter("$2", response.NodeName));
            command.Parameters.Add(new SqliteParameter("$3", response.QueueParameters.QueueHost));
            command.Parameters.Add(new SqliteParameter("$4", response.QueueParameters.QueuePort));
            command.Parameters.Add(new SqliteParameter("$5", response.QueueParameters.QueueUserName));
            command.Parameters.Add(new SqliteParameter("$6", response.QueueParameters.QueuePassword));
            command.ExecuteNonQuery();

            _configuration["RabbitMQ:Hostname"] = response.QueueParameters.QueueHost;
            _configuration["RabbitMQ:Port"] = response.QueueParameters.QueuePort.ToString();
            _configuration["RabbitMQ:Username"] = response.QueueParameters.QueueUserName;
            _configuration["RabbitMQ:Password"] = response.QueueParameters.QueuePassword;
            
            return true;
        } 
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering node with cluster");
            return false;
        }
    }

    public async Task ForceRegisterWithClusterAsync()
    {
        _logger.LogInformation("Forcing re-registration with cluster");

        using var dbConnection = new SqliteConnection(_dbConnection.GetConnectionString());
        await dbConnection.OpenAsync();
        using var command = dbConnection.CreateCommand();

        // Reset local registered flag so RegisterWithClusterAsync performs a full registration
        command.CommandText = "UPDATE Configuration SET Registered = 0";
        await command.ExecuteNonQueryAsync();

        await RegisterWithClusterAsync();
    }

    public async Task SetNodeOfflineAsync()
    {
        try
        {
            using var connection = new SqliteConnection(_dbConnection.GetConnectionString());
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Configuration SET Online = 0";            

            var client = new NodesService.NodesServiceClient(_grpcChannel);
            var request = new RecordHeartbeatRequest
            {
                NodeId = NodeId,
                ApiKey = _apiKey,
                IsOnline = false
            };

            await command.ExecuteNonQueryAsync();
            await client.RecordHeartbeatAsync(request);

            _logger.LogDebug("Node marked as offline");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting node offline");
        }
    }
}
