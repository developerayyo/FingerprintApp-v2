using System;
using System.IO;
using Newtonsoft.Json;
using ERPNextFingerprintApp.Models;
using Serilog;

namespace ERPNextFingerprintApp.Utils
{
    public static class JsonHelper
    {
        private static readonly JsonSerializerSettings Settings = new()
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
            DateFormatHandling = DateFormatHandling.IsoDateFormat
        };

        public static Config LoadConfig(string configPath)
        {
            try
            {
                if (!File.Exists(configPath))
                {
                    Log.Warning("Config file not found at {ConfigPath}. Creating default config.", configPath);
                    var defaultConfig = new Config();
                    SaveConfig(defaultConfig, configPath);
                    return defaultConfig;
                }

                var json = File.ReadAllText(configPath);
                var config = JsonConvert.DeserializeObject<Config>(json) ?? new Config();
                
                Log.Information("Configuration loaded successfully from {ConfigPath}", configPath);
                return config;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load configuration from {ConfigPath}", configPath);
                return new Config();
            }
        }

        public static void SaveConfig(Config config, string configPath)
        {
            try
            {
                var directory = Path.GetDirectoryName(configPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonConvert.SerializeObject(config, Settings);
                File.WriteAllText(configPath, json);
                
                Log.Information("Configuration saved successfully to {ConfigPath}", configPath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save configuration to {ConfigPath}", configPath);
            }
        }

        public static T? DeserializeObject<T>(string json) where T : class
        {
            try
            {
                return JsonConvert.DeserializeObject<T>(json, Settings);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to deserialize JSON to {Type}", typeof(T).Name);
                return null;
            }
        }

        public static string SerializeObject<T>(T obj) where T : class
        {
            try
            {
                return JsonConvert.SerializeObject(obj, Settings);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to serialize {Type} to JSON", typeof(T).Name);
                return string.Empty;
            }
        }
    }
}