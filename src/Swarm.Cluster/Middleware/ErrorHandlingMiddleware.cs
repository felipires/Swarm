using Microsoft.AspNetCore.Diagnostics;
using Swarm.Cluster.Models.Dto;
using Swarm.Cluster.Validation;

namespace Swarm.Cluster.Middleware;

/// <summary>
/// Global exception → <see cref="ApiError"/> translator. Wired via
/// <c>app.UseExceptionHandler(...)</c> in <c>Program.cs</c> so every
/// uncaught exception in a controller ends up as a structured response
/// instead of an ASP.NET HTML page or a raw 500.
/// </summary>
public static class ErrorHandlingMiddleware
{
    public static void UseSwarmErrorHandler(this IApplicationBuilder app)
    {
        app.UseExceptionHandler(errApp => errApp.Run(async ctx =>
        {
            var feature = ctx.Features.Get<IExceptionHandlerFeature>();
            var ex = feature?.Error;
            var logger = ctx.RequestServices.GetRequiredService<ILogger<ApiError>>();

            var (status, error) = ex switch
            {
                DispatchValidationException dve =>
                    (StatusCodes.Status400BadRequest, new ApiError(dve.Code, dve.Message, dve.Details)),
                InvalidOperationException ioe =>
                    (StatusCodes.Status400BadRequest, new ApiError("BAD_REQUEST", ioe.Message)),
                ArgumentException ae =>
                    (StatusCodes.Status400BadRequest, new ApiError("BAD_REQUEST", ae.Message)),
                _ => (StatusCodes.Status500InternalServerError,
                      new ApiError("INTERNAL_ERROR", ex?.Message ?? "Internal error")),
            };

            if (status >= 500)
                logger.LogError(ex, "Unhandled exception in {Path}", ctx.Request.Path);
            else
                logger.LogWarning("Handled exception in {Path}: {Code} — {Message}",
                    ctx.Request.Path, error.Code, error.Message);

            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsJsonAsync(error);
        }));
    }
}
