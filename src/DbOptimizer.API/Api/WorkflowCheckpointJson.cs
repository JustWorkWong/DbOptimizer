using System.Text.Json;
using System.Text.Json.Serialization;
using DbOptimizer.Infrastructure.Checkpointing;

namespace DbOptimizer.API.Api;

internal static class WorkflowCheckpointJson
{
    public static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    public static WorkflowCheckpoint? Deserialize(string json)
    {
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<WorkflowCheckpoint>(json, SerializerOptions);
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
