using Microsoft.AspNetCore.Mvc;
using Swarm.Cluster.Models.Dto;
using Swarm.Cluster.Services;

namespace Swarm.Cluster.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CapabilitiesController : ControllerBase
{
    private readonly NodeService _nodeService;

    public CapabilitiesController(NodeService nodeService)
    {
        _nodeService = nodeService;
    }

    /// <summary>
    /// The cluster-wide catalog of distinct TaskType@version handlers any Node
    /// advertises, each with its schema and resolution requirements. Drives the
    /// task-authoring UI (task-type selection + config validation).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<CapabilityCatalogEntry>>> Get()
    {
        var catalog = await _nodeService.GetCapabilityCatalogAsync();
        return Ok(catalog);
    }
}
