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

        private ObservableCollection<CheckBoxMedia>? _listBoxItems;
        public ObservableCollection<CheckBoxMedia> ListBoxItems => _listBoxItems ??= new ObservableCollection<CheckBoxMedia>();

        private ObservableCollection<ComboBoxItem>? _comboBoxItems;
        public ObservableCollection<ComboBoxItem> ComboBoxItems => _comboBoxItems ??= new ObservableCollection<ComboBoxItem>();

        public void Clear()
        {
            ListBoxItems.Clear();
            ComboBoxItems.Clear();
            Duration = TimeSpan.Zero;
        }
    }
}