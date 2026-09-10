using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Sportarr.Api.Data;
using Sportarr.Api.Services;
using Sportarr.Api.Validators;

namespace Sportarr.Api.Endpoints;

public static class DownloadCompletionEndpoints
{
    public static IEndpointRouteBuilder MapDownloadCompletionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/download/completed", async (
            DownloadCompletionRequest request,
            SportarrDbContext db,
            DownloadMonitorWakeSignal signal,
            CancellationToken cancellationToken) =>
        {
            var normalizedId = request.DownloadId!.Trim().ToLowerInvariant();
            var matches = db.DownloadQueue.AsNoTracking()
                .Where(item => item.DownloadId.ToLower() == normalizedId);
            if (request.DownloadClientId.HasValue)
                matches = matches.Where(item => item.DownloadClientId == request.DownloadClientId.Value);

            var matched = await matches.AnyAsync(cancellationToken);
            var queued = matched && await matches.Where(DownloadMonitorEligibility.ActiveDownloads)
                .AnyAsync(cancellationToken);
            if (queued)
                signal.RequestCheck();

            return Results.Ok(new { matched, queued });
        }).WithRequestValidation<DownloadCompletionRequest>();

        return app;
    }
}

public sealed class DownloadCompletionRequest
{
    public string? DownloadId { get; set; }
    public int? DownloadClientId { get; set; }
}

public sealed class DownloadCompletionRequestValidator : AbstractValidator<DownloadCompletionRequest>
{
    public DownloadCompletionRequestValidator()
    {
        RuleFor(request => request.DownloadId).NotEmpty().MaximumLength(512);
        RuleFor(request => request.DownloadClientId).GreaterThan(0)
            .When(request => request.DownloadClientId.HasValue);
    }
}
