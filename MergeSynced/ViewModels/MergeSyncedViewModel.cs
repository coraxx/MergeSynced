using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using MergeSynced.Controls;
using MergeSynced.Utilities;

namespace MergeSynced.ViewModels
{
    public class MergeSyncedViewModel : ViewModelBase
    {
        #region Private fields

        /// <summary>
        /// Memory usage
        /// </summary>
        private readonly DispatcherTimer _pollMemoryInfo = new();   // Get currently used memory by app via _proc
        private readonly Process _proc;
        private GCMemoryInfo _memInfo = GC.GetGCMemoryInfo();
        
        /// <summary>
        /// Helper flag for path evaluation on mac
        /// </summary>
        private bool _initialPathExtensionOnMacDone;

        /// <summary>
        /// Colors for status
        /// </summary>
        private static readonly SolidColorBrush BrushGreen = new(Colors.LimeGreen);
        private static readonly SolidColorBrush BrushYellow = new(Colors.Yellow);
        private static readonly SolidColorBrush BrushOrange = new(Colors.DarkOrange);
        private static readonly SolidColorBrush BrushRed = new(Colors.Red);

        #endregion

        #region Public fields and properties
        
        #region Configuration

        private bool _ffmpegAvailable;
        public bool FfmpegAvailable
        {
            get => _ffmpegAvailable;
            set
            {
                if (value == _ffmpegAvailable) return;
                _ffmpegAvailable = value;
                OnPropertyChanged();
            }
        }

        private bool _ffprobeAvailable;
        public bool FfprobeAvailable
        {
            get => _ffprobeAvailable;
            set
            {
                if (value == _ffprobeAvailable) return;
                _ffprobeAvailable = value;
                OnPropertyChanged();
            }
        }

        private bool _mkvmergeAvailable;
        public bool MkvmergeAvailable
        {
            get => _mkvmergeAvailable;
            set
            {
                if (value == _mkvmergeAvailable) return;
                _mkvmergeAvailable = value;
                OnPropertyChanged();
            }
        }

        private bool _normalizeAudio = true;
        public bool NormalizeAudio
        {
            get => _normalizeAudio;
            set
            {
                if (value == _normalizeAudio) return;
                _normalizeAudio = value;
                OnPropertyChanged();
                SettingsManager.UserSettings.NormalizeAudio = value;
            }
        }

        private bool _useMkvmerge;
        public bool UseMkvmerge
        {
            get => _useMkvmerge;
            set
            {
                if (value == _useMkvmerge) return;
                _useMkvmerge = value;
                OnPropertyChanged();
                OnPropertyChanged("SyncOffsetFormatted");
                SettingsManager.UserSettings.UseMkvmerge = value;
            }
        }

        private bool _showNotifications = true;
        public bool ShowNotifications
        {
            get => _showNotifications;
            set
            {
                if (value == _showNotifications) return;
                _showNotifications = value;
                OnPropertyChanged();
                SettingsManager.UserSettings.ShowNotifications = value;
            }
        }

        private bool _writeLog = true;
        public bool WriteLog
        {
            get => _writeLog;
            set
            {
                if (value == _writeLog) return;
                _writeLog = value;
                OnPropertyChanged();
                SettingsManager.UserSettings.WriteLog = value;
            }
        }

        #endregion

        #region Items

        public MediaData? MediaDataA { get; } = new MediaData { IsMainMedia = true };

        public MediaData? MediaDataB { get; } = new MediaData { IsMainMedia = false };

        public ThemeVariant[] ThemeVariants { get; } = new[]
        {
            ThemeVariant.Default,
            ThemeVariant.Dark,
            ThemeVariant.Light
        };

        #endregion

        #region Delay

        private double _syncDelay;
        public double SyncDelay
        {
            get => _syncDelay;
            set
            {
                _syncDelay = value;

                if (Math.Abs(value) < 0.0001) SyncOffset = "0"; // Ignore values less than ms
                else if (UseMkvmerge) SyncOffset = Convert.ToInt32(Math.Round(-1 * value * 1000)).ToString(); // Delay has to be inverted and in ms for mkvmerge
                else SyncOffset = Convert.ToString(Math.Round(-1 * value, 3), new CultureInfo("en-us"));   // and in seconds with point as decimal for ffmpeg
                DelayIconEqualVisible = Math.Abs(value) < 0.00001;
                DelayIconAbVisible = value < -0.00001;
                DelayIconBaVisible = value > 0.00001;
                OnPropertyChanged();
            }
        }

