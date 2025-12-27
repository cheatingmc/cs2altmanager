using System;
using System.IO;

namespace CacheLoginToolWPF
{
    public static class AppDataHelper
    {
        private static string? _appDataPath;
        private const string AppDataFolderName = "cs2altmanager";
        private const string ConfigFolderName = "config";
        private const string DatabaseFolderName = "database";
        private const string LogsFolderName = "logs";
        private const string InitializedMarkerFile = ".initialized";

        public static string GetAppDataPath()
        {
            if (_appDataPath != null)
                return _appDataPath;

            try
            {
                var path = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    _appDataPath = Path.Combine(path, AppDataFolderName);
                    return _appDataPath;
                }
            }
            catch { }

            try
            {
                var path = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    _appDataPath = Path.Combine(path, AppDataFolderName);
                    return _appDataPath;
                }
            }
            catch { }

            _appDataPath = Path.Combine(Path.GetTempPath(), AppDataFolderName);
            return _appDataPath;
        }

        public static string GetConfigPath()
        {
            return Path.Combine(GetAppDataPath(), ConfigFolderName);
        }

        public static string GetDatabasePath()
        {
            return Path.Combine(GetAppDataPath(), DatabaseFolderName);
        }

        public static string GetLogsPath()
        {
            return Path.Combine(GetAppDataPath(), LogsFolderName);
        }

        public static bool InitializeAppData()
        {
            try
            {
                var appDataPath = GetAppDataPath();
                var markerFile = Path.Combine(appDataPath, InitializedMarkerFile);

                if (File.Exists(markerFile))
                {
                    return false; // Not first run
                }

                Directory.CreateDirectory(appDataPath);

                Directory.CreateDirectory(GetConfigPath());
                Directory.CreateDirectory(GetDatabasePath());
                Directory.CreateDirectory(GetLogsPath());

                var initInfo = $"Initialized: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n" +
                              $"Version: 1.0\n" +
                              $"AppData Path: {appDataPath}";
                File.WriteAllText(markerFile, initInfo);

                var readmePath = Path.Combine(appDataPath, "README.txt");
                var readmeContent = @"CS2 Alt Manager - AppData Folder Structure
==========================================

This folder contains all application data and configuration files.

Folder Structure:
-----------------
- config/          : Configuration files (API keys, settings, etc.)
- database/        : SQLite database files
- logs/            : Application logs (if enabled)

Files:
------
- .initialized     : Marker file indicating successful initialization
- README.txt       : This file

Important Notes:
---------------
- Do not delete this folder unless you want to reset all application data
- The database file contains your account information
- API keys are stored in the config folder
- All data is stored locally on your computer

For support or questions, please refer to the application documentation.
";
                File.WriteAllText(readmePath, readmeContent);

                return true; // First run
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing AppData: {ex.Message}");

                return false;
            }
        }

        public static bool IsInitialized()
        {
            try
            {
                var appDataPath = GetAppDataPath();
                var markerFile = Path.Combine(appDataPath, InitializedMarkerFile);
                return File.Exists(markerFile);
            }
            catch
            {
                return false;
            }
        }
    }
}

