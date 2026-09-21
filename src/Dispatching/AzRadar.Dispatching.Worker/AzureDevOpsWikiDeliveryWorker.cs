using System.Net;
using Azure.Messaging.ServiceBus;
using AzRadar.Dispatching.Core;
using AzRadar.Dispatching.Core.Configuration;
using AzRadar.Dispatching.Core.Models;
using AzRadar.Shared.Interfaces;
using AzRadar.Shared.Models;
using AzRadar.Shared.Services;
using Microsoft.Extensions.Options;

namespace AzRadar.Dispatching.Worker;

public sealed class AzureDevOpsWikiDeliveryWorker : IHostedService, IAsyncDisposable
{
    private readonly DispatchingRepository _repository;
    private readonly ServiceHealthWikiRenderer _renderer;
    private readonly IAzureDevOpsWikiService _wikiService;
    private readonly ServiceBusProcessor _processor;
    private readonly DispatchingServiceBusSettings _settings;
    private readonly ILogger<AzureDevOpsWikiDeliveryWorker> _logger;

    public AzureDevOpsWikiDeliveryWorker(
        DispatchingRepository repository,
        ServiceHealthWikiRenderer renderer,
        IAzureDevOpsWikiService wikiService,
        ServiceBusClient serviceBusClient,
        IOptions<DispatchingServiceBusSettings> options,
        ILogger<AzureDevOpsWikiDeliveryWorker> logger)
    {
        _repository = repository;
        _renderer = renderer;
        _wikiService = wikiService;
        _settings = options.Value;
        _logger = logger;
        _processor = serviceBusClient.CreateProcessor(
            _settings.TopicName,
            _settings.WikiSubscriptionName,
            new ServiceBusProcessorOptions
            {
                AutoCompleteMessages = false,
                MaxConcurrentCalls = 1,
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
            await _processor.StopProcessingAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync() => await _processor.DisposeAsync();

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        ServiceHealthDeliveryEnvelope? envelope;
        try
        {
            envelope = args.Message.Body.ToObjectFromJson<ServiceHealthDeliveryEnvelope>();
        }
        catch (Exception ex)
        {
            await args.DeadLetterMessageAsync(args.Message, "InvalidEnvelope", ex.Message, args.CancellationToken);
            return;
        }

        if (envelope == null ||
            string.IsNullOrWhiteSpace(envelope.DeliveryIntentId) ||
            string.IsNullOrWhiteSpace(envelope.ChannelId) ||
            envelope.TargetType != ServiceHealthChannelTypes.AzureDevOpsWiki)
        {
            await args.DeadLetterMessageAsync(
                args.Message,
                "InvalidEnvelope",
                "The wiki delivery envelope is missing required target identifiers.",
                args.CancellationToken);
            return;
        }

        var intent = await _repository.GetIntentAsync(envelope.DeliveryIntentId, args.CancellationToken);
        if (intent == null)
        {
            await args.DeadLetterMessageAsync(args.Message, "IntentNotFound", "Delivery intent was not found.", args.CancellationToken);
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

        var attempt = new WikiDeliveryAttempt
        {
            DeliveryIntentId = intent.Id,
            TargetId = envelope.ChannelId,
            AttemptNumber = intent.AttemptCount + 1
        };

        try
        {
            var target = await _repository.GetChannelAsync(envelope.ChannelId, args.CancellationToken)
                ?? throw new WikiDispatchException("TargetNotFound", "The Azure DevOps Wiki target was not found.", true);
            if (target.Type != ServiceHealthChannelTypes.AzureDevOpsWiki ||
                target.RegistrationStatus == ServiceHealthChannelRegistrationStatuses.Disabled)
            {
                throw new WikiDispatchException(
                    "TargetUnavailable",
                    "The Azure DevOps Wiki target is disabled or has an invalid type.",
                    true);
            }

            var events = await _repository.GetEventsAsync(1000, args.CancellationToken);
            var content = _renderer.Render(events, DateTimeOffset.UtcNow);
            var contentHash = ServiceHealthEventNormalizer.ComputeHash(content);

            if (string.Equals(
                    target.LastRenderedContentHash,
                    contentHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                await _repository.MarkWikiDeliveredAsync(
                    intent.Id,
                    target.LastExternalVersion,
                    contentHash,
                    args.CancellationToken);
                await args.CompleteMessageAsync(args.Message, args.CancellationToken);
                return;
            }

            var snapshot = await _wikiService.GetPageAsync(target, args.CancellationToken);
            target.AzureDevOpsPagePath = snapshot.PagePath;
            var result = await _wikiService.UpdatePageAsync(
                target,
                content,
                snapshot.ETag,
                args.CancellationToken);

            attempt.CompletedAt = DateTimeOffset.UtcNow;
            attempt.Succeeded = true;
            attempt.ExternalVersion = result.ETag;
            attempt.RenderedContentHash = contentHash;
            await _repository.MarkWikiDeliveredAsync(
                intent.Id,
                result.ETag,
                contentHash,
                args.CancellationToken);
            await _repository.UpdateWikiTargetSuccessAsync(
                target.Id,
                target.AzureDevOpsPagePath,
                result.ETag,
                contentHash,
                args.CancellationToken);
            await _repository.StoreAttemptAsync(attempt, args.CancellationToken);
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
        }
        catch (Exception ex)
        {
            var failure = ClassifyFailure(ex);
            var exhausted = attempt.AttemptNumber >= _settings.MaxDeliveryAttempts;
            var deadLetter = failure.Permanent || exhausted;
            var status = failure.Code is "authentication-failed" or "credential-missing" or
                "identity-authentication-failed" or "permission-required"
                ? ServiceHealthChannelRegistrationStatuses.PermissionRequired
                : ServiceHealthChannelRegistrationStatuses.Degraded;

            attempt.CompletedAt = DateTimeOffset.UtcNow;
            attempt.ErrorCode = exhausted && !failure.Permanent ? "RetryLimitExceeded" : failure.Code;
            attempt.ErrorMessage = ex.Message;
            await _repository.UpdateWikiTargetFailureAsync(
                envelope.ChannelId,
                status,
                attempt.ErrorCode,
                ex.Message,
                args.CancellationToken);
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
                await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
            }
        }
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(
            args.Exception,
            "Azure DevOps Wiki delivery processor failure in {ErrorSource} for {EntityPath}",
            args.ErrorSource,
            args.EntityPath);
        return Task.CompletedTask;
    }

    private static DispatchFailure ClassifyFailure(Exception exception)
    {
        if (exception is WikiDispatchException dispatch)
            return new DispatchFailure(dispatch.Code, dispatch.Permanent);
        if (exception is AzureDevOpsWikiException wiki)
        {
            var transient = wiki.StatusCode is HttpStatusCode.RequestTimeout or
                HttpStatusCode.TooManyRequests ||
                wiki.StatusCode >= HttpStatusCode.InternalServerError;
            return new DispatchFailure(wiki.Code, !transient);
        }
        if (exception is OperationCanceledException)
            return new DispatchFailure("azure-devops-timeout", false);
        if (exception is HttpRequestException http)
        {
            var transient = http.StatusCode is null or HttpStatusCode.RequestTimeout or
                HttpStatusCode.TooManyRequests ||
                http.StatusCode >= HttpStatusCode.InternalServerError;
            return new DispatchFailure("azure-devops-http-failure", !transient);
        }

        return new DispatchFailure(exception.GetType().Name, false);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.FullyQualifiedNamespace) ||
            string.IsNullOrWhiteSpace(_settings.TopicName) ||
            string.IsNullOrWhiteSpace(_settings.WikiSubscriptionName) ||
            string.IsNullOrWhiteSpace(_settings.ManagedIdentityClientId))
        {
            throw new InvalidOperationException("Azure DevOps Wiki dispatch settings are incomplete.");
        }
    }

    private sealed record DispatchFailure(string Code, bool Permanent);

    private sealed class WikiDispatchException : Exception
    {
        public WikiDispatchException(string code, string message, bool permanent)
            : base(message)
        {
            Code = code;
            Permanent = permanent;
        }

        public string Code { get; }
        public bool Permanent { get; }
    }
}
