using System.Net;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using AzRadar.Dispatching.Core;
using AzRadar.Dispatching.Core.Configuration;
using AzRadar.Dispatching.Core.Models;
using AzRadar.Shared.Models;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Builder.App.Proactive;
using Microsoft.Agents.Core.Models;
using Microsoft.Agents.Core.Serialization;
using Microsoft.Extensions.Options;

namespace AzRadar.Dispatching.Worker;

public sealed class TeamsDeliveryWorker : IHostedService, IAsyncDisposable
{
    private const string AdaptiveCardContentType = "application/vnd.microsoft.card.adaptive";

    private readonly DispatchingRepository _repository;
    private readonly ServiceHealthAdaptiveCardRenderer _cardRenderer;
    private readonly TeamsDispatchAgent _agent;
    private readonly ServiceBusProcessor _processor;
    private readonly DispatchingServiceBusSettings _settings;
    private readonly ILogger<TeamsDeliveryWorker> _logger;

    public TeamsDeliveryWorker(
        DispatchingRepository repository,
        ServiceHealthAdaptiveCardRenderer cardRenderer,
        IAgent agent,
        ServiceBusClient serviceBusClient,
        IOptions<DispatchingServiceBusSettings> options,
        ILogger<TeamsDeliveryWorker> logger)
    {
        _repository = repository;
        _cardRenderer = cardRenderer;
        _agent = agent as TeamsDispatchAgent
            ?? throw new InvalidOperationException("The registered agent is not a TeamsDispatchAgent.");
        _settings = options.Value;
        _logger = logger;
        _processor = serviceBusClient.CreateProcessor(
            _settings.TopicName,
            _settings.TeamsSubscriptionName,
            new ServiceBusProcessorOptions
            {
                AutoCompleteMessages = false,
                MaxConcurrentCalls = Math.Clamp(_settings.MaxConcurrentCalls, 1, 32),
                MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5),
                ReceiveMode = ServiceBusReceiveMode.PeekLock
            });
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        EnsureConfigured();
        _processor.ProcessMessageAsync += ProcessMessageAsync;
        _processor.ProcessErrorAsync += ProcessErrorAsync;
        await _processor.StartProcessingAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor.IsProcessing)
        {
            await _processor.StopProcessingAsync(cancellationToken);
        }
    }

    public async ValueTask DisposeAsync() => await _processor.DisposeAsync();

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        TeamsDeliveryEnvelope? envelope;
        try
        {
            envelope = args.Message.Body.ToObjectFromJson<TeamsDeliveryEnvelope>();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            await args.DeadLetterMessageAsync(
                args.Message,
                "InvalidEnvelope",
                ex.Message,
                args.CancellationToken);
            return;
        }

        if (envelope == null ||
            string.IsNullOrWhiteSpace(envelope.DeliveryIntentId) ||
            string.IsNullOrWhiteSpace(envelope.EventId) ||
            string.IsNullOrWhiteSpace(envelope.ChannelId))
        {
            await args.DeadLetterMessageAsync(
                args.Message,
                "InvalidEnvelope",
                "The delivery envelope is missing required identifiers.",
                args.CancellationToken);
            return;
        }

        var intent = await _repository.GetIntentAsync(
            envelope.DeliveryIntentId,
            args.CancellationToken);
        if (intent == null)
        {
            await args.DeadLetterMessageAsync(
                args.Message,
                "IntentNotFound",
                $"Delivery intent '{envelope.DeliveryIntentId}' was not found.",
                args.CancellationToken);
            return;
        }

        if (intent.Status == ServiceHealthDeliveryIntentStatuses.Delivered)
        {
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            return;
        }

        if (intent.Status == ServiceHealthDeliveryIntentStatuses.Pending)
        {
            await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
            return;
        }

        if (!await _repository.TryBeginDispatchAsync(intent, args.CancellationToken))
        {
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            return;
        }

        var attempt = new TeamsDeliveryAttempt
        {
            DeliveryIntentId = intent.Id,
            AttemptNumber = intent.AttemptCount + 1,
            StartedAt = DateTimeOffset.UtcNow
        };

        try
        {
            var serviceHealthEvent = await RequireAsync(
                _repository.GetEventAsync(envelope.EventId, args.CancellationToken),
                "EventNotFound",
                $"Service Health event '{envelope.EventId}' was not found.");
            var channel = await RequireAsync(
                _repository.GetChannelAsync(envelope.ChannelId, args.CancellationToken),
                "ChannelNotFound",
                $"Notification channel '{envelope.ChannelId}' was not found.");

            if (channel.Type != ServiceHealthChannelTypes.TeamsBot ||
                channel.RegistrationStatus != ServiceHealthChannelRegistrationStatuses.Registered ||
                channel.SubscribedEventTypes.Count == 0 ||
                string.IsNullOrWhiteSpace(channel.ConversationReferenceId))
            {
                throw new PermanentDispatchException(
                    "ChannelUnavailable",
                    $"Notification channel '{channel.Id}' is not a registered Teams bot destination with event families.");
            }

            var reference = await RequireAsync(
                _repository.GetConversationReferenceAsync(
                    channel.ConversationReferenceId,
                    args.CancellationToken),
                "ConversationNotFound",
                $"Teams conversation '{channel.ConversationReferenceId}' was not found.");
            if (!reference.Active || string.IsNullOrWhiteSpace(reference.ConversationJson))
            {
                throw new PermanentDispatchException(
                    "ConversationInactive",
                    $"Teams conversation '{reference.Id}' is not active.");
            }

            var conversation = RestoreConversation(reference.ConversationJson);
            var cardJson = _cardRenderer.Render(serviceHealthEvent);
            var activity = MessageFactory.Attachment(new Attachment
            {
                ContentType = AdaptiveCardContentType,
                Content = JsonSerializer.Deserialize<JsonElement>(cardJson)
            });
            var createdConversation = await _agent.Proactive.CreateConversationAsync(
                CreateChannelPostOptions(conversation, reference, activity),
                continuationHandler: null!,
                autoSignInHandlers: [],
                continuationActivityFactory: null!,
                cancellationToken: args.CancellationToken);
            var activityId = createdConversation.Reference.ActivityId ?? string.Empty;

            attempt.CompletedAt = DateTimeOffset.UtcNow;
            attempt.Succeeded = true;
            attempt.TeamsActivityId = activityId;
            await _repository.MarkDeliveredAsync(intent.Id, activityId, args.CancellationToken);
            await _repository.StoreAttemptAsync(attempt, args.CancellationToken);
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
        }
        catch (Exception ex)
        {
            var failure = ClassifyFailure(ex);
            var exhausted = attempt.AttemptNumber >= _settings.MaxDeliveryAttempts;
            var deadLetter = failure.Permanent || exhausted;

            attempt.CompletedAt = DateTimeOffset.UtcNow;
            attempt.ErrorCode = exhausted && !failure.Permanent
                ? "RetryLimitExceeded"
                : failure.Code;
            attempt.ErrorMessage = ex.Message;
            await _repository.MarkFailedAsync(
                intent.Id,
                deadLetter,
                attempt.ErrorCode,
                ex.Message,
                args.CancellationToken);
            await _repository.StoreAttemptAsync(attempt, args.CancellationToken);

            if (deadLetter)
            {
                await args.DeadLetterMessageAsync(
                    args.Message,
                    attempt.ErrorCode,
                    ex.Message,
                    args.CancellationToken);
            }
            else
            {
                await args.AbandonMessageAsync(
                    args.Message,
                    cancellationToken: args.CancellationToken);
            }
        }
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(
            args.Exception,
            "Teams delivery processor failure in {ErrorSource} for {EntityPath}",
            args.ErrorSource,
            args.EntityPath);
        return Task.CompletedTask;
    }

    private static async Task<T> RequireAsync<T>(
        Task<T?> task,
        string code,
        string message)
        where T : class =>
        await task ?? throw new PermanentDispatchException(code, message);

    internal static Conversation RestoreConversation(string json)
    {
        // Pair the gateway's Conversation.ToJson() with the SDK's protocol serializer.
        var conversation = ProtocolJsonSerializer.ToObject<Conversation>(json);
        if (conversation?.Reference == null ||
            string.IsNullOrWhiteSpace(conversation.Reference.ChannelId) ||
            string.IsNullOrWhiteSpace(conversation.Reference.Conversation?.Id) ||
            string.IsNullOrWhiteSpace(conversation.Reference.User?.Id) ||
            string.IsNullOrWhiteSpace(conversation.Reference.ServiceUrl) ||
            string.IsNullOrWhiteSpace(conversation.Identity.FindFirst("aud")?.Value))
        {
            throw new JsonException("The stored Teams conversation is missing routing or bot identity data.");
        }

        return conversation;
    }

    internal static CreateConversationOptions CreateChannelPostOptions(
        Conversation conversation,
        TeamsConversationReferenceDocument reference,
        IActivity activity) =>
        CreateConversationOptionsBuilder
            .Create(
                Conversation.ClaimsFromIdentity(conversation.Identity),
                conversation.Reference.ChannelId,
                conversation.Reference.ServiceUrl)
            .WithUser(conversation.Reference.User)
            .WithTenantId(reference.TenantId)
            .WithTeamsChannelId(reference.ChannelId)
            .WithActivity(activity)
            .Build();

    private static DispatchFailure ClassifyFailure(Exception exception)
    {
        if (exception is PermanentDispatchException permanent)
        {
            return new DispatchFailure(permanent.Code, true);
        }

        if (exception is JsonException)
        {
            return new DispatchFailure("ConversationInvalid", true);
        }

        if (exception is HttpRequestException httpException)
        {
            var status = httpException.StatusCode;
            var isTransient = status is HttpStatusCode.RequestTimeout or
                HttpStatusCode.TooManyRequests ||
                status >= HttpStatusCode.InternalServerError;
            return new DispatchFailure(
                status.HasValue ? $"TeamsHttp{(int)status.Value}" : "TeamsHttpFailure",
                !isTransient);
        }

        if (exception is OperationCanceledException)
        {
            return new DispatchFailure("TeamsTimeout", false);
        }

        return new DispatchFailure(exception.GetType().Name, false);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.FullyQualifiedNamespace) ||
            string.IsNullOrWhiteSpace(_settings.TopicName) ||
            string.IsNullOrWhiteSpace(_settings.TeamsSubscriptionName) ||
            string.IsNullOrWhiteSpace(_settings.ManagedIdentityClientId))
        {
            throw new InvalidOperationException("Dispatching Service Bus settings are incomplete.");
        }
    }

    private sealed record DispatchFailure(string Code, bool Permanent);

    private sealed class PermanentDispatchException : Exception
    {
        public PermanentDispatchException(string code, string message)
            : base(message)
        {
            Code = code;
        }

        public string Code { get; }
    }
}