        private string _syncOffset = "0";
        public string SyncOffset
        {
            get => _syncOffset;
            set
            {
                if (value == _syncOffset) return;
                _syncOffset = value;
                OnPropertyChanged();
                OnPropertyChanged("SyncOffsetFormatted");
            }
        }
        
        public string SyncOffsetFormatted => UseMkvmerge ? $"{_syncOffset} ms" : $"{_syncOffset} s";

        private bool _delayIconEqualVisible = true;
        public bool DelayIconEqualVisible
        {
            get => _delayIconEqualVisible;
            set
            {
                if (value == _delayIconEqualVisible) return;
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
                if (value == _delayIconAbVisible) return;
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
                if (value == _delayIconBaVisible) return;
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
                _progressPercent = value switch
                {
                    > 100 => 100,
                    < 0 => 0,
                    _ => value
                };
                OnPropertyChanged();
            }
        }

        private double _memoryUsedPercent;
        public double MemoryUsedPercent
        {
            get => _memoryUsedPercent;
            set
            {
                _memoryUsedPercent = value switch
                {
                    > 100 => 100,
                    < 0 => 0,
                    _ => value
                };
                OnPropertyChanged();
            }
        }

        private double _memoryUsedPercentTotal;
        public double MemoryUsedPercentTotal
        {
            get => _memoryUsedPercentTotal;
            set
            {
                _memoryUsedPercentTotal = value switch
                {
                    > 100 => 100,
                    < 0 => 0,
                    _ => value
                };
                MemoryUsedTotalColor = _memoryUsedPercent switch
                {
                    > 85 => BrushRed,
                    > 75 => BrushOrange,
                    > 60 => BrushYellow,
                    _ => BrushGreen
                };
                OnPropertyChanged();
            }
        }

        private SolidColorBrush _memoryUsedTotalTotalColor = BrushGreen;
        public SolidColorBrush MemoryUsedTotalColor
        {
            get => _memoryUsedTotalTotalColor;
            set
            {
                if (Equals(value, _memoryUsedTotalTotalColor)) return;
                _memoryUsedTotalTotalColor = value;
                OnPropertyChanged();
            }
        }

        public double MemoryUsedMegaBytes;
        public double TotalMemoryAvailMegaBytes;
        private string _memoryUsedMegaBytesFormattedFormatted = "160 MB";
        public string MemoryUsedMegaBytesFormatted
        {
            get => _memoryUsedMegaBytesFormattedFormatted;
            set
            {
                if (value == _memoryUsedMegaBytesFormattedFormatted) return;
                _memoryUsedMegaBytesFormattedFormatted = value;
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
                CorrPercentColor = value switch
                {
                    > 60.0 => BrushGreen,
                    > 40 => BrushOrange,
                    _ => BrushRed
                };
                OnPropertyChanged();
            }
        }

        private SolidColorBrush _corrPercentColor = new(Colors.Black);

        public SolidColorBrush CorrPercentColor
        {
            get => _corrPercentColor;
            set
            {
                if (Equals(value, _corrPercentColor)) return;
                _corrPercentColor = value;
                OnPropertyChanged();
            }
        }

        #endregion

        #endregion

