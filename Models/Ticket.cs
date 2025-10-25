using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;

namespace ERPNextFingerprintApp.Models
{
    public class Ticket : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _employee = string.Empty;
        private string _employeeName = string.Empty;
        private decimal _amount;
        private string _ticketType = string.Empty;
        private string _status = string.Empty;

        [JsonProperty("name")]
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        [JsonProperty("employee")]
        public string Employee
        {
            get => _employee;
            set => SetProperty(ref _employee, value);
        }

        [JsonProperty("employee_name")]
        public string EmployeeName
        {
            get => _employeeName;
            set => SetProperty(ref _employeeName, value);
        }

        [JsonProperty("amount")]
        public decimal Amount
        {
            get => _amount;
            set => SetProperty(ref _amount, value);
        }

        [JsonProperty("ticket_type")]
        public string TicketType
        {
            get => _ticketType;
            set => SetProperty(ref _ticketType, value);
        }

        [JsonProperty("status")]
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        // Computed properties for UI display
        public string FormattedAmount => $"₦{Amount:N2}";
        
        public bool IsUnused => Status?.Equals("Unused", StringComparison.OrdinalIgnoreCase) == true;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            
            // Notify computed properties when relevant properties change
            if (propertyName == nameof(Amount))
                OnPropertyChanged(nameof(FormattedAmount));
            if (propertyName == nameof(Status))
                OnPropertyChanged(nameof(IsUnused));
                
            return true;
        }
    }
}