using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace MergeSynced.Tools;

public class StringTraceListener : TraceListener, INotifyPropertyChanged
{
    private readonly StringBuilder _builder;
    private string _lastEntry;
    public StreamWriter? Logfile;

    public StringTraceListener(bool debug = false, string logpath = @"log.txt")
    {
        _builder = new StringBuilder();
        _lastEntry = "";

        if (debug)
        {
            Logfile = File.CreateText(logpath);
            Logfile.AutoFlush = true;
        }
    }

    public string Trace => _builder.ToString();
    public string TraceLastEntry => _lastEntry;

    public override void Write(string? message)
    {
        lock (_builder)
        {
            if (_builder.Length > 100000) _builder.Clear(); // Some simple cleanup
            _builder.Append(message);
            _lastEntry = message ?? "";
            Logfile?.Write(message);
            OnPropertyChanged(new PropertyChangedEventArgs("Trace"));
        }
    }

    public override void WriteLine(string? message)
    {
        lock (_builder)
        {
            if (_builder.Length > 100000) _builder.Clear(); // Some simple cleanup
            _lastEntry = $"[{DateTime.Now.ToString(CultureInfo.CurrentCulture)}] {message}";
            _builder.AppendLine(_lastEntry);
            if (Logfile != null && Logfile.BaseStream.CanWrite) Logfile?.WriteLine(_lastEntry);
            OnPropertyChanged(new PropertyChangedEventArgs("Trace"));
        }
    }

    #region INotifyPropertyChanged Members

    public event PropertyChangedEventHandler? PropertyChanged;

    #endregion

    protected virtual void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        if (PropertyChanged == null) return;
        PropertyChangedEventHandler handler = PropertyChanged;
        handler?.Invoke(this, e);
    }
}