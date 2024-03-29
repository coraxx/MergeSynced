using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using MergeSynced.Audio;
using MergeSynced.Controls;
using MergeSynced.Utilities;
using MergeSynced.ViewModels;
using ScottPlot.Control;
using ScottPlot.Plottables;

namespace MergeSynced.Views
{
    public partial class MainWindow : Window
    {
        #region Fields
        
        public MergeSyncedViewModel MainViewModel = new();
        
        private WindowNotificationManager? _notificationManager;
        
        private readonly ExternalProcesses _ep = new();

        // Parse time from external tools' output
        private readonly Regex _reTime = new(@"time=\s*([0-9\.:]*)", RegexOptions.IgnoreCase);
        private Match _timeMatchFfmpegProg = null!;
        private TimeSpan _currentTimeFfmpegProg;

        // Directories for application data
        private readonly string _settingsDir;
        private readonly string _workingDir;

        // Debugging
        private readonly StringTraceListener _trace;
        private Stopwatch _sw = new();

        #endregion

        #region CTOR and window events

        public MainWindow()
        {
            InitializeComponent();
            DataContext = MainViewModel;

            Title = $"{Assembly.GetExecutingAssembly().GetName().Name} - v{Assembly.GetExecutingAssembly().GetName().Version?.ToString().TrimEnd('0').TrimEnd('.')}";

            // Get settings folder
            _settingsDir = CreateSettingsDir();

            // Create temporary working directory
            _workingDir = CreateTempDir();

            // Add event handler in order to expose logs
            _trace = new StringTraceListener();
            _trace.PropertyChanged += TraceOnPropertyChanged;
            Trace.Listeners.Add(_trace);
            Trace.AutoFlush = true;

            // Only allow input of numbers
            SampleStart.AddHandler(TextInputEvent, Uint32_OnPreviewTextInput!, RoutingStrategies.Tunnel);
            SampleDuration.AddHandler(TextInputEvent, Uint32_OnPreviewTextInput!, RoutingStrategies.Tunnel);

            FilePathA.AddHandler(DragDrop.DropEvent, File_Drop!, RoutingStrategies.Bubble);
            FilePathB.AddHandler(DragDrop.DropEvent, File_Drop!, RoutingStrategies.Bubble);
            FilePathOut.AddHandler(DragDrop.DropEvent, File_Drop!, RoutingStrategies.Bubble);

            // Initial plot setup...
            SetWpfPlotStatic(WpfPlotAudioWaves);
            SetWpfPlotStatic(WpfPlotCrossCorrelation);

            // ...and subscribe to theme change event
            if (Application.Current != null)
                Application.Current.ActualThemeVariantChanged += (sender, args) =>
                {
                    SetWpfPlotStatic(WpfPlotAudioWaves);
                    SetWpfPlotStatic(WpfPlotCrossCorrelation);
                    DelayIcon.InvalidateVisual();
                    DelayIconAb.InvalidateVisual();
                    DelayIconBa.InvalidateVisual();
                };

            if (MainViewModel.MkvmergeAvailable) ProbeButton.ClearValue(BackgroundProperty);

            if (MainViewModel is { FfmpegAvailable: false, FfprobeAvailable: false, MkvmergeAvailable: false }) SwitchButtonState(ProbeButton, false, "No ffmpeg in PATH", true);
        }

        private void Window_OnLoaded(object? sender, RoutedEventArgs e)
        {
            // Load settings when window is loaded so all OnPropertyChanged events are evaluated properly
            SettingsManager.FilePath = Path.Combine(_settingsDir, "MergeSynced_Settings.json");
            SettingsManager.Load();

            // Add event handler in order to expose logs
            if (SettingsManager.UserSettings.WriteLog)
                _trace.GenerateLogfile(Path.Combine(_workingDir, "log.txt"));

            // Bindings for settings from CodeBehind to avoid trigger on open/close of fly out
            ThemeSelect.Bind(ItemsControl.ItemsSourceProperty, new Binding
            {
                Source = MainViewModel,
                Path = nameof(MainViewModel.ThemeVariants)
            });
            ThemeSelect.SelectedItem = SettingsManager.UserSettings.SelectedTheme;  // -> property changed triggers SetSelectedTheme()

            SettingWriteLog.Bind(ToggleButton.IsCheckedProperty, new Binding
            {
                Source = MainViewModel,
                Path = nameof(MainViewModel.WriteLog)
            });

            SettingShowNotification.Bind(ToggleButton.IsCheckedProperty, new Binding
            {
                Source = MainViewModel,
                Path = nameof(MainViewModel.ShowNotifications)
            });

            UseMkvMergeCheckBox.GetObservable(ToggleButton.IsCheckedProperty).Subscribe(_ =>
            {
                CheckModeAndFiles();
            });

            if (SettingsManager.UserSettings.ShowNotifications)
            {
                _notificationManager = new WindowNotificationManager(this)
                {
                    Position = NotificationPosition.TopRight,
                    MaxItems = 5
                };
            }
        }

        private void Window_OnClosing(object? sender, WindowClosingEventArgs e)
        {
            // Save settings
            SettingsManager.Save();
            // Close log file
            Trace.Listeners.Remove(_trace);
            _trace.Logfile?.Close();
            // Clean up temp dir
            if (!string.IsNullOrEmpty(_workingDir) && Path.Exists(_workingDir)) Directory.Delete(_workingDir, true);
        }

        #endregion

        #region Setup helper methods

        private static void SetWpfPlotStatic(IPlotControl wpfPlot)
        {
            wpfPlot.Plot.XAxes.ForEach(x => x.IsVisible = false);
            wpfPlot.Plot.YAxes.ForEach(x => x.IsVisible = false);
            wpfPlot.Plot.Grids.Clear();

            // Initialize with current Theme...
            if (Application.Current?.ActualThemeVariant == ThemeVariant.Dark) wpfPlot.Plot.Style.Background(ScottPlot.Color.FromHex("#232323"), ScottPlot.Color.FromHex("#232323"));
            else wpfPlot.Plot.Style.Background(ScottPlot.Color.FromHex("#FFFFFFFF"), ScottPlot.Color.FromHex("#FFFFFFFF"));

            // Currently not available in v5 beta
            //wpfPlot.Configuration.LeftClickDragPan = false;
            //wpfPlot.Configuration.RightClickDragZoom = false;
            //wpfPlot.Configuration.DoubleClickBenchmark = false;
            //wpfPlot.Configuration.MiddleClickAutoAxis = false;
            //wpfPlot.Configuration.MiddleClickDragZoom = false;
            //wpfPlot.Configuration.ScrollWheelZoom = false;
            //wpfPlot.Configuration.LockHorizontalAxis = true;
            //wpfPlot.Configuration.LockVerticalAxis = true;
            wpfPlot.Refresh();
        }

