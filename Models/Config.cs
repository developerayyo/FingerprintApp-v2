using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace ERPNextFingerprintApp.Models
{
    public class Config : INotifyPropertyChanged
    {
        private string _erpUrl = string.Empty;
        private string _apiKey = string.Empty;
        private string _apiSecret = string.Empty;
        private string _logPath = "C:\\Logs\\FingerprintApp.log";
        private bool _fingerprintCacheEnabled = true;
        private bool _autoSaveToERPNext = true;
        private int _connectionTimeout = 30;
        private int _maxRetryAttempts = 3;

        [JsonProperty("erp_url")]
        public string ErpUrl
        {
            get => _erpUrl;
            set => SetProperty(ref _erpUrl, value);
        }

        [JsonProperty("api_key")]
        public string ApiKey
        {
            get => _apiKey;
            set => SetProperty(ref _apiKey, value);
        }

        [JsonProperty("api_secret")]
        public string ApiSecret
        {
            get => _apiSecret;
            set => SetProperty(ref _apiSecret, value);
        }

        [JsonProperty("log_path")]
        public string LogPath
        {
            get => _logPath;
            set => SetProperty(ref _logPath, value);
        }

        [JsonProperty("fingerprint_cache_enabled")]
        public bool FingerprintCacheEnabled
        {
            get => _fingerprintCacheEnabled;
            set => SetProperty(ref _fingerprintCacheEnabled, value);
        }

        [JsonProperty("auto_save_to_erpnext")]
        public bool AutoSaveToERPNext
        {
            get => _autoSaveToERPNext;
            set => SetProperty(ref _autoSaveToERPNext, value);
        }

        [JsonProperty("connection_timeout")]
        public int ConnectionTimeout
        {
            get => _connectionTimeout;
            set => SetProperty(ref _connectionTimeout, value);
        }

        [JsonProperty("max_retry_attempts")]
        public int MaxRetryAttempts
        {
            get => _maxRetryAttempts;
            set => SetProperty(ref _maxRetryAttempts, value);
        }

        public string AuthorizationHeader => $"token {ApiKey}:{ApiSecret}";

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}