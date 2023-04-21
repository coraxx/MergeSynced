using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;

namespace MergeSynced.Controls
{
    public class CheckBoxMedia: INotifyPropertyChanged
    {
        private SolidColorBrush _typeBrush = new SolidColorBrush(Colors.Gray);
        private bool _isSelected;
        private string _description = string.Empty;
        private string? _languageId = string.Empty;
        private string? _codecType = string.Empty;
        private int _index = -1;

        public SolidColorBrush TypeBrush
        {
            get => _typeBrush;
            set
            {
                _typeBrush = value;
                OnPropertyChanged();
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public string Description
        {
            get => _description;
            set
            {
                _description = value;
                OnPropertyChanged();
            }
        }

        public string? LanguageId
        {
            get => _languageId;
            set
            {
                _languageId = value;
                OnPropertyChanged();
            }
        }

        public string? CodecType
        {
            get => _codecType;
            set
            {
                _codecType = value;
                OnPropertyChanged();
            }
        }

        public int Index
        {
            get => _index;
            set
            {
                _index = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}