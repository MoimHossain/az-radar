using AzRadar.Shared.Models;

namespace AzRadar.Shared.Interfaces;

public interface IJobHandler
{
    string JobType { get; }
    Task HandleAsync(CrawlJob job, CancellationToken cancellationToken = default);
}
