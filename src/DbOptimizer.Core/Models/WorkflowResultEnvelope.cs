using System.Text.Json;

namespace DbOptimizer.Core.Models;

public sealed record WorkflowResultEnvelope
{
    public required string ResultType { get; init; }

    public required string DisplayName { get; init; }

    public required string Summary { get; init; }

    public required JsonElement Data { get; init; }

    public required JsonElement Metadata { get; init; }
}
