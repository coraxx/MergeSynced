using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Avalonia.Controls;
using Avalonia.Media;
using MergeSynced.Controls;
using Newtonsoft.Json.Linq;

namespace MergeSynced.Tools
{
    /// <summary>
    /// Calling external tools ffmpeg and mkvtoolnix to probe and merge input files.
    /// </summary>
    public class ExternalProcesses
    {
        #region Fields

        public Process? FfmpegProcess;
        public Process? FfprobeProcess;
        public bool FfmpegWasAborted;

        public Process MkvmergeProcess = null!;
        public bool MkvmergeWasAborted;

        #endregion

        #region Probing output handler

        private readonly StringBuilder _probeJson = new StringBuilder();
        public void ProbeOutputHandler(object sender, DataReceivedEventArgs e)
        {
            _probeJson.AppendLine(e.Data);
        }

        #endregion

        #region ffmpeg

        public void CallFfmpeg(string args, DataReceivedEventHandler outputHandler, string workingDir = @"C:\temp")
        {
            if (FfmpegProcess != null)
            {
                AbortMerge();
            }

            FfmpegWasAborted = false;
            FfmpegProcess = new Process();
            FfmpegProcess.StartInfo.FileName = "ffmpeg";
            FfmpegProcess.StartInfo.Arguments = args;
            FfmpegProcess.StartInfo.WorkingDirectory = workingDir;

            // Options
            FfmpegProcess.StartInfo.CreateNoWindow = true;
            FfmpegProcess.StartInfo.UseShellExecute = false;
            FfmpegProcess.StartInfo.RedirectStandardInput = true;
            FfmpegProcess.StartInfo.RedirectStandardOutput = true;
            FfmpegProcess.StartInfo.RedirectStandardError = true;
            //_ffmpegProcess.EnableRaisingEvents = true;
            //_ffmpegProcess.Exited += delegate {/* clean up*/};

            // Receive StdOut and StdErr
            FfmpegProcess.OutputDataReceived += outputHandler;
            FfmpegProcess.ErrorDataReceived += outputHandler;

            // Start process
            try
            {
                FfmpegProcess.Start();
                FfmpegProcess.BeginOutputReadLine();
                FfmpegProcess.BeginErrorReadLine();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                FfmpegProcess.Close();
                FfmpegProcess.Dispose();
                FfmpegProcess = null;
            }
        }

        public void CallFfprobe(string filePath, string workingDir = @"C:\temp")
        {
            if (FfprobeProcess != null)
            {
                try
                {
                    if (!FfprobeProcess.HasExited)
                    {
                        Debug.WriteLine("Killing ffprobe...");
                        FfprobeProcess.Kill();
                        Debug.WriteLine("... ffprobe killed at start");
                    }
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e);
                }
                finally
                {
                    FfprobeProcess.Close();
                    FfprobeProcess.Dispose();
                    FfprobeProcess = null;
                }
            }

            FfprobeProcess = new Process();
            FfprobeProcess.StartInfo.FileName = "ffprobe";
            FfprobeProcess.StartInfo.Arguments = $"-v quiet -print_format json -show_format -show_streams -print_format json \"{filePath}\"";
            FfprobeProcess.StartInfo.WorkingDirectory = workingDir;

            // Options
            FfprobeProcess.StartInfo.CreateNoWindow = true;
            FfprobeProcess.StartInfo.UseShellExecute = false;
            FfprobeProcess.StartInfo.RedirectStandardInput = true;
            FfprobeProcess.StartInfo.RedirectStandardOutput = true;
            FfprobeProcess.StartInfo.RedirectStandardError = true;

            // Receive StdOut and StdErr
            _probeJson.Clear();
            FfprobeProcess.OutputDataReceived += ProbeOutputHandler;
            FfprobeProcess.ErrorDataReceived += ProbeOutputHandler;

            // Start process
            try
            {
                FfprobeProcess.Start();
                FfprobeProcess.BeginOutputReadLine();
                FfprobeProcess.BeginErrorReadLine();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                FfprobeProcess.Close();
                FfprobeProcess.Dispose();
                FfprobeProcess = null;
            }
        }

