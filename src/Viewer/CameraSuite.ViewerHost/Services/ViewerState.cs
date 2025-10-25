using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;
using CameraSuite.Shared.Models;

namespace CameraSuite.ViewerHost;

public sealed class ViewerState
{
    private readonly ObservableCollection<StreamViewModel> _streams = new();
    private Dispatcher? _dispatcher;

    public ObservableCollection<StreamViewModel> Streams => _streams;

    public void AttachDispatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public StreamViewModel UpsertStream(StreamAnnouncement announcement, Func<StreamViewModel> factory)
    {
        StreamViewModel? result = null;

        void Apply()
        {
            var existing = _streams.FirstOrDefault(x =>
                string.Equals(x.SourceId, announcement.SourceId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.ChannelName, announcement.ChannelName, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                var viewModel = factory();
                viewModel.UpdateFromAnnouncement(announcement);
                _streams.Add(viewModel);
                result = viewModel;
            }
            else
            {
                existing.UpdateFromAnnouncement(announcement);
                result = existing;
            }
        }

        Dispatch(Apply);
        return result ?? throw new InvalidOperationException("Unable to upsert stream");
    }

    public StreamViewModel? UpdateState(StreamStateUpdate update, Action<StreamViewModel>? mutator = null)
    {
        StreamViewModel? result = null;

        void Apply()
        {
            var existing = _streams.FirstOrDefault(x =>
                string.Equals(x.SourceId, update.SourceId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.ChannelName, update.ChannelName, StringComparison.OrdinalIgnoreCase));

            if (existing is null)
            {
                return;
            }

            existing.Status = update.State.ToString();
            existing.LastError = update.ErrorMessage;
            mutator?.Invoke(existing);
            result = existing;
        }

        Dispatch(Apply);
        return result;
    }

    private void Dispatch(Action action)
    {
        if (_dispatcher is null || _dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _dispatcher.Invoke(action);
        }
    }
}
