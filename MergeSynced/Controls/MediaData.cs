using Avalonia.Controls;
using System;
using System.Collections.ObjectModel;

namespace MergeSynced.Controls
{
    public class MediaData
    {
        public bool IsMainMedia;
        public TimeSpan Duration = TimeSpan.Zero;
        public string Title = string.Empty;

        public ObservableCollection<CheckBoxMedia> ListBoxItems { get; } = new();

        public ObservableCollection<ComboBoxItem> ComboBoxItems { get; } = new();

        public void Clear()
        {
            ListBoxItems.Clear();
            ComboBoxItems.Clear();
            Duration = TimeSpan.Zero;
        }
    }
}