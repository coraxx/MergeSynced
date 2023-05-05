using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Avalonia.Styling;
using Newtonsoft.Json;

namespace MergeSynced.Utilities
{
    public class SettingsManager
    {
        #region Fields

        public static string FilePath = string.Empty;
        public static UserData UserSettings = new UserData();
        public static ApplicationData ApplicationSettings = new ApplicationData();
        public static event EventHandler? SettingsLoaded;

        #endregion

        public static bool Save()
        {
            if (string.IsNullOrEmpty(FilePath))
            {
                Trace.WriteLine($"Error saving settings: filepath={FilePath}");
                return false;
            }

            SettingsData settingsData = new() { UserSettings = UserSettings, ApplicationSettings = ApplicationSettings};
            string json = JsonConvert.SerializeObject(settingsData, Formatting.Indented);
            File.WriteAllText(FilePath, json);
            return true;
        }

        public static bool Load()
        {
            if (string.IsNullOrEmpty(FilePath))
            {
                Trace.WriteLine($"Error loading settings: filepath={FilePath}");
                return false;
            }

            if (!Path.Exists(FilePath))
            {
                Trace.WriteLine($"Error loading settings: file {FilePath} does not exist");
                return false;
            }

            string json = File.ReadAllText(FilePath);
            SettingsData settingsData = new() { UserSettings = UserSettings, ApplicationSettings = ApplicationSettings };
            try
            {
                settingsData = JsonConvert.DeserializeObject<SettingsData>(json);
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex);
            }
            UserSettings = settingsData.UserSettings;
            // Reset application settings on new version
            if (settingsData.ApplicationSettings.Version == ApplicationSettings.Version)
                ApplicationSettings = settingsData.ApplicationSettings;
            OnSettingsLoaded();
            return true;
        }

        protected static void OnSettingsLoaded()
        {
            SettingsLoaded?.Invoke(null, EventArgs.Empty);
        }
    }

    public class UserData
    {
        public bool NormalizeAudio = true;
        public bool UseMkvmerge = false;
        public ThemeVariant SelectedTheme = ThemeVariant.Default;
        public bool WriteLog = true;
        public bool ShowNotifications = true;
    }

    public class ApplicationData
    {
        public string Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "UNKNOWN";
        public int NoOfSampleToDraw = 100000;
    }

    public struct SettingsData
    {
        public UserData UserSettings;
        public ApplicationData ApplicationSettings;
    }
}
