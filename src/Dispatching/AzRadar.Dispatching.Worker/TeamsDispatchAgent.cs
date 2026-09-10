using Microsoft.Agents.Builder.App;

namespace AzRadar.Dispatching.Worker;

public sealed class TeamsDispatchAgent : AgentApplication
{
    public TeamsDispatchAgent(AgentApplicationOptions options)
        : base(options)
    {
    }
}