        public MergeSyncedViewModel()
        {
            // Set up dispatch timer
            _pollMemoryInfo.Interval = TimeSpan.FromSeconds(2);
            _pollMemoryInfo.Tick += PollMemoryInfoOnTick;
            _pollMemoryInfo.IsEnabled = true;

            _proc = Process.GetCurrentProcess();

            // Initialize with current Theme...
            CorrPercentColor = SolidColorBrush.Parse(Application.Current?.ActualThemeVariant.Key.ToString() == "Dark" 
                ? "#FFFFFFFF" 
                : "#FF000000");
            // ...and subscribe to theme change event
            if (Application.Current != null)
                Application.Current.ActualThemeVariantChanged += (sender, args) =>
                {
                    CorrPercentColor =
                        SolidColorBrush.Parse(Application.Current.ActualThemeVariant.Key.ToString() == "Dark"
                            ? "#FFFFFFFF"
                            : "#FF000000");
                };

            // Load settings
            SettingsManager.SettingsLoaded += (sender, args) => {
                NormalizeAudio = SettingsManager.UserSettings.NormalizeAudio;
                UseMkvmerge = SettingsManager.UserSettings.UseMkvmerge;
                ShowNotifications = SettingsManager.UserSettings.ShowNotifications;
                WriteLog = SettingsManager.UserSettings.WriteLog;
            };

            // Check if ffmpeg and mkvmerge is available
            if (OperatingSystem.IsWindows())
            {
                FfmpegAvailable = SearchBinaryInPath("ffmpeg.exe");
                FfprobeAvailable = SearchBinaryInPath("ffprobe.exe");
                MkvmergeAvailable = SearchBinaryInPath("mkvmerge.exe");
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                FfmpegAvailable = SearchBinaryInPath("ffmpeg");
                FfprobeAvailable = SearchBinaryInPath("ffprobe");
                MkvmergeAvailable = SearchBinaryInPath("mkvmerge");
            }
            if (MkvmergeAvailable) UseMkvmerge = !FfmpegAvailable && !FfprobeAvailable;

            if (!Design.IsDesignMode) return;

            // Design time dummy items ////////////////////////////////////////////////////////////
            MemoryUsedPercentTotal = 50;
            MemoryUsedPercent = 25;

            CheckBoxMedia itemA1 = new CheckBoxMedia
            {
                Description = "Item 1",
                Index = 1,
                LanguageId = "EN",
                CodecType = "wav"
            };
            CheckBoxMedia itemA2 = new CheckBoxMedia
            {
                Description = "Item 1",
                Index = 1,
                LanguageId = "EN",
                CodecType = "wav",
                IsSelected = true
            };
            CheckBoxMedia itemB1 = new CheckBoxMedia
            {
                Description = "Item 1",
                Index = 1,
                LanguageId = "EN",
                CodecType = "wav",
                IsSelected = true
            };
            CheckBoxMedia itemB2 = new CheckBoxMedia
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

            ComboBoxItem comboItemA1 = new ComboBoxItem { Content = "1", IsSelected = true };
            ComboBoxItem comboItemA2 = new ComboBoxItem { Content = "2" };
            MediaDataA.ComboBoxItems.Add(comboItemA1);
            MediaDataA.ComboBoxItems.Add(comboItemA2);

            ComboBoxItem comboItemB1 = new ComboBoxItem { Content = "2", IsSelected = true };
            ComboBoxItem comboItemB2 = new ComboBoxItem { Content = "3" };
            ComboBoxItem comboItemB3 = new ComboBoxItem { Content = "4" };
            MediaDataB.ComboBoxItems.Add(comboItemB1);
            MediaDataB.ComboBoxItems.Add(comboItemB2);
            MediaDataB.ComboBoxItems.Add(comboItemB3);
        }
        
        private void PollMemoryInfoOnTick(object? sender, EventArgs e)
        {
            _proc.Refresh();
            _memInfo = GC.GetGCMemoryInfo();
            MemoryUsedMegaBytes = _proc.PrivateMemorySize64 / 1024.0 / 1024.0;
            MemoryUsedMegaBytesFormatted = $"{MemoryUsedMegaBytes:F0} MB";
            TotalMemoryAvailMegaBytes = _memInfo.TotalAvailableMemoryBytes / 1024.0 / 1024.0;
            MemoryUsedPercentTotal = _memInfo.TotalAvailableMemoryBytes > 0 ? _memInfo.MemoryLoadBytes / (double)_memInfo.TotalAvailableMemoryBytes * 100 : 0;
            MemoryUsedPercent = _memInfo.TotalAvailableMemoryBytes > 0 ? _proc.PrivateMemorySize64 / (double)_memInfo.TotalAvailableMemoryBytes * 100 : 0;
        }

        private bool SearchBinaryInPath(string binaryName)
        {
            string? pathData = Environment.GetEnvironmentVariable("PATH");

            if (pathData == null) return false;

            List<string> paths = new List<string>();
            if (OperatingSystem.IsWindows())
            {
                paths = pathData.Split(';').ToList();
            }
            else if (OperatingSystem.IsMacOS())
            {
                paths = pathData.Split(':').ToList();

                // If .app bundle is called, current user PATH is normally not passed in, so try a few common ones
                if (!paths.Any(path => File.Exists(Path.Combine(path, binaryName))) && !_initialPathExtensionOnMacDone)
                {
                    string appBundleMissingPaths = "/opt/homebrew/bin:/opt/homebrew/sbin:/bin:/usr/bin:/usr/sbin:/usr/local/bin";
                    Environment.SetEnvironmentVariable("PATH", $"{appBundleMissingPaths}:{pathData}");
                    _initialPathExtensionOnMacDone = true;
                    Trace.WriteLine($"Could not find {binaryName} in {pathData}, adding {appBundleMissingPaths} to $PATH and trying again");
                    pathData = Environment.GetEnvironmentVariable("PATH");
                    if (pathData == null) return false;
                    paths = pathData.Split(':').ToList();
                }
            }
            else if (OperatingSystem.IsLinux())
            {
                paths = pathData.Split(':').ToList();
            }

            return paths.Any(path => File.Exists(Path.Combine(path, binaryName)));
        }
    }
}
