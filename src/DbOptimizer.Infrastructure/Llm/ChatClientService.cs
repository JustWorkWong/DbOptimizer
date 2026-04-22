using System.Text.Json;
using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DbOptimizer.Infrastructure.Llm;

public sealed class LlmProviderOptions
{
    public const string SectionName = "LlmProvider";

    public string Provider { get; set; } = "DashScope";

    public string ApiKey { get; set; } = string.Empty;

    public string? Endpoint { get; set; }

    public string Model { get; set; } = "qwen-max";

    public int MaxOutputTokens { get; set; } = 4096;

    public float Temperature { get; set; } = 0.3f;

    public int MaxRetries { get; set; } = 3;

    public int TimeoutSeconds { get; set; } = 30;

    public bool UseJsonSchemaResponseFormat { get; set; }
}

public sealed class LlmRequestOptions
{
    public string? ModelId { get; set; }

    public int? MaxOutputTokens { get; set; }

    public float? Temperature { get; set; }

    public int? MaxRetries { get; set; }

    public TimeSpan? Timeout { get; set; }

    public bool? UseJsonSchemaResponseFormat { get; set; }

    public string? ConversationId { get; set; }

    public bool? UseStreaming { get; set; }
}

public sealed record LlmTokenUsage(
    int InputTokens,
    int OutputTokens,
    int TotalTokens);

public sealed record LlmTextResponse(
    string Text,
    string ModelId,
    LlmTokenUsage Usage);

public sealed record LlmStructuredResponse<TResponse>(
    TResponse Value,
    string RawText,
    string ModelId,
    LlmTokenUsage Usage)
    where TResponse : class;

public interface IChatClientService
{
    Task<LlmStructuredResponse<TResponse>> GenerateStructuredAsync<TResponse>(
        string systemPrompt,
        string userPrompt,
        LlmRequestOptions? options = null,
        CancellationToken cancellationToken = default)
        where TResponse : class;

    Task<LlmTextResponse> GenerateTextAsync(
        string systemPrompt,
        string userPrompt,
        LlmRequestOptions? options = null,
        CancellationToken cancellationToken = default);
}

