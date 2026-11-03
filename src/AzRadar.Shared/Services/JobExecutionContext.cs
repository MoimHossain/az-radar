using AzRadar.Shared.Models;

namespace AzRadar.Shared.Services;

public sealed class JobExecutionContext : IDisposable
{
    private static readonly AsyncLocal<JobExecutionContext?> Ambient = new();
    private readonly JobExecutionContext? _previous = Ambient.Value;
    public static bool IsScheduled => Ambient.Value?.Scheduled == true;
    public static AiProviderProfile? Profile => Ambient.Value?._profile;
    private readonly AiProviderProfile? _profile;
    public bool Scheduled { get; }
    public List<JobReadBarrier> Barriers { get; } = [];

    public JobExecutionContext(bool scheduled, AiProviderProfile? profile = null)
    {
        Scheduled = scheduled;
        _profile = profile;
        Ambient.Value = this;
    }

    public static void Record(string container, string id, string? sessionToken)
    {
        if (Ambient.Value is not { Scheduled: true } context) return;
        if (string.IsNullOrEmpty(sessionToken))
            throw new InvalidOperationException("A scheduled data write returned no Cosmos session token.");
        context.Barriers.Add(new(container, id, sessionToken));
        if (context.Barriers.Count > 20_000)
            throw new InvalidOperationException("Scheduled job exceeded the supported completion-barrier size.");
    }

    public void Dispose() => Ambient.Value = _previous;
}

public record JobReadBarrier(string Container, string ItemId, string SessionToken);

public class JobBarrierPage
{
    public string Id { get; set; } = "";
    public string ScheduleId { get; set; } = "";
    public string Kind { get; set; } = "barrier";
    public List<JobReadBarrier> Items { get; set; } = [];
}
