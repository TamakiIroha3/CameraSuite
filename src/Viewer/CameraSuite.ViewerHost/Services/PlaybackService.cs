using System;
using CameraSuite.Shared.Models;
using LibVLCSharp.Shared;
using Microsoft.Extensions.Logging;

namespace CameraSuite.ViewerHost;

public sealed class PlaybackService : IDisposable
{
    private readonly LibVLC _libVlc;
    private readonly ILogger<PlaybackService> _logger;

    public PlaybackService(ILogger<PlaybackService> logger)
    {
        _logger = logger;
        _libVlc = new LibVLC();
    }

    public StreamViewModel CreateOrUpdate(StreamAnnouncement announcement, StreamViewModel? existing = null)
    {
        var viewModel = existing ?? new StreamViewModel();
        viewModel.UpdateFromAnnouncement(announcement);
        ConfigureMedia(viewModel);
        return viewModel;
    }

    public void ConfigureMedia(StreamViewModel viewModel)
    {
        if (string.IsNullOrWhiteSpace(viewModel.Passphrase))
        {
            return;
        }

        var srtUrl = $"srt://{viewModel.SrtHost}:{viewModel.SrtPort}?mode=caller&passphrase={viewModel.Passphrase}&pbkeylen=32&streamid={viewModel.StreamKey}";

        try
        {
            using var media = new Media(_libVlc, srtUrl, FromType.FromLocation);
            var player = viewModel.MediaPlayer ?? new MediaPlayer(_libVlc);
            if (!player.Play(media))
            {
                _logger.LogWarning("Unable to start playback for channel {Channel}", viewModel.ChannelName);
                viewModel.Status = "PlayError";
            }
            else
            {
                viewModel.Status = "Playing";
                viewModel.LastError = null;
            }

            viewModel.MediaPlayer = player;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Playback error for channel {Channel}", viewModel.ChannelName);
            viewModel.Status = "Error";
            viewModel.LastError = ex.Message;
        }
    }

    public void Dispose()
    {
        _libVlc.Dispose();
    }
}
