using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MergeSynced.Annotations;
using Microsoft.Win32;
using ScottPlot;
using ScottPlot.Plottable;
using Color = System.Drawing.Color;
using Path = System.IO.Path;

namespace MergeSynced
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    // ReSharper disable once RedundantExtendsListEntry
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        #region Fields and properties
        
        private Stopwatch _sw = Stopwatch.StartNew();

        private readonly ExternalProcesses _ep = new ExternalProcesses();

        private readonly Regex _reTime = new Regex(@"time=\s*([0-9\.:]*)", RegexOptions.IgnoreCase);
        private Match _timeMatchFfmpegProg;
        private TimeSpan _currentTimeFfmpegProg;

        private readonly string _workingDir;
        
        private readonly MediaData _mediaDataA = new MediaData { IsMainMedia = true};
        private readonly MediaData _mediaDataB = new MediaData();

        private const int ProbeLengthInSeconds = 20;

        private readonly bool _ffmpegExisting;
        private readonly bool _ffprobeExisting;
        private readonly bool _mkvmergeExisting;

        private double _syncDelay;
        public double SyncDelay
        {
            get => _syncDelay;
            set
            {
                _syncDelay = value;
                if (value < 0.0)
                {
                    DelayIcon.Visibility = Visibility.Hidden;
                    DelayIconAB.Visibility = Visibility.Visible;
                    DelayIconBA.Visibility = Visibility.Hidden;
                }
                else
                {
                    if (Math.Abs(value) < 0.00001)
                    {
                        DelayIcon.Visibility = Visibility.Visible;
                        DelayIconAB.Visibility = Visibility.Hidden;
                        DelayIconBA.Visibility = Visibility.Hidden;
                    }
                    else
                    {
                        DelayIcon.Visibility = Visibility.Hidden;
                        DelayIconAB.Visibility = Visibility.Hidden;
                        DelayIconBA.Visibility = Visibility.Visible;
                    }
                }
                OnPropertyChanged();
            }
        }

        private double _progressPercent;
        public double ProgressPercent
        {
            get => _progressPercent;
            set
            {
                if (value > 100) _progressPercent = 100;
                else if (value < 0 ) _progressPercent = 0;
                else _progressPercent = value;
                OnPropertyChanged();
            }
        }

        private double _statsMaxAa;
        // ReSharper disable once InconsistentNaming
        public double StatsMaxAA
        {
            get => _statsMaxAa;
            set
            {
                _statsMaxAa = value;
                OnPropertyChanged();
            }
        }

        private double _statsMaxAb;
        // ReSharper disable once InconsistentNaming
        public double StatsMaxAB
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
                    CorrPercentLabel.Foreground = new SolidColorBrush(Colors.LimeGreen);
                } else if (value > 40)
                {
                    CorrPercentLabel.Foreground = new SolidColorBrush(Colors.DarkOrange);
                }
                else
                {
                    CorrPercentLabel.Foreground = new SolidColorBrush(Colors.Red);
                }
                OnPropertyChanged();
            }
        }

        #endregion

        #region CTOR and base app events

        public MainWindow()
        {
            InitializeComponent();
            Title = $"Merge Synced - v{Assembly.GetExecutingAssembly().GetName().Version.ToString().TrimEnd('0').TrimEnd('.')}";

            SelectionA.ItemsSource = _mediaDataA.ListBoxItems;
            SelectionB.ItemsSource = _mediaDataB.ListBoxItems;

            SelectTrackA.ItemsSource = _mediaDataA.ComboBoxItems;
            SelectTrackB.ItemsSource = _mediaDataB.ComboBoxItems;

            // Plot without interaction
            SetWpfPlotStatic(WpfPlotAudioWaves);
            SetWpfPlotStatic(WpfPlotCrossCorrelation);

            // Check if ffmpeg and mkvmerge is available
            _ffmpegExisting = SearchBinaryInPath("ffmpeg.exe");
            _ffprobeExisting = SearchBinaryInPath("ffprobe.exe");
            _mkvmergeExisting = SearchBinaryInPath("mkvmerge.exe");


            if (_ffmpegExisting)
            {
                FfmpegAvailableState.Background = new SolidColorBrush(Colors.LimeGreen);
            }
            if (_ffprobeExisting)
            {
                FfprobeAvailableState.Background = new SolidColorBrush(Colors.LimeGreen);
            }
            if (_mkvmergeExisting)
            {
                MkvmergeAvailableState.Background = new SolidColorBrush(Colors.LimeGreen);
                UseMkvMergeCheckBox.IsChecked = !_ffmpegExisting && !_ffprobeExisting;
                ProbeButton.ClearValue(BackgroundProperty);
            }

            if (!_ffmpegExisting && !_ffprobeExisting && !_mkvmergeExisting) SwitchButtonState(ProbeButton, false, "No ffmpeg in PATH", true);
            if (_ffmpegExisting) _workingDir = CreateTempDir();
        }

        private void MainWindow_OnClosing(object sender, CancelEventArgs e)
        {
            // Clean up temp dir
            if (_ffmpegExisting) Directory.Delete(_workingDir, true);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

        #region Setup helper methods

        private static void SetWpfPlotStatic(WpfPlot wpfPlot)
        {
            wpfPlot.Plot.Frameless();
            wpfPlot.Plot.Grid(false);
            //wpfPlot.Plot.Style(ScottPlot.Style.Gray1);
            wpfPlot.Configuration.LeftClickDragPan = false;
            wpfPlot.Configuration.RightClickDragZoom = false;
            wpfPlot.Configuration.DoubleClickBenchmark = false;
            wpfPlot.Configuration.MiddleClickAutoAxis = false;
            wpfPlot.Configuration.MiddleClickDragZoom = false;
            wpfPlot.Configuration.ScrollWheelZoom = false;
            wpfPlot.Configuration.LockHorizontalAxis = true;
            wpfPlot.Configuration.LockVerticalAxis = true;
            wpfPlot.Refresh();
        }

        public string CreateTempDir()
        {
            string tempDirPath = Path.Combine(Path.GetTempPath(), $"{Assembly.GetExecutingAssembly().GetName().Name}_{Path.GetRandomFileName()}");
            Directory.CreateDirectory(tempDirPath);
            return tempDirPath;
        }

        private bool SearchBinaryInPath(string binaryName)
        {
            string pathData = Environment.GetEnvironmentVariable("PATH");

            if (pathData == null) return false;

            string[] paths = pathData.Split(';');

            return paths.Any(path => File.Exists(Path.Combine(path, binaryName)));
        }

        #endregion

        #region Button logic

        private async void Probe_OnClick(object sender, RoutedEventArgs e)
        {
            if (FilePathA.Text == string.Empty || FilePathB.Text == string.Empty)
            {
                SwitchButtonState(ProbeButton, false, "Please select two media files", true);
                return;
            }

            bool useMkvChecked = UseMkvMergeCheckBox.IsChecked != null && (bool)UseMkvMergeCheckBox.IsChecked;

            if (!_ffprobeExisting && !useMkvChecked)
            {
                SwitchButtonState(ProbeButton, false, "No ffprobe in PATH", true);
                return;
            }

            if (!_mkvmergeExisting && useMkvChecked)
            {
                SwitchButtonState(ProbeButton, false, "No mkvmerge in PATH", true);
                return;
            }

            AnalyzeButton.IsEnabled = false;
            MergeButton.IsEnabled = false;

            SwitchButtonState(ProbeButton, true, "Probing...");

            // Clear Data
            WpfPlotAudioWaves.Plot.Clear();
            WpfPlotAudioWaves.Refresh();
            WpfPlotCrossCorrelation.Plot.Clear();
            WpfPlotCrossCorrelation.Refresh();

            ExternalProcessOutputTextBox.Document.Blocks.Clear();

            ClearProbeData();

            // Using mkvmerge /////////////////////////////////////////////////////////////////////////////////////////
            bool result;
            if (useMkvChecked)
            {
                // A
                _ep.CallMkvmerge($"--identification-format json --identify \"{FilePathA.Text}\"",
                    _ep.ProbeOutputHandler, _workingDir);
                await _ep.MkvmergeProcess.WaitForExitAsync();

                ProgressPercent = 25;
                result = _ep.ParseMkvmergeJson(_mediaDataA);

                if (_ep.MkvmergeProcess.ExitCode > 0 || !result)
                {
                    ProgressPercent = 0;
                    SwitchButtonState(ProbeButton, false, "Error probing input A", true);
                    return;
                }

                ProgressPercent = 50;
                SelectTrackA.IsEnabled = true;

                // B
                _ep.CallMkvmerge($"--identification-format json --identify \"{FilePathB.Text}\"",
                    _ep.ProbeOutputHandler, _workingDir);
                await _ep.MkvmergeProcess.WaitForExitAsync();

                ProgressPercent = 75;
                result = _ep.ParseMkvmergeJson(_mediaDataB);

                if (_ep.MkvmergeProcess.ExitCode > 0 || !result)
                {
                    ProgressPercent = 0;
                    SwitchButtonState(ProbeButton, false, "Error probing input B", true);
                    return;
                }

                ProgressPercent = 100;
                SelectTrackB.IsEnabled = true;
            }
            // Using ffmpeg   /////////////////////////////////////////////////////////////////////////////////////////
            else
            {
                _ep.CallFfprobe(FilePathA.Text, _workingDir);
                await _ep.FfprobeProcess.WaitForExitAsync();

                ProgressPercent = 25;
                result = _ep.ParseFfprobeJson(_mediaDataA);

                if (_ep.FfprobeProcess.ExitCode > 0 || !result)
                {
                    ProgressPercent = 0;
                    SwitchButtonState(ProbeButton, false, "Error probing input A", true);
                    return;
                }
                ProgressPercent = 50;
                SelectTrackA.IsEnabled = true;

                _ep.CallFfprobe(FilePathB.Text, _workingDir);
                await _ep.FfprobeProcess.WaitForExitAsync();

                ProgressPercent = 75;
                result = _ep.ParseFfprobeJson(_mediaDataB);

                if (_ep.FfprobeProcess.ExitCode > 0 || !result)
                {
                    ProgressPercent = 0;
                    SwitchButtonState(ProbeButton, false, "Error probing input B", true);
                    return;
                }
                ProgressPercent = 100;
                SelectTrackB.IsEnabled = true;
            }
            
            AnalyzeButton.IsEnabled = true;
            ProbeButton.ClearValue(BackgroundProperty);
            SwitchButtonState(ProbeButton, false, "Probing done");
        }

        private async void Analyze_OnClick(object sender, RoutedEventArgs e)
        {
            SwitchButtonState(AnalyzeButton, true, "Analyzing...");

            if (FilePathA.Text == string.Empty || FilePathB.Text == string.Empty)
            {
                SwitchButtonState(AnalyzeButton, false, "Please select two media files", true);
                return;
            }

            if (!_ffmpegExisting)
            {
                SwitchButtonState(AnalyzeButton, false, "No ffmpeg in PATH", true);
                return;
            }

            ProbeButton.IsEnabled = false;
            MergeButton.IsEnabled = false;

            // Clear Data
            Annotation graphLabel = WpfPlotAudioWaves.Plot.AddAnnotation("Updating", WpfPlotAudioWaves.ActualWidth * 0.5 - 40, WpfPlotAudioWaves.ActualHeight * 0.5 - 10);
            graphLabel.Font.Size = 24;
            graphLabel.Font.Name = "Impact";
            graphLabel.Font.Color = Color.Red;
            graphLabel.Shadow = false;
            graphLabel.BackgroundColor = Color.FromArgb(100, Color.DimGray);
            graphLabel.BorderWidth = 2;
            graphLabel.BorderColor = Color.Red;
            WpfPlotAudioWaves.Refresh();

            graphLabel = WpfPlotCrossCorrelation.Plot.AddAnnotation("Updating", WpfPlotCrossCorrelation.ActualWidth * 0.5 - 40, WpfPlotCrossCorrelation.ActualHeight * 0.5 - 10);
            graphLabel.Font.Size = 24;
            graphLabel.Font.Name = "Impact";
            graphLabel.Font.Color = Color.Red;
            graphLabel.Shadow = false;
            graphLabel.BackgroundColor = Color.FromArgb(100, Color.DimGray);
            graphLabel.BorderWidth = 2;
            graphLabel.BorderColor = Color.Red;
            WpfPlotCrossCorrelation.Refresh();

            ExternalProcessOutputTextBox.Document.Blocks.Clear();
            
            if (_mediaDataA.Duration.TotalSeconds == 0)
            {
                ProbeButton.IsEnabled = true;
                SwitchButtonState(AnalyzeButton, false, "Total seconds is 0 for input A", true);
                ProgressPercent = 0;
                return;
            }
            
            if (_mediaDataB.Duration.TotalSeconds == 0)
            {
                ProbeButton.IsEnabled = true;
                SwitchButtonState(AnalyzeButton, false, "Total seconds is 0 for input B", true);
                ProgressPercent = 0;
                return;
            }

            bool durationResult = int.TryParse(SampleDuration.Text, out int sampleDurationSeconds);
            if (!durationResult) sampleDurationSeconds = ProbeLengthInSeconds;
            int.TryParse(SampleStart.Text, out int startTime);

            // Down mix to mono and shorten files
            string args;

            // A
            ComboBoxItem co = SelectTrackA.SelectedItem as ComboBoxItem;
            int selectedTrack = int.Parse(co != null ? co.Content.ToString() : "0");
            string inputA = Path.Combine(_workingDir, "inputA.wav");

            if (_mediaDataA.Duration.TotalSeconds > sampleDurationSeconds)
            {
                if (startTime + sampleDurationSeconds > _mediaDataA.Duration.TotalSeconds) startTime = (int)_mediaDataA.Duration.TotalSeconds - sampleDurationSeconds;
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
            await _ep.FfmpegProcess.WaitForExitAsync();

            if (_ep.FfmpegProcess.ExitCode > 0)
            {
                ProgressPercent = 0;
                ProbeButton.IsEnabled = true;
                SwitchButtonState(AnalyzeButton, false, "Error converting audio input A", true);
                return;
            }
            ProgressPercent = 45;

            // B
            co = SelectTrackB.SelectedItem as ComboBoxItem;
            selectedTrack = int.Parse(co != null ? co.Content.ToString() : "0");
            string inputB = Path.Combine(_workingDir, "inputB.wav");

            if (_mediaDataB.Duration.TotalSeconds > sampleDurationSeconds)
            {
                if (startTime + sampleDurationSeconds > _mediaDataB.Duration.TotalSeconds) startTime = (int)_mediaDataB.Duration.TotalSeconds - sampleDurationSeconds;
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
                ProgressPercent = 0;
                ProbeButton.IsEnabled = true;
                SwitchButtonState(AnalyzeButton, false, "Error converting audio input B", true);
                return;
            }
            ProgressPercent = 60;

            // Load generated audio files
            Audio au = new Audio();
            WavHeader headerA = au.ReadWav(inputA, out float[] l1);
            ProgressPercent = 65;
            WavHeader headerB = au.ReadWav(inputB, out float[] l2);
            ProgressPercent = 70;

            DebugTimeSpan("reading wav files");

            if (l1 == null || l2 == null || headerA == null || headerB == null)
            {
                ProbeButton.IsEnabled = true;
                SwitchButtonState(AnalyzeButton, false, "Error converting audio", true);
                ProgressPercent = 0;
                return;
            }

            if (headerA.SampleRate <= 0)
            {
                ProbeButton.IsEnabled = true;
                SwitchButtonState(AnalyzeButton, false, "Sample rate is zero", true);
                ProgressPercent = 0;
                return;
            }

            // Normalize data
            if (NormalizeCheckBox.IsChecked != null && (bool)NormalizeCheckBox.IsChecked)
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
            ProgressPercent = 75;

            // Draw audio wave lines
            await WpfPlotAudioWaves.Dispatcher.BeginInvoke(new Action(() => {
                WpfPlotAudioWaves.Plot.Clear();
                // ReSharper disable once AccessToModifiedClosure
                SignalPlot sig = WpfPlotAudioWaves.Plot.AddSignal(Array.ConvertAll(l1, Convert.ToDouble));
                sig.Color = Color.FromArgb(0x90, sig.Color.R, sig.Color.G, sig.Color.B);
                // ReSharper disable once AccessToModifiedClosure
                sig = WpfPlotAudioWaves.Plot.AddSignal(Array.ConvertAll(l2, Convert.ToDouble));
                sig.Color = Color.FromArgb(0x90, sig.Color.R, sig.Color.G, sig.Color.B);
                WpfPlotAudioWaves.Refresh();
            }));

            // Padding data
            int padSize = l1.Length + l2.Length;
            // pad to next power of 2 -> faster FFT
            // padSize = Convert.ToInt32(Math.Pow(2, Math.Ceiling(Math.Log10(padSize) / Math.Log10(2))));
            padSize = MathNet.Numerics.Euclid.CeilingToPowerOfTwo(padSize);

            Array.Resize(ref l1, padSize);
            Array.Resize(ref l2, padSize);
            DebugTimeSpan("padding wav data");
            ProgressPercent = 80;

            // Cross correlate to itself in order to display a proper fit percentage
            float[] corr = null;
            await Task.Run(() => Analysis.CrossCorrelation(l1, l1, out corr));
            StatsMaxAA = corr.Max();
            ProgressPercent = 85;

            await WpfPlotCrossCorrelation.Dispatcher.BeginInvoke(new Action(() => {
                WpfPlotCrossCorrelation.Plot.Clear();
                // ReSharper disable once AccessToModifiedClosure
                SignalPlot sig = WpfPlotCrossCorrelation.Plot.AddSignal(Array.ConvertAll(corr, Convert.ToDouble));
                sig.Color = Color.DimGray;
                sig.Color = Color.FromArgb(0x90, sig.Color.R, sig.Color.G, sig.Color.B);
                WpfPlotCrossCorrelation.Refresh();
            }));

            // Do cross correlation between A and B
            corr = null;
            await Task.Run(() => Analysis.CrossCorrelation(l1, l2, out corr));
            StatsMaxAB = corr.Max();
            CorrPercent = StatsMaxAA > 0 ? 100 / StatsMaxAA * StatsMaxAB : 0;
            DebugTimeSpan("cross correlation");
            ProgressPercent = 90;

            await WpfPlotCrossCorrelation.Dispatcher.BeginInvoke(new Action(() =>
            {
                SignalPlot sig = WpfPlotCrossCorrelation.Plot.AddSignal(Array.ConvertAll(corr, Convert.ToDouble));
                sig.Color = Color.LimeGreen;
                sig.Color = Color.FromArgb(0x90, sig.Color.R, sig.Color.G, sig.Color.B);
                WpfPlotCrossCorrelation.Plot.AxisAuto();
                WpfPlotCrossCorrelation.Refresh();
            }));
            DebugTimeSpan("plotting");

            if (headerA.SampleRate != headerB.SampleRate) SwitchButtonState(AnalyzeButton, true, $"Warning: Sample rate input A {headerA.SampleRate} not equal to input B {headerB.SampleRate}", true);
            SyncDelay = Analysis.CalculateDelay(corr, headerA.SampleRate);

            DebugTimeSpan("getting max value");

            ProbeButton.IsEnabled = true;
            MergeButton.IsEnabled = true;

            SwitchButtonState(AnalyzeButton, false, "Analysis done");
            AnalyzeButton.ClearValue(BackgroundProperty);
            ProgressPercent = 100;
            GC.Collect();
        }

        private async void Merge_OnClick(object sender, RoutedEventArgs e)
        {
            ProbeButton.IsEnabled = false;
            AnalyzeButton.IsEnabled = false;

            // Check A selection
            bool nothingChecked = true;
            foreach (CheckBoxMedia listBoxItem in _mediaDataA.ListBoxItems)
            {
                if (listBoxItem?.IsChecked != null) nothingChecked = nothingChecked && !(bool)listBoxItem.IsChecked;
            }

            if (nothingChecked)
            {
                AnalyzeButton.IsEnabled = true;
                SwitchButtonState(MergeButton, false, "Nothing selected for input A", true);
                return;
            }

            if (!_ffmpegExisting)
            {
                SwitchButtonState(MergeButton, false, "No ffmpeg in PATH", true);
                return;
            }

            if (!_mkvmergeExisting)
            {
                SwitchButtonState(MergeButton, false, "No mkvmerge in PATH", true);
                return;
            }

            // Check B selection
            nothingChecked = true;
            foreach (CheckBoxMedia listBoxItem in _mediaDataB.ListBoxItems)
            {
                if (listBoxItem?.IsChecked != null) nothingChecked = nothingChecked && !(bool)listBoxItem.IsChecked;
            }

            if (nothingChecked)
            {
                AnalyzeButton.IsEnabled = true;
                SwitchButtonState(MergeButton, false, "Nothing selected for input B", true);
                return;
            }

            try
            {
                if (!Path.IsPathRooted(FilePathOut.Text) || !Directory.Exists(Path.GetDirectoryName(FilePathOut.Text)))
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

            ProgressPercent = 0;
            SwitchButtonState(MergeButton, true, "Merging...");

            // Build command line argument
            // Using mkvmerge /////////////////////////////////////////////////////////////////////////////////////////
            if (UseMkvMergeCheckBox.IsChecked != null && (bool)UseMkvMergeCheckBox.IsChecked)
            {
                string delayFormatted = Convert.ToInt32(Math.Round(-1 * SyncDelay * 1000)).ToString(); // Delay has to be inverted and in ms
                string args = $"--output \"{FilePathOut.Text}\"";

                string trackOrderAudio = "";
                string trackOrderSubs = "";
                string trackOrderVideo = "";
                string trackOrderUnknown = "";

                // Select streams from A
                List<int> audioTracks = new List<int>();
                List<int> videoTracks = new List<int>();
                List<int> subtitleTracks = new List<int>();
                List<int> attachments = new List<int>();
                bool copyChapters = false;
                foreach (CheckBoxMedia listBoxItem in _mediaDataA.ListBoxItems)
                {
                    if (listBoxItem?.IsChecked != null && (bool)listBoxItem.IsChecked && listBoxItem.Index > -1)
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
                //args = $"{args} --sync -1:{delayFormatted}";

                audioTracks.Clear();
                videoTracks.Clear();
                subtitleTracks.Clear();
                attachments.Clear();
                copyChapters = false;
                foreach (CheckBoxMedia listBoxItem in _mediaDataB.ListBoxItems)
                {
                    if (listBoxItem?.IsChecked != null && (bool)listBoxItem.IsChecked && listBoxItem.Index > -1)
                    {
                        switch (listBoxItem.CodecType)
                        {
                            case "audio":
                                audioTracks.Add(listBoxItem.Index);
                                args = $"{args} --sync {listBoxItem.Index}:{delayFormatted}";
                                trackOrderAudio = trackOrderAudio != "" ? $"{trackOrderAudio},1:{listBoxItem.Index}" : $"1:{listBoxItem.Index}";
                                break;
                            case "subtitles":
                                subtitleTracks.Add(listBoxItem.Index);
                                args = $"{args} --sync {listBoxItem.Index}:{delayFormatted}";
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
                                args = $"{args} --sync {listBoxItem.Index}:{delayFormatted}";
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
                args = $"{args} --title \"{(_mediaDataA.Title == string.Empty ? Path.GetFileNameWithoutExtension(FilePathOut.Text) : _mediaDataA.Title)}\"";

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
                ExternalProcessOutputTextBox.Document.Blocks.Clear();

                _ep.CallMkvmerge(args, MkvmergeOutputHandler, _workingDir);
                await _ep.MkvmergeProcess.WaitForExitAsync();
                
                SwitchButtonState(MergeButton, false,
                    _ep.FfmpegWasAborted || _ep.MkvmergeProcess.ExitCode > 0
                        ? "mkvmerge process aborted"
                        : "Merge done", _ep.FfmpegWasAborted || _ep.MkvmergeProcess.ExitCode > 0);
            }
            // Using ffmpeg   /////////////////////////////////////////////////////////////////////////////////////////
            else
            {
                string delayFormatted = Convert.ToString(-1 * SyncDelay, new CultureInfo("en-us")); // Delay has to be inverted
                string args = $"-y -i \"{FilePathA.Text}\" -itsoffset {delayFormatted} -i \"{FilePathB.Text}\"";

                // Select streams from B
                foreach (CheckBoxMedia listBoxItem in _mediaDataA.ListBoxItems)
                {
                    if (listBoxItem?.IsChecked != null && (bool)listBoxItem.IsChecked && listBoxItem.Index > -1)
                    {
                        args = $"{args} -map 0:{listBoxItem.Index}";
                    }
                }

                // Select streams from B
                foreach (CheckBoxMedia listBoxItem in _mediaDataB.ListBoxItems)
                {
                    if (listBoxItem?.IsChecked != null && (bool)listBoxItem.IsChecked && listBoxItem.Index > -1)
                    {
                        args = $"{args} -map 1:{listBoxItem.Index}";
                    }
                }
                args = $"{args} -c copy \"{FilePathOut.Text}\"";

                Debug.WriteLine($"ffmpeg args: {args}");

                // Reset text output
                ExternalProcessOutputTextBox.Document.Blocks.Clear();

                _ep.CallFfmpeg(args, FfmpegOutputHandler, _workingDir);
                await _ep.FfmpegProcess.WaitForExitAsync();
                
                SwitchButtonState(MergeButton, false,
                    _ep.FfmpegWasAborted || _ep.FfmpegProcess.ExitCode > 0 ? "ffmpeg process aborted" : "Merge done",
                    _ep.FfmpegWasAborted || _ep.FfmpegProcess.ExitCode > 0);
            }
            ProgressPercent = 100;
            AnalyzeButton.IsEnabled = true;
        }

        private void Abort_OnClick(object sender, RoutedEventArgs e)
        {
            SwitchButtonState(AbortButton, true, "Aborting ffmpeg process", true);
            bool ret = _ep.AbortMerge();
            SwitchButtonState(AbortButton, false, ret ? "ffmpeg process aborted" : "no merge process to abort");
        }

        private void OpenTempFolder_OnClick(object sender, RoutedEventArgs e)
        {
            Process.Start(_workingDir);
            GC.Collect();
        }

        private void SelectOutputButton_OnClick(object sender, RoutedEventArgs e)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();

            string dirName = File.Exists(FilePathA.Text) ? Path.GetDirectoryName(FilePathA.Text) : null;
            string fileName = Path.GetFileNameWithoutExtension(FilePathA.Text);
            string fileExt = Path.GetExtension(FilePathA.Text);
            saveFileDialog.InitialDirectory = dirName ?? @"C:\temp";
            saveFileDialog.FileName = fileName != string.Empty && fileExt != string.Empty ? $"{fileName}_SyncedMerged{fileExt}" : "SyncedMerged.mp4";
            if (saveFileDialog.ShowDialog() == true) FilePathOut.Text = saveFileDialog.FileName;
        }

        private void SelectInputButtonA_OnClick(object sender, RoutedEventArgs e)
        {
            OpenFileDialog saveFileDialog = new OpenFileDialog();

            string pathToCheck = FilePathA.Text != string.Empty ? FilePathA.Text : FilePathB.Text;
            string dirName = File.Exists(pathToCheck) ? Path.GetDirectoryName(pathToCheck) : null;
            string fileName = Path.GetFileNameWithoutExtension(pathToCheck);
            saveFileDialog.InitialDirectory = dirName ?? @"C:\";
            saveFileDialog.FileName = fileName;
            if (saveFileDialog.ShowDialog() == true) FilePathA.Text = saveFileDialog.FileName;
        }

        private void SelectInputButtonB_OnClick(object sender, RoutedEventArgs e)
        {
            OpenFileDialog saveFileDialog = new OpenFileDialog();

            string pathToCheck = FilePathB.Text != string.Empty ? FilePathB.Text : FilePathA.Text;
            string dirName = File.Exists(pathToCheck) ? Path.GetDirectoryName(pathToCheck) : null;
            string fileName = Path.GetFileNameWithoutExtension(pathToCheck);
            saveFileDialog.InitialDirectory = dirName ?? @"C:\";
            saveFileDialog.FileName = fileName;
            if (saveFileDialog.ShowDialog() == true) FilePathB.Text = saveFileDialog.FileName;
        }

        #endregion

        #region UI helper functions

        private void ClearProbeData()
        {
            _mediaDataA.Clear();
            _mediaDataB.Clear();
            
            SyncDelay = 0;
            ProgressPercent = 0;

            SelectTrackA.IsEnabled = false;
            SelectTrackB.IsEnabled = false;
            AnalyzeButton.IsEnabled = false;
        }

        public void SwitchButtonState(Button btn, bool blocked, string status = "", bool error = false)
        {
            btn.Dispatcher.BeginInvoke(new Action(() => {
                btn.IsEnabled = !blocked;
                NormalizeCheckBox.IsEnabled = !blocked;
                UseMkvMergeCheckBox.IsEnabled = !blocked && _mkvmergeExisting;
                SwitchPathsButton.IsEnabled = !blocked;
                FilePathA.AllowDrop = !blocked;
                FilePathB.AllowDrop = !blocked;
                ProbeButton.IsEnabled = !blocked && 
                                        (UseMkvMergeCheckBox.IsChecked != null && (bool)UseMkvMergeCheckBox.IsChecked && _mkvmergeExisting || _ffprobeExisting);
                SelectTrackA.IsEnabled = !blocked && _mediaDataA.ComboBoxItems.Count > 0;
                SelectTrackB.IsEnabled = !blocked && _mediaDataB.ComboBoxItems.Count > 0;
            }));

            StatusLabel.Dispatcher.BeginInvoke(new Action(() => {
                StatusLabel.Content = status;
                if (blocked)
                {
                    StatusLabel.Foreground = new SolidColorBrush(Colors.DarkOrange);
                }
                else
                {
                    StatusLabel.Foreground = error ? new SolidColorBrush(Colors.Red) : new SolidColorBrush(Colors.LimeGreen);
                }
            }));
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
                Debug.WriteLine($"{_mediaDataA.Duration - _currentTimeFfmpegProg} remaining");

                ProgressPercent = 100 - (_mediaDataA.Duration.TotalSeconds - _currentTimeFfmpegProg.TotalSeconds) / _mediaDataA.Duration.TotalSeconds * 100;
            }

            ExternalProcessOutputTextBox.Dispatcher.BeginInvoke(new Action(() => {
                ExternalProcessOutputTextBox.AppendText(e.Data);
                ExternalProcessOutputTextBox.AppendText("\u2028"); // Line break, not paragraph break
                ExternalProcessOutputTextBox.ScrollToEnd();
            }));
        }

        private readonly Regex _reMkvMergeProgress = new Regex(@"(\d{1,3})\%");
        private void MkvmergeOutputHandler(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;

            // Get process status info
            _timeMatchFfmpegProg = _reMkvMergeProgress.Match(e.Data);

            if (_timeMatchFfmpegProg.Groups.Count > 1)
            {

                ProgressPercent = Convert.ToInt32(_timeMatchFfmpegProg.Groups[1].Value);
            }

            ExternalProcessOutputTextBox.Dispatcher.BeginInvoke(new Action(() => {
                ExternalProcessOutputTextBox.AppendText(e.Data);
                ExternalProcessOutputTextBox.AppendText("\u2028"); // Line break, not paragraph break
                ExternalProcessOutputTextBox.ScrollToEnd();
            }));
        }

        private void File_Drop(object sender, DragEventArgs e)
        {
            // Check for file list
            if (!(e.Data is DataObject dataObject) || !dataObject.ContainsFileDropList()) return;

            // Process file names
            StringCollection filePaths = dataObject.GetFileDropList();

            // Write to textbox
            if (!(sender is TextBox tb) || filePaths.Count == 0) return;
            tb.Text = filePaths[0];
            switch (tb.Name)
            {
                case "FilePathA":
                    _mediaDataA.Clear();

                    SyncDelay = 0;
                    ProgressPercent = 0;

                    MergeButton.IsEnabled = false;
                    AnalyzeButton.IsEnabled = false;
                    ProbeButton.Background = new SolidColorBrush(Colors.DarkOrange);
                    break;
                case "FilePathB":
                    _mediaDataB.Clear();

                    SyncDelay = 0;
                    ProgressPercent = 0;

                    MergeButton.IsEnabled = false;
                    AnalyzeButton.IsEnabled = false;
                    ProbeButton.Background = new SolidColorBrush(Colors.DarkOrange);
                    break;
            }
            SwitchButtonState(ProbeButton, false);
            ProbeButton.IsEnabled = _ffmpegExisting || !_mkvmergeExisting || UseMkvMergeCheckBox.IsChecked == null || !(bool)UseMkvMergeCheckBox.IsChecked;
        }

        private void FileDrop_PreviewAck(object sender, DragEventArgs e)
        {
            e.Handled = true;
        }
        
        private void UseMkvMergeCheckBox_CheckChange(object sender, RoutedEventArgs e)
        {
            ClearProbeData();
            MergeButton.IsEnabled = false;
            AnalyzeButton.IsEnabled = false;
            if (FilePathA.Text.Length > 0 || FilePathB.Text.Length > 0) ProbeButton.Background = new SolidColorBrush(Colors.DarkOrange);
            ProbeButton.IsEnabled =
                UseMkvMergeCheckBox.IsChecked != null && (bool)UseMkvMergeCheckBox.IsChecked && _mkvmergeExisting ||
                _ffprobeExisting;
        }

        private void SwitchInputs_OnClick(object sender, RoutedEventArgs e)
        {
            // Switch paths
            (FilePathA.Text, FilePathB.Text) = (FilePathB.Text, FilePathA.Text);

            ClearProbeData();

            MergeButton.IsEnabled = false;
            AnalyzeButton.IsEnabled = false;
            ProbeButton.Background = new SolidColorBrush(Colors.DarkOrange);
        }

        // TextBoxInput Check for UInt16
        private void Uint32_OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (!(e.Source is TextBox tb)) return;
            string str = tb.Text.Insert(tb.CaretIndex, e.Text);
            e.Handled = !uint.TryParse(str, out _);
        }

        private void ComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            AnalyzeButton.Background = new SolidColorBrush(Colors.DarkOrange);
        }

        #endregion

    }
}