public sealed class ChatClientService(
    IChatClient chatClient,
    IOptions<LlmProviderOptions> providerOptions,
    ILogger<ChatClientService> logger) : IChatClientService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IChatClient _chatClient = chatClient;
    private readonly LlmProviderOptions _providerOptions = providerOptions.Value;
    private readonly ILogger<ChatClientService> _logger = logger;

    public async Task<LlmStructuredResponse<TResponse>> GenerateStructuredAsync<TResponse>(
        string systemPrompt,
        string userPrompt,
        LlmRequestOptions? options = null,
        CancellationToken cancellationToken = default)
        where TResponse : class
    {
        var response = await ExecuteWithRetryAsync(
            options,
            static (service, prompts, requestOptions, ct) => service.SendStructuredAsync<TResponse>(
                prompts.SystemPrompt,
                prompts.UserPrompt,
                requestOptions,
                ct),
            new PromptPair(systemPrompt, userPrompt),
            cancellationToken);

        var value = JsonSerializer.Deserialize<TResponse>(response.Text, SerializerOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize LLM response to {typeof(TResponse).Name}.");

        return new LlmStructuredResponse<TResponse>(
            value,
            response.Text,
            ResolveModelId(options),
            ExtractUsage(response));
    }

    public async Task<LlmTextResponse> GenerateTextAsync(
        string systemPrompt,
        string userPrompt,
        LlmRequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await ExecuteWithRetryAsync(
            options,
            static (service, prompts, requestOptions, ct) => service.SendTextAsync(
                prompts.SystemPrompt,
                prompts.UserPrompt,
                requestOptions,
                ct),
            new PromptPair(systemPrompt, userPrompt),
            cancellationToken);

        return new LlmTextResponse(
            response.Text,
            ResolveModelId(options),
            ExtractUsage(response));
    }

    private async Task<ChatResponse> ExecuteWithRetryAsync(
        LlmRequestOptions? options,
        Func<ChatClientService, PromptPair, LlmRequestOptions?, CancellationToken, Task<ChatResponse>> operation,
        PromptPair prompts,
        CancellationToken cancellationToken)
    {
        var maxRetries = Math.Max(1, options?.MaxRetries ?? _providerOptions.MaxRetries);
        var timeout = ResolveTimeout(options);
        var modelId = ResolveModelId(options);
        Exception? lastException = null;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                _logger.LogInformation(
                    "Sending LLM request. Attempt={Attempt}/{MaxRetries}, Provider={Provider}, Model={ModelId}, TimeoutSeconds={TimeoutSeconds}, ConversationId={ConversationId}, SystemPromptLength={SystemPromptLength}, UserPromptLength={UserPromptLength}",
                    attempt,
                    maxRetries,
                    _providerOptions.Provider,
                    modelId,
                    timeout.TotalSeconds,
                    options?.ConversationId ?? string.Empty,
                    prompts.SystemPrompt.Length,
                    prompts.UserPrompt.Length);

                var response = await operation(this, prompts, options, cancellationToken);

                _logger.LogInformation(
                    "LLM request completed. Attempt={Attempt}/{MaxRetries}, Provider={Provider}, Model={ModelId}, DurationMs={DurationMs}, InputTokens={InputTokens}, OutputTokens={OutputTokens}, TotalTokens={TotalTokens}",
                    attempt,
                    maxRetries,
                    _providerOptions.Provider,
                    modelId,
                    stopwatch.ElapsedMilliseconds,
                    response.Usage?.InputTokenCount ?? 0,
                    response.Usage?.OutputTokenCount ?? 0,
                    response.Usage?.TotalTokenCount ?? 0);

                return response;
            }
            catch (Exception ex) when (IsRetriable(ex, cancellationToken) && attempt < maxRetries)
            {
                lastException = ex;
                _logger.LogWarning(
                    ex,
                    "Transient LLM request failure, retrying. Attempt={Attempt}/{MaxRetries}, Provider={Provider}, Model={ModelId}, TimeoutSeconds={TimeoutSeconds}, DurationMs={DurationMs}, ExceptionType={ExceptionType}",
                    attempt,
                    maxRetries,
                    _providerOptions.Provider,
                    modelId,
                    timeout.TotalSeconds,
                    stopwatch.ElapsedMilliseconds,
                    ex.GetType().Name);

                await Task.Delay(GetRetryDelay(attempt), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "LLM request failed. Attempt={Attempt}/{MaxRetries}, Provider={Provider}, Model={ModelId}, TimeoutSeconds={TimeoutSeconds}, DurationMs={DurationMs}, ConversationId={ConversationId}, ExceptionType={ExceptionType}",
                    attempt,
                    maxRetries,
                    _providerOptions.Provider,
                    modelId,
                    timeout.TotalSeconds,
                    stopwatch.ElapsedMilliseconds,
                    options?.ConversationId ?? string.Empty,
                    ex.GetType().Name);
                throw;
            }
        }

        throw lastException ?? new InvalidOperationException("LLM request failed without an exception.");
    }

    private async Task<ChatResponse> SendStructuredAsync<TResponse>(
        string systemPrompt,
        string userPrompt,
        LlmRequestOptions? options,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        var messages = BuildMessages(systemPrompt, userPrompt);
        var chatOptions = CreateChatOptions(options);
        chatOptions.ResponseFormat = ResolveStructuredResponseFormat<TResponse>(options);

        return await SendAsync(messages, chatOptions, options, cancellationToken);
    }

    private async Task<ChatResponse> SendTextAsync(
        string systemPrompt,
        string userPrompt,
        LlmRequestOptions? options,
        CancellationToken cancellationToken)
    {
        var messages = BuildMessages(systemPrompt, userPrompt);
        var chatOptions = CreateChatOptions(options);
        return await SendAsync(messages, chatOptions, options, cancellationToken);
    }

    private async Task<ChatResponse> SendAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions chatOptions,
        LlmRequestOptions? options,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ResolveTimeout(options));

        if (UseStreaming(options))
        {
            return await SendStreamingAsync(messages, chatOptions, options, timeoutCts.Token);
        }

        return await _chatClient.GetResponseAsync(messages, chatOptions, timeoutCts.Token);
    }

    private async Task<ChatResponse> SendStreamingAsync(
        IReadOnlyList<ChatMessage> messages,
        ChatOptions chatOptions,
        LlmRequestOptions? options,
        CancellationToken cancellationToken)
    {
        var updates = new List<ChatResponseUpdate>();
        var chunkCount = 0;
        var firstChunkAtMs = -1L;
        var stopwatch = Stopwatch.StartNew();

        await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, chatOptions, cancellationToken))
        {
            updates.Add(update);
            chunkCount++;

            if (firstChunkAtMs < 0)
            {
                firstChunkAtMs = stopwatch.ElapsedMilliseconds;
                _logger.LogInformation(
                    "Received first LLM streaming chunk. Provider={Provider}, Model={ModelId}, ConversationId={ConversationId}, FirstChunkAtMs={FirstChunkAtMs}",
                    _providerOptions.Provider,
                    ResolveModelId(options),
                    options?.ConversationId ?? string.Empty,
                    firstChunkAtMs);
            }
        }

        _logger.LogInformation(
            "LLM streaming completed. Provider={Provider}, Model={ModelId}, ConversationId={ConversationId}, ChunkCount={ChunkCount}, DurationMs={DurationMs}",
            _providerOptions.Provider,
            ResolveModelId(options),
            options?.ConversationId ?? string.Empty,
            chunkCount,
            stopwatch.ElapsedMilliseconds);

        return await EnumerateUpdatesAsync(updates, cancellationToken).ToChatResponseAsync(cancellationToken);
    }

    private static IReadOnlyList<ChatMessage> BuildMessages(string systemPrompt, string userPrompt)
    {
        return
        [
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, userPrompt)
        ];
    }

    private ChatOptions CreateChatOptions(LlmRequestOptions? options)
    {
        return new ChatOptions
        {
            ModelId = ResolveModelId(options),
            Temperature = options?.Temperature ?? _providerOptions.Temperature,
            MaxOutputTokens = options?.MaxOutputTokens ?? _providerOptions.MaxOutputTokens,
            ConversationId = options?.ConversationId
        };
    }

    private ChatResponseFormat ResolveStructuredResponseFormat<TResponse>(LlmRequestOptions? options)
        where TResponse : class
    {
        var useJsonSchema = options?.UseJsonSchemaResponseFormat ?? _providerOptions.UseJsonSchemaResponseFormat;
        return useJsonSchema
            ? ChatResponseFormat.ForJsonSchema<TResponse>()
            : ChatResponseFormat.Json;
    }

    private TimeSpan ResolveTimeout(LlmRequestOptions? options)
    {
        var timeout = options?.Timeout ?? TimeSpan.FromSeconds(Math.Max(1, _providerOptions.TimeoutSeconds));
        return timeout <= TimeSpan.Zero
            ? TimeSpan.FromSeconds(1)
            : timeout;
    }

    private static TimeSpan GetRetryDelay(int attempt)
    {
        return attempt switch
        {
            <= 1 => TimeSpan.FromSeconds(1),
            2 => TimeSpan.FromSeconds(2),
            _ => TimeSpan.FromSeconds(5)
        };
    }

    private string ResolveModelId(LlmRequestOptions? options)
    {
        return string.IsNullOrWhiteSpace(options?.ModelId) ? _providerOptions.Model : options.ModelId;
    }

    private static bool UseStreaming(LlmRequestOptions? options)
    {
        return options?.UseStreaming ?? false;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> EnumerateUpdatesAsync(
        IReadOnlyList<ChatResponseUpdate> updates,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var update in updates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return update;
            await Task.Yield();
        }
    }

    private static LlmTokenUsage ExtractUsage(ChatResponse response)
    {
        var usage = response.Usage;
        return new LlmTokenUsage(
            (int)Math.Min(int.MaxValue, usage?.InputTokenCount ?? 0),
            (int)Math.Min(int.MaxValue, usage?.OutputTokenCount ?? 0),
            (int)Math.Min(int.MaxValue, usage?.TotalTokenCount ?? 0));
    }

    private static bool IsRetriable(Exception exception, CancellationToken cancellationToken)
    {
        return exception switch
        {
            OperationCanceledException when !cancellationToken.IsCancellationRequested => true,
            TimeoutException => true,
            HttpRequestException => true,
            _ => false
        };
    }

    private sealed record PromptPair(string SystemPrompt, string UserPrompt);
}
