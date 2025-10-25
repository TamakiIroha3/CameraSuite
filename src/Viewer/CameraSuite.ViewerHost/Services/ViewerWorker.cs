using System.Threading;
using System.Threading.Tasks;
using CameraSuite.Shared.Messaging;
using CameraSuite.Shared.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CameraSuite.ViewerHost;

public sealed class ViewerWorker : BackgroundService
{
    private readonly INatsJsonMessenger _messenger;
    private readonly ViewerState _state;
    private readonly PlaybackService _playback;
    private readonly MediamtxManager _mediamtx;
    private readonly ILogger<ViewerWorker> _logger;

    public ViewerWorker(
        INatsJsonMessenger messenger,
        ViewerState state,
        PlaybackService playback,
        MediamtxManager mediamtx,
        ILogger<ViewerWorker> logger)
    {
        _messenger = messenger;
        _state = state;
        _playback = playback;
        _mediamtx = mediamtx;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var announceTask = HandleAnnouncementsAsync(stoppingToken);
        var stateTask = HandleStateUpdatesAsync(stoppingToken);
        await Task.WhenAll(announceTask, stateTask).ConfigureAwait(false);
    }

    private async Task HandleAnnouncementsAsync(CancellationToken cancellationToken)
    {
        await foreach (var message in _messenger.SubscribeAsync<StreamAnnouncement>(NatsSubjects.StreamsAnnounce, cancellationToken))
        {
            if (message.Data is not StreamAnnouncement announcement)
            {
                continue;
            }

            _logger.LogInformation("Announcement received for {Channel} ({Source})", announcement.ChannelName, announcement.SourceId);

            var viewModel = _state.UpsertStream(announcement, () => new StreamViewModel());
            _playback.CreateOrUpdate(announcement, viewModel);
            await _mediamtx.RegisterStreamAsync(viewModel, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleStateUpdatesAsync(CancellationToken cancellationToken)
    {
        await foreach (var message in _messenger.SubscribeAsync<StreamStateUpdate>(NatsSubjects.StreamsState, cancellationToken))
        {
            if (message.Data is not StreamStateUpdate update)
            {
                continue;
            }

            _logger.LogDebug("State update {Channel}: {State}", update.ChannelName, update.State);
            _state.UpdateState(update);
        }
    }
}
