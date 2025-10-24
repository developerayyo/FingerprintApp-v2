using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace ERPNextFingerprintApp.Models
{
    public class Employee : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _employeeName = string.Empty;
        private string _department = string.Empty;
        private string _designation = string.Empty;
        private string _fingerprintTemplate = string.Empty;
        private bool _isActive = true;

        [JsonProperty("name")]
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        [JsonProperty("employee_name")]
        public string EmployeeName
        {
            get => _employeeName;
            set => SetProperty(ref _employeeName, value);
        }

        [JsonProperty("department")]
        public string Department
        {
            get => _department;
            set => SetProperty(ref _department, value);
        }

        [JsonProperty("designation")]
        public string Designation
        {
            get => _designation;
            set => SetProperty(ref _designation, value);
        }

        [JsonProperty("custom_fingerprint_template")]
        public string FingerprintTemplate
        {
            get => _fingerprintTemplate;
            set => SetProperty(ref _fingerprintTemplate, value);
        }

        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }

        public string DisplayName => $"{Name} - {EmployeeName}";

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