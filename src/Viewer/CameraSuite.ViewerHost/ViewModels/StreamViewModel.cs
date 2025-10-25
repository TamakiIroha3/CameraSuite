using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CameraSuite.Shared.Models;
using CameraSuite.Shared.Security;
using LibVLCSharp.Shared;

namespace CameraSuite.ViewerHost;

public sealed class StreamViewModel : INotifyPropertyChanged, IDisposable
{
    private string _status = "Pending";
    private string? _lastError;
    private MediaPlayer? _mediaPlayer;
    private string? _passphrase;

    public string SourceId { get; private set; } = string.Empty;

    public string ChannelName { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string SrtHost { get; private set; } = string.Empty;

    public int SrtPort { get; private set; }

    public string StreamKey { get; private set; } = string.Empty;

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public string? LastError
    {
        get => _lastError;
        set => SetField(ref _lastError, value);
    }

    public string? Passphrase
    {
        get => _passphrase;
        private set => SetField(ref _passphrase, value);
    }

    public MediaPlayer? MediaPlayer
    {
        get => _mediaPlayer;
        set => SetField(ref _mediaPlayer, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void UpdateFromAnnouncement(StreamAnnouncement announcement)
    {
        SourceId = announcement.SourceId;
        ChannelName = announcement.ChannelName;
        DisplayName = announcement.DisplayName;
        SrtHost = announcement.SrtHost;
        SrtPort = announcement.SrtPort;
        StreamKey = announcement.StreamKey;

        Passphrase = announcement.AesKey is { Length: > 0 }
            ? AesKeyMaterial.DerivePassphrase(announcement.AesKey)
            : Passphrase;

        OnPropertyChanged(nameof(SourceId));
        OnPropertyChanged(nameof(ChannelName));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(SrtHost));
        OnPropertyChanged(nameof(SrtPort));
        OnPropertyChanged(nameof(StreamKey));
    }

    private bool SetField<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        MediaPlayer?.Dispose();
    }
}