        public string CreateSettingsDir()
        {
            string settingsDirPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Assembly.GetExecutingAssembly().GetName().Name ?? "MergeSynced");
            Directory.CreateDirectory(settingsDirPath);
            return settingsDirPath;
        }

        public string CreateTempDir()
        {
            string tempDirPath = Path.Combine(Path.GetTempPath(), $"{Assembly.GetExecutingAssembly().GetName().Name}_{Path.GetRandomFileName()}");
            Directory.CreateDirectory(tempDirPath);
            return tempDirPath;
        }

        #endregion

        #region Controls logic

        private async void Probe_OnClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(FilePathA.Text) || string.IsNullOrEmpty(FilePathB.Text))
            {
                SwitchButtonState(ProbeButton, false, "Please select two media files", true);
                return;
            }

            if (!MainViewModel.FfprobeAvailable && !MainViewModel.UseMkvmerge)
            {
                SwitchButtonState(ProbeButton, false, "No ffprobe in PATH", true);
                return;
            }

            if (!MainViewModel.MkvmergeAvailable && MainViewModel.UseMkvmerge)
            {
                SwitchButtonState(ProbeButton, false, "No mkvmerge in PATH", true);
                return;
            }

            AnalyzeButton.IsEnabled = false;
            MergeButton.IsEnabled = false;

            SwitchButtonState(ProbeButton, true, "Probing...");

            // Clear data
            WpfPlotAudioWaves.Plot.Clear();
            WpfPlotAudioWaves.Refresh();
            WpfPlotCrossCorrelation.Plot.Clear();
            WpfPlotCrossCorrelation.Refresh();

            ExternalProcessOutputTextBox.Clear();

            ClearProbeData();

            // Using mkvmerge /////////////////////////////////////////////////////////////////////////////////////////
            bool result;
            if (MainViewModel.UseMkvmerge)
            {
                // A
                _ep.CallMkvmerge($"--identification-format json --identify \"{FilePathA.Text}\"",
                    _ep.ProbeOutputHandler, _workingDir);
                if (_ep.MkvmergeProcess == null!)
                {
                    SwitchButtonState(ProbeButton, false, "Error starting mkvmerge", true);
                    return;
                }
                await _ep.MkvmergeProcess.WaitForExitAsync();

                MainViewModel.ProgressPercent = 25;
                result = _ep.ParseMkvmergeJson(MainViewModel.MediaDataA);
                SelectTrackA.SelectedIndex = 0;

                if (_ep.MkvmergeProcess.ExitCode > 0 || !result)
                {
                    MainViewModel.ProgressPercent = 0;
                    SwitchButtonState(ProbeButton, false, "Error probing input A", true);
                    return;
                }

                MainViewModel.ProgressPercent = 50;
                SelectTrackA.IsEnabled = true;

                // B
                _ep.CallMkvmerge($"--identification-format json --identify \"{FilePathB.Text}\"",
                    _ep.ProbeOutputHandler, _workingDir);
                await _ep.MkvmergeProcess.WaitForExitAsync();

                MainViewModel.ProgressPercent = 75;
                result = _ep.ParseMkvmergeJson(MainViewModel.MediaDataB);
                SelectTrackB.SelectedIndex = 0;

                if (_ep.MkvmergeProcess.ExitCode > 0 || !result)
                {
                    MainViewModel.ProgressPercent = 0;
                    SwitchButtonState(ProbeButton, false, "Error probing input B", true);
                    return;
                }

                MainViewModel.ProgressPercent = 100;
                SelectTrackB.IsEnabled = true;
            }
            // Using ffmpeg   /////////////////////////////////////////////////////////////////////////////////////////
            else
            {
                _ep.CallFfprobe(FilePathA.Text, _workingDir);
                if (_ep.FfprobeProcess == null!)
                {
                    SwitchButtonState(ProbeButton, false, "Error starting ffprobe", true);
                    return;
                }
                await _ep.FfprobeProcess!.WaitForExitAsync();

                MainViewModel.ProgressPercent = 25;
                result = _ep.ParseFfprobeJson(MainViewModel.MediaDataA);
                SelectTrackA.SelectedIndex = 0;

                if (_ep.FfprobeProcess.ExitCode > 0 || !result)
                {
                    MainViewModel.ProgressPercent = 0;
                    SwitchButtonState(ProbeButton, false, "Error probing input A", true);
                    return;
                }
                MainViewModel.ProgressPercent = 50;
                SelectTrackA.IsEnabled = true;

                _ep.CallFfprobe(FilePathB.Text, _workingDir);
                if (_ep.FfprobeProcess == null!)
                {
                    SwitchButtonState(ProbeButton, false, "Error starting ffprobe", true);
                    return;
                }
                await _ep.FfprobeProcess.WaitForExitAsync();

                MainViewModel.ProgressPercent = 75;
                result = _ep.ParseFfprobeJson(MainViewModel.MediaDataB);
                SelectTrackB.SelectedIndex = 0;

                if (_ep.FfprobeProcess.ExitCode > 0 || !result)
                {
                    MainViewModel.ProgressPercent = 0;
                    SwitchButtonState(ProbeButton, false, "Error probing input B", true);
                    return;
                }
                MainViewModel.ProgressPercent = 100;
                SelectTrackB.IsEnabled = true;
            }

            AnalyzeButton.IsEnabled = true;
            ProbeButton.ClearValue(BackgroundProperty);
            ProbeButton.ClearValue(ForegroundProperty);
            SwitchButtonState(ProbeButton, false, "Probing done");
        }

        private async void Analyze_OnClick(object sender, RoutedEventArgs e)
        {
            SwitchButtonState(AnalyzeButton, true, "Analyzing...");

            if (string.IsNullOrEmpty(FilePathA.Text) || string.IsNullOrEmpty(FilePathB.Text))
            {
                SwitchButtonState(AnalyzeButton, false, "Please select two media files", true);
                return;
            }

            if (!MainViewModel.FfmpegAvailable)
            {
                SwitchButtonState(AnalyzeButton, false, "No ffmpeg in PATH", true);
                return;
            }

            ProbeButton.IsEnabled = false;
            MergeButton.IsEnabled = false;

            // Clear Data - Currently not available in v5 beta
            //Annotation graphLabel = WpfPlotAudioWaves.Plot.Add.Annotation("Updating", WpfPlotAudioWaves.Bounds.Width * 0.5 - 40, WpfPlotAudioWaves.Bounds.Height * 0.5 - 10);
            //graphLabel.Font.Size = 24;
            //graphLabel.Font.Name = "Impact";
            //graphLabel.Font.Color = System.Drawing.Color.Red;
            //graphLabel.Shadow = false;
            //graphLabel.BackgroundColor = System.Drawing.Color.FromArgb(100, System.Drawing.Color.DimGray);
            //graphLabel.BorderWidth = 2;
            //graphLabel.BorderColor = System.Drawing.Color.Red;
            //WpfPlotAudioWaves.Refresh();

            //graphLabel = WpfPlotCrossCorrelation.Plot.AddAnnotation("Updating", WpfPlotCrossCorrelation.Bounds.Width * 0.5 - 40, WpfPlotCrossCorrelation.Bounds.Height * 0.5 - 10);
            //graphLabel.Font.Size = 24;
            //graphLabel.Font.Name = "Impact";
            //graphLabel.Font.Color = System.Drawing.Color.Red;
            //graphLabel.Shadow = false;
            //graphLabel.BackgroundColor = System.Drawing.Color.FromArgb(100, System.Drawing.Color.DimGray);
            //graphLabel.BorderWidth = 2;
            //graphLabel.BorderColor = System.Drawing.Color.Red;
            //WpfPlotCrossCorrelation.Refresh();

            ExternalProcessOutputTextBox.Clear();

            if (MainViewModel.MediaDataA!.Duration.TotalSeconds == 0)
            {
                ProbeButton.IsEnabled = true;
                SwitchButtonState(AnalyzeButton, false, "Total seconds is 0 for input A", true);
                MainViewModel.ProgressPercent = 0;
                return;
            }

            if (MainViewModel.MediaDataB!.Duration.TotalSeconds == 0)
            {
                ProbeButton.IsEnabled = true;
                SwitchButtonState(AnalyzeButton, false, "Total seconds is 0 for input B", true);
                MainViewModel.ProgressPercent = 0;
                return;
            }

            bool durationResult = int.TryParse(SampleDuration.Text, out int sampleDurationSeconds);
            if (!durationResult) sampleDurationSeconds = 20;
            int.TryParse(SampleStart.Text, out int startTime);

            _sw.Restart();
            // Down mix to mono and shorten files
            string args;

            // A
            ComboBoxItem? co = SelectTrackA.SelectedItem as ComboBoxItem;
            int selectedTrack = int.Parse((co != null && co.Content != null ? co.Content.ToString() : "0")!);
            string inputA = Path.Combine(_workingDir, "inputA.wav");

            if (MainViewModel.MediaDataA.Duration.TotalSeconds > sampleDurationSeconds)
            {
                if (startTime + sampleDurationSeconds > MainViewModel.MediaDataA.Duration.TotalSeconds) startTime = (int)MainViewModel.MediaDataA.Duration.TotalSeconds - sampleDurationSeconds;
                args =
                    $"-y -ss {startTime} -i \"{FilePathA.Text}\" -t {sampleDurationSeconds} -map 0:{(selectedTrack > 0 ? selectedTrack.ToString() : "a:0")} -c:a pcm_s16le -ac 1 \"{inputA}\"";
                Debug.Print(args);
                _ep.CallFfmpeg(args, FfmpegOutputHandler, _workingDir);
            }
            else
            {
                args = $"-y -i \"{FilePathA.Text}\" -map 0:{(selectedTrack > 0 ? selectedTrack.ToString() : "a:0")} -c:a pcm_s16le -ac 1 \"{inputA}\"";
                Debug.Print(args);
                _ep.CallFfmpeg(args, FfmpegOutputHandler, _workingDir);
            }
            if (_ep.FfmpegProcess == null!)
            {
                ProbeButton.IsEnabled = true;
                SwitchButtonState(AnalyzeButton, false, "Error starting ffmpeg", true);
                return;
            }
            await _ep.FfmpegProcess!.WaitForExitAsync();

            if (_ep.FfmpegProcess.ExitCode > 0)
            {
                MainViewModel.ProgressPercent = 0;
                ProbeButton.IsEnabled = true;
                SwitchButtonState(AnalyzeButton, false, "Error converting audio input A", true);
                return;
            }
            MainViewModel.ProgressPercent = 45;

            DebugTimeSpan("extracting audio A");

            // B
            co = SelectTrackB.SelectedItem as ComboBoxItem;
            selectedTrack = int.Parse((co != null && co.Content != null ? co.Content.ToString() : "0")!);
            string inputB = Path.Combine(_workingDir, "inputB.wav");

            if (MainViewModel.MediaDataB.Duration.TotalSeconds > sampleDurationSeconds)
            {
                if (startTime + sampleDurationSeconds > MainViewModel.MediaDataB.Duration.TotalSeconds) startTime = (int)MainViewModel.MediaDataB.Duration.TotalSeconds - sampleDurationSeconds;
                args = $"-y -ss {startTime} -i \"{FilePathB.Text}\" -t {sampleDurationSeconds} -map 0:{(selectedTrack > 0 ? selectedTrack.ToString() : "a:0")} -c:a pcm_s16le -ac 1 \"{inputB}\"";
                Debug.Print(args);
                _ep.CallFfmpeg(args, FfmpegOutputHandler, _workingDir);
            }
            else
            {
                args = $"-y -i \"{FilePathB.Text}\" -map 0:{(selectedTrack > 0 ? selectedTrack.ToString() : "a:0")} -c:a pcm_s16le -ac 1 \"{inputB}\"";
                Debug.Print(args);
                _ep.CallFfmpeg(args, FfmpegOutputHandler, _workingDir);
            }
            await _ep.FfmpegProcess.WaitForExitAsync();

            if (_ep.FfmpegProcess.ExitCode > 0)
            {
                MainViewModel.ProgressPercent = 0;
                ProbeButton.IsEnabled = true;
                SwitchButtonState(AnalyzeButton, false, "Error converting audio input B", true);
                return;
            }
            MainViewModel.ProgressPercent = 60;

            DebugTimeSpan("extracting audio B");

            // Load generated audio files
            WavHeader? headerA = WavTools.ReadWav(inputA, out float[]? l1);
            MainViewModel.ProgressPercent = 65;
            WavHeader? headerB = WavTools.ReadWav(inputB, out float[]? l2);
            MainViewModel.ProgressPercent = 70;

            DebugTimeSpan("reading wav files");

            if (l1 == null || l2 == null || headerA == null || headerB == null)
            {
                ProbeButton.IsEnabled = true;
                SwitchButtonState(AnalyzeButton, false, "Error converting audio", true);
                MainViewModel.ProgressPercent = 0;
                return;
            }

            if (headerA.SampleRate <= 0)
            {
                ProbeButton.IsEnabled = true;
                SwitchButtonState(AnalyzeButton, false, "Sample rate is zero", true);
                MainViewModel.ProgressPercent = 0;
                return;
            }

            // Normalize data
            if (MainViewModel.NormalizeAudio)
            {

                float maxA = l1.Max();
                float minA = l1.Min();
                float absMaxA = Math.Abs(Math.Abs(maxA) > Math.Abs(minA) ? maxA : minA);
                float maxB = l2.Max();
                float minB = l2.Min();
                float absMaxB = Math.Abs(Math.Abs(maxB) > Math.Abs(minB) ? maxB : minB);


                //float offset = Math.Abs(absMaxA - absMaxB);
                if (absMaxA > absMaxB && absMaxB > 0 && absMaxB > 0)
                {
                    float offset = absMaxA / absMaxB;
                    for (int i = 0; i < l2.Length; i++)
                    {
                        l2[i] *= offset;
                    }
                }
                else if (absMaxA < absMaxB && absMaxA > 0 && absMaxB > 0)
                {
                    float offset = absMaxB / absMaxA;
                    for (int i = 0; i < l1.Length; i++)
                    {
                        l1[i] *= offset;
                    }
                }
            }
            MainViewModel.ProgressPercent = 75;
            DebugTimeSpan("normalizing audio");

            byte transparency = 200;
            // Draw audio wave lines
            await Dispatcher.UIThread.InvokeAsync(() => {
                WpfPlotAudioWaves.Plot.Clear();
                // ReSharper disable once AccessToModifiedClosure
                Signal sig = WpfPlotAudioWaves.Plot.Add.Signal(Array.ConvertAll(l1.Length > SettingsManager.ApplicationSettings.NoOfSampleToDraw
                    ? WavTools.Downsample(l1, SettingsManager.ApplicationSettings.NoOfSampleToDraw)
                    : l1, Convert.ToDouble));
                sig.LineStyle.Color = new ScottPlot.Color(sig.LineStyle.Color.Red, sig.LineStyle.Color.Green, sig.LineStyle.Color.Blue, transparency);
                // ReSharper disable once AccessToModifiedClosure
                sig = WpfPlotAudioWaves.Plot.Add.Signal(Array.ConvertAll(l2.Length > SettingsManager.ApplicationSettings.NoOfSampleToDraw
                    ? WavTools.Downsample(l2, SettingsManager.ApplicationSettings.NoOfSampleToDraw)
                    : l2, Convert.ToDouble));
                sig.LineStyle.Color = new ScottPlot.Color(sig.LineStyle.Color.Red, sig.LineStyle.Color.Green, sig.LineStyle.Color.Blue, transparency);
                WpfPlotAudioWaves.Refresh();
                WpfPlotAudioWaves.Plot.AutoScale();
            });

            DebugTimeSpan("plotting audio");

            // Padding data
            int padSize = l1.Length + l2.Length;
            // pad to next power of 2 -> faster FFT
            // padSize = Convert.ToInt32(Math.Pow(2, Math.Ceiling(Math.Log10(padSize) / Math.Log10(2))));
            padSize = MathNet.Numerics.Euclid.CeilingToPowerOfTwo(padSize);

            Array.Resize(ref l1, padSize);
            Array.Resize(ref l2, padSize);
            DebugTimeSpan("padding wav data");
            MainViewModel.ProgressPercent = 80;

            //int size = l1.Length < l2.Length ? l1.Length : l2.Length;
            //double estimatedMemoryUsage = (3 * size * sizeof(float) + 4 * SettingsManager.ApplicationSettings.NoOfSampleToDraw * sizeof(double)) / 1024.0 / 1024.0;

            // Cross correlate to itself in order to display a proper fit percentage
            float[]? corr = null;
            await Task.Run(() => Analysis.CrossCorrelation(l1, l1, out corr));
            MainViewModel.StatsMaxAa = corr!.Max();
            MainViewModel.ProgressPercent = 85;

            await Dispatcher.UIThread.InvokeAsync(() => {
                WpfPlotCrossCorrelation.Plot.Clear();
                // ReSharper disable once AccessToModifiedClosure
                Signal sig = WpfPlotCrossCorrelation.Plot.Add.Signal(Array.ConvertAll(corr!.Length > SettingsManager.ApplicationSettings.NoOfSampleToDraw
                    ? WavTools.Downsample(corr, SettingsManager.ApplicationSettings.NoOfSampleToDraw)
                    : corr, Convert.ToDouble));
                sig.LineStyle.Color = new ScottPlot.Color(Colors.DimGray.R, Colors.DimGray.G, Colors.DimGray.B, transparency);
                WpfPlotCrossCorrelation.Plot.AutoScale();
                WpfPlotCrossCorrelation.Refresh();
            });
            DebugTimeSpan("plotting correlation AA");

            // Do cross correlation between A and B
            corr = null;
            await Task.Run(() => Analysis.CrossCorrelation(l1, l2, out corr));
            MainViewModel.StatsMaxAb = corr!.Max();
            MainViewModel.CorrPercent = MainViewModel.StatsMaxAa > 0 ? 100 / MainViewModel.StatsMaxAa * MainViewModel.StatsMaxAb : 0;
            DebugTimeSpan("cross correlation");
            MainViewModel.ProgressPercent = 90;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Signal sig = WpfPlotCrossCorrelation.Plot.Add.Signal(Array.ConvertAll(corr!.Length > SettingsManager.ApplicationSettings.NoOfSampleToDraw
                    ? WavTools.Downsample(corr, SettingsManager.ApplicationSettings.NoOfSampleToDraw)
                    : corr, Convert.ToDouble));
                sig.LineStyle.Color = new ScottPlot.Color(Colors.LimeGreen.R, Colors.LimeGreen.G, Colors.LimeGreen.B, transparency);
                WpfPlotCrossCorrelation.Plot.AutoScale();
                WpfPlotCrossCorrelation.Refresh();
            });
            DebugTimeSpan("plotting correlation AB");

            if (headerA.SampleRate != headerB.SampleRate) SwitchButtonState(AnalyzeButton, true, $"Warning: Sample rate input A {headerA.SampleRate} not equal to input B {headerB.SampleRate}", true);
            MainViewModel.SyncDelay = Analysis.CalculateDelay(corr, headerA.SampleRate);

            DebugTimeSpan("getting max value");

            ProbeButton.IsEnabled = true;
            MergeButton.IsEnabled = true;

            SwitchButtonState(AnalyzeButton, false, "Analysis done");
            AnalyzeButton.ClearValue(BackgroundProperty);
            AnalyzeButton.ClearValue(ForegroundProperty);
            MainViewModel.ProgressPercent = 100;
            GC.Collect();
        }

        private async void Merge_OnClick(object sender, RoutedEventArgs e)
        {
            ProbeButton.IsEnabled = false;
            AnalyzeButton.IsEnabled = false;

            // Check A selection
            bool nothingChecked = true;
            foreach (CheckBoxMedia listBoxItem in MainViewModel.MediaDataA?.ListBoxItems!)
            {
                nothingChecked = nothingChecked && !listBoxItem.IsSelected;
            }

            if (nothingChecked)
            {
                AnalyzeButton.IsEnabled = true;
                SwitchButtonState(MergeButton, false, "Nothing selected for input A", true);
                return;
            }

            if (!MainViewModel.FfmpegAvailable)
            {
                SwitchButtonState(MergeButton, false, "No ffmpeg in PATH", true);
                return;
            }

            if (MainViewModel is { MkvmergeAvailable: false, UseMkvmerge: true })
            {
                SwitchButtonState(MergeButton, false, "No mkvmerge in PATH", true);
                return;
            }

            // Check B selection
            nothingChecked = true;
            foreach (CheckBoxMedia listBoxItem in MainViewModel.MediaDataB!.ListBoxItems)
            {
                nothingChecked = nothingChecked && !listBoxItem.IsSelected;
            }

            if (nothingChecked)
            {
                AnalyzeButton.IsEnabled = true;
                SwitchButtonState(MergeButton, false, "Nothing selected for input B", true);
                return;
            }

            try
            {
                if (!Path.IsPathRooted(FilePathOut.Text) || !Directory.Exists(Path.GetDirectoryName((string?)FilePathOut.Text)))
                {
                    AnalyzeButton.IsEnabled = true;
                    SwitchButtonState(MergeButton, false, "Output filepath invalid", true);
                    return;
                }
            }
            catch (Exception ex)
            {
                AnalyzeButton.IsEnabled = true;
                SwitchButtonState(MergeButton, false, "Output filepath invalid", true);
                Debug.WriteLine(ex);
                return;
            }

            MainViewModel.ProgressPercent = 0;
            SwitchButtonState(MergeButton, true, "Merging...");

            // Build command line argument
            // Using mkvmerge /////////////////////////////////////////////////////////////////////////////////////////
            if (MainViewModel.UseMkvmerge)
            {
                string args = $"--output \"{FilePathOut.Text}\"";

                string trackOrderAudio = "";
                string trackOrderSubs = "";
                string trackOrderVideo = "";
                string trackOrderUnknown = "";

                // Select streams from A
                List<int> audioTracks = new();
                List<int> videoTracks = new();
                List<int> subtitleTracks = new();
                List<int> attachments = new();
                bool copyChapters = false;
                foreach (CheckBoxMedia listBoxItem in MainViewModel.MediaDataA.ListBoxItems)
                {
                    if (listBoxItem.IsSelected && listBoxItem.Index > -1)
                    {
                        switch (listBoxItem.CodecType)
                        {
                            case "audio":
                                audioTracks.Add(listBoxItem.Index);
                                trackOrderAudio = trackOrderAudio != "" ? $"{trackOrderAudio},0:{listBoxItem.Index}" : $"0:{listBoxItem.Index}";
                                break;
                            case "subtitles":
                                subtitleTracks.Add(listBoxItem.Index);
                                trackOrderSubs = trackOrderSubs != "" ? $"{trackOrderSubs},0:{listBoxItem.Index}" : $"0:{listBoxItem.Index}";
                                break;
                            case "video":
                                videoTracks.Add(listBoxItem.Index);
                                trackOrderVideo = trackOrderVideo != "" ? $"{trackOrderVideo},0:{listBoxItem.Index}" : $"0:{listBoxItem.Index}";
                                break;
                            case "chapters":
                                copyChapters = true;
                                break;
                            case "attachments":
                                attachments.Add(listBoxItem.Index);
                                break;
                            default:
                                Debug.WriteLine($"WARNING: Unknown track type: {listBoxItem.CodecType}");
                                trackOrderUnknown = trackOrderUnknown != "" ? $"{trackOrderUnknown},0:{listBoxItem.Index}" : $"0:{listBoxItem.Index}";
                                break;
                        }
                    }
                }

                // Only specify tracks when not all checkboxes are checked, otherwise skip and copy everything
                args = audioTracks.Count > 0
                    ? $"{args} --audio-tracks {string.Join(",", audioTracks)}"
                    : $"{args} --no-audio";
                args = videoTracks.Count > 0
                    ? $"{args} --video-tracks {string.Join(",", videoTracks)}"
                    : $"{args} --no-video";
                args = subtitleTracks.Count > 0
                    ? $"{args} --subtitle-tracks {string.Join(",", subtitleTracks)}"
                    : $"{args} --no-subtitles";
                args = attachments.Count > 0
                    ? $"{args} --attachments {string.Join(",", attachments)}"
                    : $"{args} --no-attachments";
                if (!copyChapters) args = $"{args} --no-chapters";

                args = $"{args} \"(\" \"{FilePathA.Text}\" \")\"";

                // Select streams from B
                //args = $"{args} --sync -1:{MainViewModel.SyncOffset}";

                audioTracks.Clear();
                videoTracks.Clear();
                subtitleTracks.Clear();
                attachments.Clear();
                copyChapters = false;
                foreach (CheckBoxMedia listBoxItem in MainViewModel.MediaDataB.ListBoxItems)
                {
                    if (listBoxItem.IsSelected && listBoxItem.Index > -1)
                    {
                        switch (listBoxItem.CodecType)
                        {
                            case "audio":
                                audioTracks.Add(listBoxItem.Index);
                                args = $"{args} --sync {listBoxItem.Index}:{MainViewModel.SyncOffset}";
                                trackOrderAudio = trackOrderAudio != "" ? $"{trackOrderAudio},1:{listBoxItem.Index}" : $"1:{listBoxItem.Index}";
                                break;
                            case "subtitles":
                                subtitleTracks.Add(listBoxItem.Index);
                                args = $"{args} --sync {listBoxItem.Index}:{MainViewModel.SyncOffset}";
                                trackOrderSubs = trackOrderSubs != "" ? $"{trackOrderSubs},1:{listBoxItem.Index}" : $"1:{listBoxItem.Index}";
                                break;
                            case "video":
                                videoTracks.Add(listBoxItem.Index);
                                trackOrderVideo = trackOrderVideo != "" ? $"{trackOrderVideo},1:{listBoxItem.Index}" : $"1:{listBoxItem.Index}";
                                break;
                            case "chapters":
                                copyChapters = true;
                                break;
                            case "attachments":
                                attachments.Add(listBoxItem.Index);
                                break;
                            default:
                                Debug.WriteLine($"WARNING: Unknown track type: {listBoxItem.CodecType}");
                                args = $"{args} --sync {listBoxItem.Index}:{MainViewModel.SyncOffset}";
                                trackOrderUnknown = trackOrderUnknown != "" ? $"{trackOrderUnknown},1:{listBoxItem.Index}" : $"1:{listBoxItem.Index}";
                                break;
                        }
                    }
                }

                // Only specify tracks when not all checkboxes are checked, otherwise skip and copy everything
                args = audioTracks.Count > 0
                    ? $"{args} --audio-tracks {string.Join(",", audioTracks)}"
                    : $"{args} --no-audio";
                args = videoTracks.Count > 0
                    ? $"{args} --video-tracks {string.Join(",", videoTracks)}"
                    : $"{args} --no-video";
                args = subtitleTracks.Count > 0
                    ? $"{args} --subtitle-tracks {string.Join(",", subtitleTracks)}"
                    : $"{args} --no-subtitles";
                args = attachments.Count > 0
                    ? $"{args} --attachments {string.Join(",", attachments)}"
                    : $"{args} --no-attachments";
                if (!copyChapters) args = $"{args} --no-chapters";

                args = $"{args} \"(\" \"{FilePathB.Text}\"  \")\"";

                // Add title
                args = $"{args} --title \"{(string.IsNullOrEmpty(MainViewModel.MediaDataA.Title) ? Path.GetFileNameWithoutExtension((string?)FilePathOut.Text) : MainViewModel.MediaDataA.Title)}\"";

                // Add track order
                args = $"{args} --track-order ";
                bool firstEntryDone = false;

                if (trackOrderVideo != "")
                {
                    args = $"{args}{trackOrderVideo}";
                    firstEntryDone = true;
                }
                if (trackOrderAudio != "")
                {
                    if (firstEntryDone) args = $"{args},";
                    args = $"{args}{trackOrderAudio}";
                    firstEntryDone = true;
                }
                if (trackOrderSubs != "")
                {
                    if (firstEntryDone) args = $"{args},";
                    args = $"{args}{trackOrderSubs}";
                    firstEntryDone = true;
                }
                if (trackOrderUnknown != "")
                {
                    if (firstEntryDone) args = $"{args},";
                    args = $"{args} --track-order {trackOrderVideo},{trackOrderAudio},{trackOrderSubs},{trackOrderUnknown}";
                }

                Debug.WriteLine($"mkvmerge args: {args}");

                // Reset text output
                ExternalProcessOutputTextBox.Clear();

                _ep.CallMkvmerge(args, MkvmergeOutputHandler, _workingDir);
                await _ep.MkvmergeProcess.WaitForExitAsync();

                SwitchButtonState(MergeButton, false,
                    _ep.FfmpegWasAborted || _ep.MkvmergeProcess.ExitCode > 0
                        ? $"mkvmerge process exited with {_ep.MkvmergeProcess.ExitCode}"
                        : "Merge done",
                    _ep.FfmpegWasAborted || _ep.MkvmergeProcess.ExitCode > 0);
            }
            // Using ffmpeg   /////////////////////////////////////////////////////////////////////////////////////////
            else
            {
                string args = $"-y -i \"{FilePathA.Text}\" -itsoffset {MainViewModel.SyncOffset} -i \"{FilePathB.Text}\"";

                // Select streams from B
                foreach (CheckBoxMedia listBoxItem in MainViewModel.MediaDataA.ListBoxItems)
                {
                    if (listBoxItem.IsSelected && listBoxItem.Index > -1)
                    {
                        args = $"{args} -map 0:{listBoxItem.Index}";
                    }
                }

                // Select streams from B
                foreach (CheckBoxMedia listBoxItem in MainViewModel.MediaDataB.ListBoxItems)
                {
                    if (listBoxItem.IsSelected && listBoxItem.Index > -1)
                    {
                        args = $"{args} -map 1:{listBoxItem.Index}";
                    }
                }
                args = $"{args} -c copy \"{FilePathOut.Text}\"";

                Debug.WriteLine($"ffmpeg args: {args}");

                // Reset text output
                ExternalProcessOutputTextBox.Clear();

                _ep.CallFfmpeg(args, FfmpegOutputHandler, _workingDir);
                await _ep.FfmpegProcess!.WaitForExitAsync();

                SwitchButtonState(MergeButton, false,
                    _ep.FfmpegWasAborted || _ep.FfmpegProcess.ExitCode > 0
                        ? $"ffmpeg process exited with {_ep.FfmpegProcess.ExitCode}"
                        : "Merge done",
                    _ep.FfmpegWasAborted || _ep.FfmpegProcess.ExitCode > 0);
            }
            MainViewModel.ProgressPercent = 100;
            AnalyzeButton.IsEnabled = true;
        }

        private void Abort_OnClick(object sender, RoutedEventArgs e)
        {
            SwitchButtonState(AbortButton, true, "Aborting current process", true);
            bool ret = _ep.AbortMerge();
            SwitchButtonState(AbortButton, false, ret ? "Process aborted" : "No process to abort");
        }

        private void OpenTempFolder_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                    Process.Start("explorer.exe", _workingDir);
                else if (OperatingSystem.IsMacOS())
                    Process.Start("open", _workingDir);
                else if (OperatingSystem.IsLinux()) Process.Start("open", _workingDir);
            }
            catch (Exception exception)
            {
                Debug.WriteLine(exception);
            }
            GC.Collect();
        }

        private async void SelectOutputButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (!StorageProvider.CanSave)
            {
                _notificationManager?.Show(new Notification("Error", "Save file picker not available on this platform", NotificationType.Error));
                return;
            }

            string? pathToCheck = File.Exists(FilePathOut.Text) ? FilePathOut.Text : FilePathA.Text;
            string? dirName = File.Exists(pathToCheck) ? Path.GetDirectoryName(pathToCheck) : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string? fileName = Path.GetFileNameWithoutExtension(FilePathA.Text);
            string? fileExt = Path.GetExtension(FilePathA.Text);

            FilePickerSaveOptions filePickerOptions = new FilePickerSaveOptions()
            {
                Title = "Save new file as...",
                DefaultExtension = fileExt?.TrimStart('.'),
                ShowOverwritePrompt = true,
                SuggestedFileName = string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(fileExt) ? "SyncedMerged" : $"{fileName}_SyncedMerged"
            };

            if (!Uri.TryCreate(dirName, UriKind.Absolute, out Uri? folderLink))
            {
                Uri.TryCreate("file://" + dirName, UriKind.Absolute, out folderLink);
            }

            if (folderLink is not null)
            {
                IStorageFolder? folder = await StorageProvider.TryGetFolderFromPathAsync(folderLink);
                if (folder != null && !OperatingSystem.IsLinux()) filePickerOptions.SuggestedStartLocation = folder;
            }

            IStorageFile? dialog = await StorageProvider.SaveFilePickerAsync(filePickerOptions);

            if (!string.IsNullOrEmpty(dialog?.Name))
            {
                FilePathOut.Text = dialog.Path.LocalPath;
            }
        }

        private async void SelectInputButtonA_OnClick(object sender, RoutedEventArgs e)
        {
            if (!StorageProvider.CanOpen)
            {
                _notificationManager?.Show(new Notification("Error", "File picker not available on this platform", NotificationType.Error));
                return;
            }

            FilePickerOpenOptions filePickerOptions = new FilePickerOpenOptions()
            {
                Title = "Select file for input A",
                AllowMultiple = false
            };
            string? pathToCheck = string.IsNullOrEmpty(FilePathA.Text) ? FilePathB.Text : FilePathA.Text;
            string dirName = (File.Exists(pathToCheck) ? Path.GetDirectoryName(pathToCheck) : null) ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (!Uri.TryCreate(dirName, UriKind.Absolute, out Uri? folderLink))
            {
                Uri.TryCreate("file://" + dirName, UriKind.Absolute, out folderLink);
            }

            if (folderLink is not null)
            {
                IStorageFolder? folder = await StorageProvider.TryGetFolderFromPathAsync(folderLink);
                if (folder != null) filePickerOptions.SuggestedStartLocation = folder;
            }

            IReadOnlyList<IStorageFile> dialog = await StorageProvider.OpenFilePickerAsync(filePickerOptions);
            
            if (dialog.Count > 0)
            {
                FilePathA.Text = dialog[0].Path.LocalPath;
                CheckModeAndFiles();
            }
        }

        private async void SelectInputButtonB_OnClick(object sender, RoutedEventArgs e)
        {
            if (!StorageProvider.CanOpen)
            {
                _notificationManager?.Show(new Notification("Error", "File picker not available on this platform", NotificationType.Error));
                return;
            }

            FilePickerOpenOptions filePickerOptions = new FilePickerOpenOptions()
            {
                Title = "Select file for input B",
                AllowMultiple = false
            };
            string? pathToCheck = string.IsNullOrEmpty(FilePathB.Text) ? FilePathA.Text : FilePathB.Text;
            string dirName = (File.Exists(pathToCheck) ? Path.GetDirectoryName(pathToCheck) : null) ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (!Uri.TryCreate(dirName, UriKind.Absolute, out Uri? folderLink))
            {
                Uri.TryCreate("file://" + dirName, UriKind.Absolute, out folderLink);
            }

            if (folderLink is not null)
            {
                IStorageFolder? folder = await StorageProvider.TryGetFolderFromPathAsync(folderLink);
                if (folder != null) filePickerOptions.SuggestedStartLocation = folder;
            }

            IReadOnlyList<IStorageFile> dialog = await StorageProvider.OpenFilePickerAsync(filePickerOptions);

            if (dialog.Count > 0)
            {
                FilePathB.Text = dialog[0].Path.LocalPath;
                CheckModeAndFiles();
            }
        }

        private void TraceOnPropertyChanged(object? sender, PropertyChangedEventArgs? e)
        {
            if (e?.PropertyName == "Trace")
            {
                Dispatcher.UIThread.InvokeAsync(() => {
                    ExternalProcessOutputTextBox.Text = $"{ExternalProcessOutputTextBox.Text}\n{_trace.TraceLastEntry}";
                    ExternalProcessOutputTextBox.CaretIndex = ExternalProcessOutputTextBox.Text.Length;
                });
            }
        }

        #endregion

        #region UI helper functions

        private void ClearProbeData()
        {
            MainViewModel.MediaDataA?.Clear();
            MainViewModel.MediaDataB?.Clear();

            MainViewModel.SyncDelay = 0;
            MainViewModel.ProgressPercent = 0;

            SelectTrackA.IsEnabled = false;
            SelectTrackB.IsEnabled = false;
            AnalyzeButton.IsEnabled = false;
        }

        public void SwitchButtonState(Button btn, bool blocked, string status = "", bool error = false)
        {
            Dispatcher.UIThread.InvokeAsync(() => {
                btn.IsEnabled = !blocked;
                NormalizeCheckBox.IsEnabled = !blocked;
                UseMkvMergeCheckBox.IsEnabled = !blocked && MainViewModel.MkvmergeAvailable;
                SwitchPathsButton.IsEnabled = !blocked;
                //FilePathA.Allow = !blocked;
                //FilePathB.AllowDrop = !blocked;
                ProbeButton.IsEnabled = !blocked &&
                                        MainViewModel is { UseMkvmerge: true, MkvmergeAvailable: true } or { FfprobeAvailable: true, UseMkvmerge: false };
                SelectTrackA.IsEnabled = !blocked && MainViewModel.MediaDataA!.ComboBoxItems.Count > 0;
                SelectTrackB.IsEnabled = !blocked && MainViewModel.MediaDataB!.ComboBoxItems.Count > 0;
            });

            Dispatcher.UIThread.InvokeAsync(() => {
                StatusLabel.Content = status;
                if (blocked)
                {
                    StatusLabel.Foreground = new SolidColorBrush(Colors.DarkOrange);
                }
                else
                {
                    StatusLabel.Foreground = error ? new SolidColorBrush(Colors.Red) : new SolidColorBrush(Colors.LimeGreen);
                }

                if (error) _notificationManager?.Show(new Notification("Error", status, NotificationType.Error));
            });
        }

        public void CheckModeAndFiles()
        {
            bool wrongMode = FilePathA.Text != null 
                             && Path.GetExtension(FilePathA.Text).Length > 1
                             && (Path.GetExtension(FilePathA.Text).ToLower() == ".mkv" && !MainViewModel.UseMkvmerge
                                 || Path.GetExtension(FilePathA.Text).ToLower() != ".mkv" && MainViewModel.UseMkvmerge);
            wrongMode = wrongMode
                        || FilePathB.Text != null
                            && Path.GetExtension(FilePathB.Text).Length > 1
                            && (Path.GetExtension(FilePathB.Text).ToLower() == ".mkv" && !MainViewModel.UseMkvmerge
                                || Path.GetExtension(FilePathB.Text).ToLower() != ".mkv" && MainViewModel.UseMkvmerge);

            Dispatcher.UIThread.InvokeAsync(() => {
                if (wrongMode)
                {
                    StatusLabel.Content = MainViewModel.UseMkvmerge ? "Disable 'use mkvmerge' for current file type" : "Enable 'use mkvmerge' for current file type";
                    StatusLabel.Foreground = SolidColorBrush.Parse("#0078D4");
                    _notificationManager?.Show(new Notification("Info", StatusLabel.Content.ToString()));
                }
                else
                {
                    StatusLabel.Content = "";
                    StatusLabel.Foreground = new SolidColorBrush(Colors.Black);
                }
            });
        }

        private void DebugTimeSpan(string msg)
        {
            if (!Debugger.IsAttached) return;
            Debug.WriteLine($"{_sw.ElapsedMilliseconds}ms {msg}");
            _sw = Stopwatch.StartNew();
        }

        private void FfmpegOutputHandler(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;

            // Get process status info
            _timeMatchFfmpegProg = _reTime.Match(e.Data);

            if (_timeMatchFfmpegProg.Groups.Count > 1)
            {
                //Debug.WriteLine(_timeMatchFfmpegProg.Groups[1]);
                _currentTimeFfmpegProg = TimeSpan.Parse(_timeMatchFfmpegProg.Groups[1].Value, new CultureInfo("en-us"));
                Debug.WriteLine($"{MainViewModel.MediaDataA!.Duration - _currentTimeFfmpegProg} remaining");

                MainViewModel.ProgressPercent = 100 - (MainViewModel.MediaDataA.Duration.TotalSeconds - _currentTimeFfmpegProg.TotalSeconds) / MainViewModel.MediaDataA.Duration.TotalSeconds * 100;
            }

            Dispatcher.UIThread.InvokeAsync(() => {
                ExternalProcessOutputTextBox.Text = $"{ExternalProcessOutputTextBox.Text}{e.Data}\n";
                ExternalProcessOutputTextBox.CaretIndex = ExternalProcessOutputTextBox.Text.Length;
            });
        }

        private readonly Regex _reMkvMergeProgress = new Regex(@"(\d{1,3})\%");
        private void MkvmergeOutputHandler(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;

            // Get process status info
            _timeMatchFfmpegProg = _reMkvMergeProgress.Match(e.Data);

            if (_timeMatchFfmpegProg.Groups.Count > 1)
            {

                MainViewModel.ProgressPercent = Convert.ToInt32(_timeMatchFfmpegProg.Groups[1].Value);
            }

            Dispatcher.UIThread.InvokeAsync(() => {
                ExternalProcessOutputTextBox.Text = $"{ExternalProcessOutputTextBox.Text}{e.Data}\n";
                ExternalProcessOutputTextBox.CaretIndex = ExternalProcessOutputTextBox.Text.Length;
            });
        }

        private void File_Drop(object sender, DragEventArgs e)
        {
            // Process file names
            IEnumerable<IStorageItem>? filePaths = e.Data.GetFiles();
            if (sender is not TextBox tb || filePaths == null) return;

            // Write to textbox
            tb.Text = filePaths.First().TryGetLocalPath();

            switch (tb.Name)
            {
                case "FilePathA":
                    MainViewModel.MediaDataA?.Clear();

                    MainViewModel.SyncDelay = 0;
                    MainViewModel.ProgressPercent = 0;

                    MergeButton.IsEnabled = false;
                    AnalyzeButton.IsEnabled = false;
                    ProbeButton.Background = new SolidColorBrush(Colors.DarkOrange);
                    if (Application.Current?.ActualThemeVariant.Key.ToString() == "Dark") ProbeButton.Foreground = Brushes.Black;
                    break;
                case "FilePathB":
                    MainViewModel.MediaDataB?.Clear();

                    MainViewModel.SyncDelay = 0;
                    MainViewModel.ProgressPercent = 0;

                    MergeButton.IsEnabled = false;
                    AnalyzeButton.IsEnabled = false;
                    ProbeButton.Background = new SolidColorBrush(Colors.DarkOrange);
                    if (Application.Current?.ActualThemeVariant.Key.ToString() == "Dark") ProbeButton.Foreground = Brushes.Black;
                    break;
            }
            SwitchButtonState(ProbeButton, false);
            CheckModeAndFiles();
        }

        private void UseMkvMergeCheckBox_CheckChange(object sender, RoutedEventArgs e)
        {
            ClearProbeData();
            MergeButton.IsEnabled = false;
            AnalyzeButton.IsEnabled = false;
            if (FilePathA.Text?.Length > 0 || FilePathB.Text?.Length > 0)
            {
                ProbeButton.Background = new SolidColorBrush(Colors.DarkOrange);
                if (Application.Current?.ActualThemeVariant.Key.ToString() == "Dark") ProbeButton.Foreground = Brushes.Black;
            }
            ProbeButton.IsEnabled = MainViewModel is { UseMkvmerge: true, MkvmergeAvailable: true } or { FfprobeAvailable: true, UseMkvmerge: false };
        }

        private void SwitchInputs_OnClick(object sender, RoutedEventArgs e)
        {
            // Switch paths
            (FilePathA.Text, FilePathB.Text) = (FilePathB.Text, FilePathA.Text);

            ClearProbeData();

            MergeButton.IsEnabled = false;
            AnalyzeButton.IsEnabled = false;
            ProbeButton.Background = new SolidColorBrush(Colors.DarkOrange);
            if (Application.Current?.ActualThemeVariant.Key.ToString() == "Dark") ProbeButton.Foreground = Brushes.Black;
        }

        // TextBoxInput Check for UInt16
        private void Uint32_OnPreviewTextInput(object sender, TextInputEventArgs e)
        {
            if (!(e.Source is TextBox tb)) return;
            string? str = tb.Text?.Insert(tb.CaretIndex, e.Text!);
            e.Handled = !uint.TryParse(str, out _);
        }

        private void SelectTrack_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            AnalyzeButton.Background = new SolidColorBrush(Colors.DarkOrange);
            if (Application.Current?.ActualThemeVariant.Key.ToString() == "Dark") AnalyzeButton.Foreground = Brushes.Black;
        }

        #endregion

        #region Settings changed

        private void ThemeSelect_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (ThemeSelect.SelectedItem == null) return;

            SettingsManager.UserSettings.SelectedTheme = (ThemeVariant)ThemeSelect.SelectedItem;
            SetSelectedTheme();
        }

        private void ShowNotificationSetting_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb || cb.IsChecked == null) return;
            if ((bool)cb.IsChecked && _notificationManager == null)
            {
                _notificationManager = new WindowNotificationManager(this)
                {
                    Position = NotificationPosition.TopRight,
                    MaxItems = 5
                };
            } else _notificationManager = null;
        }

        private void WriteLogSetting_OnIsCheckedChanged(object? sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb || cb.IsChecked == null) return;
            if ((bool)cb.IsChecked)
                _trace.GenerateLogfile(Path.Combine(_workingDir, "log.txt"));
            else _trace.StopLogfile();
        }

        private void SetSelectedTheme()
        {
            if (Application.Current == null ||
                Application.Current.RequestedThemeVariant == SettingsManager.UserSettings.SelectedTheme) return;
            Application.Current.RequestedThemeVariant = SettingsManager.UserSettings.SelectedTheme;
            // Update plot theme
            SetWpfPlotStatic(WpfPlotAudioWaves);
            SetWpfPlotStatic(WpfPlotCrossCorrelation);
        }

        #endregion

    }
}