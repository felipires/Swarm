using System;
using Swarm.Node.Data;
using Swarm.Node.Services;
using Swarm.Node.Logging;

namespace Swarm.Node.BackgroundServices;

public class StartupService(
    BackgroundMaestro gate,
    RegistrationService registrationService,
    AppDbConnection dbConnection,
    IConfiguration configuration,
    ILogger<StartupService> logger,
    IHostApplicationLifetime appLifetime
    ) : IHostedService
{
    private readonly BackgroundMaestro _gate = gate;
    private readonly ILogger<StartupService> _logger = logger;
    private readonly AppDbConnection _dbConnection = dbConnection;
    private readonly IConfiguration _configuration = configuration;
    private readonly IHostApplicationLifetime _appLifetime = appLifetime;
    private readonly RegistrationService _registrationService = registrationService;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Startup service initializing node. Locking background services until initialization is complete.");
            await Task.WhenAll(InitializeNodeAsync(), SetupShutdownHandlerAsync());
            _gate.Release();
        } catch (Exception ex)
        {
            _logger.LogCritical(ex, "Startup service failed to initialize node. Shutting down application.");
            _appLifetime.StopApplication();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task InitializeNodeAsync()
    {
        _logger.LogDebug("Initializing node");

        try
        {
            await _dbConnection.SetupDatabaseAsync();

            var registered = await _registrationService.RegisterWithClusterAsync();

            if (!registered)
            {
                _logger.LogError("Failed to register with cluster");
                throw new InvalidOperationException("Node registration failed");
            }

            _logger.LogInformation("Node registered successfully. Configuring RabbitMQ logging sink.");
            SerilogConfiguration.AddRabbitMQSink(_configuration);
            _logger.LogInformation("RabbitMQ logging sink configured and active");

            _logger.LogDebug("Node initialization completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while initializing node");
            throw;
        }
    }

    private async Task SetupShutdownHandlerAsync()
    {
        await Task.Run(() =>
        {
            _appLifetime.ApplicationStopping.Register(async () =>
            {
                try
                {
                    _logger.LogDebug("Running shutdown operations...");
                    await _registrationService.SetNodeOfflineAsync();
                    _logger.LogDebug("Resources cleaned up successfully. Application is shutting down.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during shutdown operations");
                }
            });
        });
    }
}