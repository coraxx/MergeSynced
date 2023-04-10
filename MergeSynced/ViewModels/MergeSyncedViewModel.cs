using Avalonia.Media;
using MergeSynced.Controls;
using System;
using System.Dynamic;
using Avalonia.Controls;

namespace MergeSynced.ViewModels
{
    public class MergeSyncedViewModel : ViewModelBase
    {
        #region Fields and properties

        #region Items

        public MediaData MediaDataA { get; } = new MediaData { IsMainMedia = true };

        public MediaData MediaDataB { get; } = new MediaData { IsMainMedia = false };

        #endregion

        #region Delay

        private double _syncDelay;
        public double SyncDelay
        {
            get => _syncDelay;
            set
            {
                _syncDelay = value;
                DelayIconEqualVisible = Math.Abs(value) < 0.00001;
                DelayIconAbVisible = value < -0.00001;
                DelayIconBaVisible = value > 0.00001;
                OnPropertyChanged();
            }
        }

        private bool _delayIconEqualVisible = true;
        public bool DelayIconEqualVisible
        {
            get => _delayIconEqualVisible;
            set
            {
                _delayIconEqualVisible = value;
                OnPropertyChanged();
            }
        }

        private bool _delayIconAbVisible;
        public bool DelayIconAbVisible
        {
            get => _delayIconAbVisible;
            set
            {
                _delayIconAbVisible = value;
                OnPropertyChanged();
            }
        }

        private bool _delayIconBaVisible;
        public bool DelayIconBaVisible
        {
            get => _delayIconBaVisible;
            set
            {
                _delayIconBaVisible = value;
                OnPropertyChanged();
            }
        }

        #endregion

        #region Statistics

        private double _progressPercent;
        public double ProgressPercent
        {
            get => _progressPercent;
            set
            {
                if (value > 100) _progressPercent = 100;
                else if (value < 0) _progressPercent = 0;
                else _progressPercent = value;
                OnPropertyChanged();
            }
        }

        private double _statsMaxAa;
        public double StatsMaxAa
        {
            get => _statsMaxAa;
            set
            {
                _statsMaxAa = value;
                OnPropertyChanged();
            }
        }

        private double _statsMaxAb;
        public double StatsMaxAb
        {
            get => _statsMaxAb;
            set
            {
                _statsMaxAb = value;
                OnPropertyChanged();
            }
        }

        private double _corrPercent;
        public double CorrPercent
        {
            get => _corrPercent;
            set
            {
                _corrPercent = value;
                if (value > 60.0)
                {
                    CorrPercentColor = new SolidColorBrush(Colors.LimeGreen);
                }
                else if (value > 40)
                {
                    CorrPercentColor = new SolidColorBrush(Colors.DarkOrange);
                }
                else
                {
                    CorrPercentColor = new SolidColorBrush(Colors.Red);
                }
                OnPropertyChanged();
            }
        }

        private SolidColorBrush _corrPercentColor = new SolidColorBrush(Colors.Black);

        public SolidColorBrush CorrPercentColor
        {
            get => _corrPercentColor;
            set
            {
                _corrPercentColor = value;
                OnPropertyChanged();
            }
        }

        #endregion

        #endregion

        public MergeSyncedViewModel()
        {
            if (!Avalonia.Controls.Design.IsDesignMode) return;

            CheckBoxMedia itemA1 = new CheckBoxMedia()
            {
                Description = "Item 1",
                Index = 1,
                LanguageId = "EN",
                CodecType = "wav"
            };
            CheckBoxMedia itemA2 = new CheckBoxMedia()
            {
                Description = "Item 1",
                Index = 1,
                LanguageId = "EN",
                CodecType = "wav",
                IsSelected = true
            };
            CheckBoxMedia itemB1 = new CheckBoxMedia()
            {
                Description = "Item 1",
                Index = 1,
                LanguageId = "EN",
                CodecType = "wav",
                IsSelected = true
            };
            CheckBoxMedia itemB2 = new CheckBoxMedia()
            {
                Description = "Item 2",
                Index = 2,
                LanguageId = "EN",
                CodecType = "wav"
            };
            MediaDataA.ListBoxItems.Add(itemA1);
            MediaDataA.ListBoxItems.Add(itemA2);
            MediaDataB.ListBoxItems.Add(itemB1);
            MediaDataB.ListBoxItems.Add(itemB2);

            ComboBoxItem comboItemA1 = new ComboBoxItem() { Content = "1", IsSelected = true };
            ComboBoxItem comboItemA2 = new ComboBoxItem() { Content = "2" };
            MediaDataA.ComboBoxItems.Add(comboItemA1);
            MediaDataA.ComboBoxItems.Add(comboItemA2);

            ComboBoxItem comboItemB1 = new ComboBoxItem() { Content = "2", IsSelected = true };
            ComboBoxItem comboItemB2 = new ComboBoxItem() { Content = "3" };
            ComboBoxItem comboItemB3 = new ComboBoxItem() { Content = "4" };
            MediaDataB.ComboBoxItems.Add(comboItemB1);
            MediaDataB.ComboBoxItems.Add(comboItemB2);
            MediaDataB.ComboBoxItems.Add(comboItemB3);
        }
    }
}