        public bool ParseFfprobeJson(MediaData? md)
        {
            if (md == null) return false;
            md.Clear();

            try
            {
                JObject json = JObject.Parse(_probeJson.ToString());

                if (json["format"] == null || json["streams"] == null || json["format"]?["duration"] == null) return false;

                // Get length of file
                md.Duration = TimeSpan.FromSeconds(Convert.ToDouble(json["format"]?["duration"]?.ToString(), new CultureInfo("en-us")));

                bool audioTrackSelected = false;

                foreach (JToken stream in json["streams"]!)
                {
                    string? language = string.Empty;
                    if (stream["tags"] != null)
                    {
                        if (stream["tags"]?["language"] != null) language = stream["tags"]?["language"]?.ToString();
                    }
                    else
                    {
                        language = "unknown";
                    }

                    string streamInfo =
                        $"idx: {stream["index"]}; type: {stream["codec_type"]}; codec: {stream["codec_name"]}; language: {language};";
                    Debug.WriteLine(streamInfo);
                    CheckBoxMedia cb = new CheckBoxMedia();
                    if (int.TryParse(stream["index"]?.ToString(), out int index))
                    {
                        cb.Index = index;
                    }

                    cb.LanguageId = language;
                    cb.CodecType = stream["codec_type"]?.ToString();
                    cb.Description = streamInfo;
                    cb.IsSelected = md.IsMainMedia;

                    // Color code
                    switch (cb.CodecType)
                    {
                        case "audio":
                            cb.TypeBrush = new SolidColorBrush(Colors.LimeGreen);
                            ComboBoxItem co = new ComboBoxItem
                            {
                                Content = cb.Index.ToString(),
                                IsSelected = !audioTrackSelected
                            };
                            md.ComboBoxItems.Add(co);
                            audioTrackSelected = true;
                            break;
                        case "subtitle":
                            cb.TypeBrush = new SolidColorBrush(Colors.Yellow);
                            break;
                        case "video":
                            cb.TypeBrush = new SolidColorBrush(Colors.DodgerBlue);
                            break;
                    }

                    md.ListBoxItems.Add(cb);
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                return false;
            }

            return true;
        }

        #endregion

        #region mkvtoolnix

        public bool AbortMerge()
        {
            if (FfmpegProcess == null && MkvmergeProcess == null!) return false;

            if (FfmpegProcess != null)
            {
                try
                {
                    Debug.WriteLine("Sending quit signal to ffmpeg process...");
                    // Get StdInput from ffmpeg process and send q to quit gracefully
                    StreamWriter streamWriter = FfmpegProcess.StandardInput;
                    streamWriter.WriteLine("q");

                    // Give process time to quit
                    FfmpegProcess.WaitForExit(5000);
                    Debug.WriteLine("Checking if ffmpeg quit gracefully");

                    if (!FfmpegProcess.HasExited)
                    {
                        Debug.WriteLine("Killing ffmpeg...");
                        FfmpegProcess.Kill();
                        Debug.WriteLine("... ffmpeg killed");
                    }
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e);
                }
                finally
                {
                    FfmpegProcess.Close();
                    FfmpegProcess.Dispose();
                    FfmpegProcess = null;
                }

                FfmpegWasAborted = true;
            }

            if (MkvmergeProcess == null!) return true;

            try
            {
                Debug.WriteLine("Sending quit signal to mkvmerge process...");
                // Get StdInput from mkvmerge process and send ctrl+c
                StreamWriter streamWriter = MkvmergeProcess.StandardInput;
                streamWriter.WriteLine("\x3");

                // Give process time to quit
                MkvmergeProcess.WaitForExit(5000);
                Debug.WriteLine("Checking if mkvmerge quit gracefully");

                if (!MkvmergeProcess.HasExited)
                {
                    Debug.WriteLine("Killing mkvmerge...");
                    MkvmergeProcess.Kill();
                    Debug.WriteLine("... mkvmerge killed");
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
            }
            finally
            {
                MkvmergeProcess.Close();
                MkvmergeProcess.Dispose();
                MkvmergeProcess = null!;
            }

            MkvmergeWasAborted = true;

            return true;
        }

        public void CallMkvmerge(string args, DataReceivedEventHandler outputHandler, string workingDir = @"C:\temp")
        {
            if (MkvmergeProcess != null!)
            {
                try
                {
                    Debug.WriteLine("Sending quit signal to mkvmerge process...");
                    // Get StdInput from mkvmerge process and send ctrl+c
                    StreamWriter streamWriter = MkvmergeProcess.StandardInput;
                    streamWriter.WriteLine("\x3");

                    // Give process time to quit
                    MkvmergeProcess.WaitForExit(5000);
                    Debug.WriteLine("Checking if mkvmerge quit gracefully");

                    if (!MkvmergeProcess.HasExited)
                    {
                        Debug.WriteLine("Killing mkvmerge...");
                        MkvmergeProcess.Kill();
                        Debug.WriteLine("... mkvmerge killed");
                    }
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e);
                }
                finally
                {
                    MkvmergeProcess.Close();
                    MkvmergeProcess.Dispose();
                    MkvmergeProcess = null!;
                }
            }

            MkvmergeWasAborted = false;
            MkvmergeProcess = new Process();
            MkvmergeProcess.StartInfo.FileName = "mkvmerge";
            MkvmergeProcess.StartInfo.Arguments = args;
            MkvmergeProcess.StartInfo.WorkingDirectory = workingDir;

            // Options
            MkvmergeProcess.StartInfo.CreateNoWindow = true;
            MkvmergeProcess.StartInfo.UseShellExecute = false;
            MkvmergeProcess.StartInfo.RedirectStandardInput = true;
            MkvmergeProcess.StartInfo.RedirectStandardOutput = true;
            MkvmergeProcess.StartInfo.RedirectStandardError = true;
            //MkvmergeProcess.EnableRaisingEvents = true;
            //MkvmergeProcess.Exited += delegate {/* clean up*/};

            // Receive StdOut and StdErr
            _probeJson.Clear();
            MkvmergeProcess.OutputDataReceived += outputHandler;
            MkvmergeProcess.ErrorDataReceived += outputHandler;

            // Start process
            try
            {
                MkvmergeProcess.Start();
                MkvmergeProcess.BeginOutputReadLine();
                MkvmergeProcess.BeginErrorReadLine();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                MkvmergeProcess.Close();
                MkvmergeProcess.Dispose();
                MkvmergeProcess = null!;
            }
        }

