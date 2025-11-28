using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace MSP_ES
{
    class Config
    {
        private const string FolderPath = @"C:\CASTLESTECH_EMAILER\SETTINGS";
        private const string AttachFolderPath = @"C:\CASTLESTECH_EMAILER\ATTACH";
        private const string FileName = "Settings.ini";
        private readonly string _fullPath;

        private Dictionary<string, string> _settings;

        public Config()
        {
            CreateFolder();

            _fullPath = Path.Combine(FolderPath, FileName);

            Load();
        }

        private void CreateFolder()
        {
            //SETTINGS
            if (!Directory.Exists(FolderPath))
            {
                try
                {
                    Directory.CreateDirectory(FolderPath);
                    Console.WriteLine($"Created folder: {FolderPath}");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Cannot create folder {FolderPath}. Run as Administrator or change path. Error: {ex.Message}");
                }
            }

            //ATTACHMENTS
            if (!Directory.Exists(AttachFolderPath))
            {
                try
                {
                    Directory.CreateDirectory(AttachFolderPath);
                    Console.WriteLine($"Created folder: {AttachFolderPath}");
                }
                catch (Exception ex)
                {
                    throw new Exception($"Cannot create folder {AttachFolderPath}. Run as Administrator or change path. Error: {ex.Message}");
                }
            }
        }

        private void Load()
        {
            _settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!File.Exists(_fullPath))
            {
                CreateDefaultIni();
                return;
            }

            foreach (string line in File.ReadAllLines(_fullPath, Encoding.UTF8))
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith(";") || trimmed.StartsWith("#"))
                    continue;

                int eqIndex = trimmed.IndexOf('=');
                if (eqIndex > 0)
                {
                    string key = trimmed.Substring(0, eqIndex).Trim();
                    string value = trimmed.Substring(eqIndex + 1).Trim();
                    _settings[key] = value;
                }
            }
        }

        private void CreateDefaultIni()
        {
            var defaultContent = new[]
            {
                "; =============================================",
                "; MSP Email Scheduler - Configuration File",
                "; Location: C:\\CASTLESTECH_MSP\\SETTINGS\\settings.ini",
                "; =============================================",
                "",
                "Server=",
                "UserId=",
                "Password=",
                "Database=",
                "AttachmentFolder=",
                "SmtpUser=",
                "SmtpPass=",
                "Procedure=",
                "SmtpHost=",
                "SmtpPort=",
                "EnableSsl="
            };

            try
            {
                File.WriteAllLines(_fullPath, defaultContent, Encoding.UTF8);
                Debug.WriteLine($"Created config file: {_fullPath}");
                Debug.WriteLine("Please edit the file with your real settings and restart the app.");
            }
            catch (Exception ex)
            {
                throw new Exception($"Cannot create config file: {_fullPath}\n{ex.Message}\nRun as Administrator once.");
            }
        }

        public string Get(string key, string defaultValue = "")
            => _settings.TryGetValue(key, out string value) ? value : defaultValue;

        public void Set(string key, string value) => _settings[key] = value;

        public void Save()
        {
            var lines = new List<string>
            {
            "; =============================================",
            "; MSP Email Scheduler - Configuration File",
            $" Last saved: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            "; Location: C:\\CASTLESTECH_MSP\\SETTINGS\\Settings.ini",
            "; =============================================",
            ""
            };

            foreach (var kvp in _settings)
                lines.Add($"{kvp.Key}={kvp.Value}");

            File.WriteAllLines(_fullPath, lines, Encoding.UTF8);
        }

        public string ConnString => $"server={Get("Server")};user id={Get("UserId")};password={Get("Password")};database={Get("Database")};";
        public string AttachmentFolder => Get("AttachmentFolder");
        public string SmtpUser => Get("SmtpUser");
        public string SmtpPass => Get("SmtpPass");
        public string Procedure => Get("Procedure");
        public string SmtpHost => Get("SmtpHost");
        public int SmtpPort => int.TryParse(Get("SmtpPort"), out int p) ? p : 587;
        public bool EnableSsl => bool.TryParse(Get("EnableSsl"), out bool b) ? b : true;
        public string ConfigPath => _fullPath;
        public string DefaultPath => FolderPath;

    }
}
