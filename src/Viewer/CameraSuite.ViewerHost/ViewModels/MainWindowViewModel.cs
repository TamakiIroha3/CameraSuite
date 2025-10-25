using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CameraSuite.ViewerHost;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly ViewerState _state;
    private int _gridColumns = 1;

    public MainWindowViewModel(ViewerState state)
    {
        _state = state;
        _state.Streams.CollectionChanged += OnStreamsChanged;
        UpdateGridColumns();
    }

    public ObservableCollection<StreamViewModel> Streams => _state.Streams;

    public int GridColumns
    {
        get => _gridColumns;
        private set => SetField(ref _gridColumns, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnStreamsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => UpdateGridColumns();

    private void UpdateGridColumns()
    {
        var count = Math.Max(Streams.Count, 1);
        var columns = (int)Math.Ceiling(Math.Sqrt(count));
        GridColumns = Math.Max(columns, 1);
    }

    private bool SetField<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