        public bool ParseMkvmergeJson(MediaData? md)
        {
            if (md == null) return false;
            md.Clear();

            try
            {
                JObject json = JObject.Parse(_probeJson.ToString());

                if (json["tracks"] == null) return false;

                // Get length of file
                string? duration = string.Empty;
                if (json["container"]?["properties"]?["duration"] != null)
                    duration = json["container"]?["properties"]?["duration"]?.ToString();
                // ReSharper disable once PossibleLossOfFraction
                if (!string.IsNullOrEmpty(duration)) md.Duration = TimeSpan.FromSeconds(Convert.ToInt32(duration?.Substring(0, duration.Length - 6)) / 1000); // ns -> ms -> s

                bool audioTrackSelected = false;

                foreach (JToken stream in json["tracks"]!)
                {
                    CheckBoxMedia cb = new CheckBoxMedia
                    {
                        IsSelected = md.IsMainMedia
                    };
                    string? codecName = stream["codec"]?.ToString();
                    if (stream["properties"] != null)
                    {
                        if (stream["properties"]?["language"] != null) cb.LanguageId = stream["properties"]?["language"]?.ToString();
                    }
                    else
                    {
                        cb.LanguageId = "unknown";
                    }
                    if (int.TryParse(stream["id"]?.ToString(), out int index))
                    {
                        cb.Index = index;
                    }
                    cb.CodecType = stream["type"]?.ToString();

                    string streamInfo = $"idx: {cb.Index}; type: {cb.CodecType}; codec: {codecName}; language: {cb.LanguageId};";
                    cb.Description = streamInfo;
                    Debug.WriteLine(streamInfo);

                    // Color code
                    switch (cb.CodecType)
                    {
                        case "audio":
                            cb.TypeBrush = new SolidColorBrush(Colors.LimeGreen);
                            ComboBoxItem co = new ComboBoxItem
                            {
                                Content = cb.Index.ToString(),
                                IsSelected = !audioTrackSelected
                            };
                            md.ComboBoxItems.Add(co);
                            audioTrackSelected = true;
                            break;
                        case "subtitles":
                            cb.TypeBrush = new SolidColorBrush(Colors.Yellow);
                            break;
                        case "video":
                            cb.TypeBrush = new SolidColorBrush(Colors.DodgerBlue);
                            break;
                    }

                    md.ListBoxItems.Add(cb);
                }
                try
                {
                    if (json["chapters"] != null)
                    {
                        foreach (JToken stream in json["chapters"]!)
                        {
                            if (stream["num_entries"] == null || stream["num_entries"]!.ToObject<int>() <= 0) continue;
                            CheckBoxMedia cb = new CheckBoxMedia
                            {
                                IsSelected = md.IsMainMedia,
                                CodecType = "chapters",
                                Index = 0,
                                Description = $"idx: n/a; type: chapters; entries: {stream["num_entries"]}",
                                TypeBrush = new SolidColorBrush(Colors.Silver)
                            };
                            md.ListBoxItems.Add(cb);
                        }

                    }

                    if (json["attachments"] != null)
                    {
                        foreach (JToken stream in json["attachments"]!)
                        {
                            if (stream["file_name"] == null || stream["id"] == null) continue;
                            CheckBoxMedia cb = new CheckBoxMedia
                            {
                                IsSelected = md.IsMainMedia,
                                CodecType = "attachments",
                                Description = $"idx: {stream["id"]}; type: attachments; file name: {stream["file_name"]}",
                                TypeBrush = new SolidColorBrush(Colors.DeepPink)
                            };
                            if (int.TryParse(stream["id"]?.ToString(), out int index))
                            {
                                cb.Index = index;
                            }
                            md.ListBoxItems.Add(cb);
                        }

                    }
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e);
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                return false;
            }

            return true;
        }

        #endregion

    }
}