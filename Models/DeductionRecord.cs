using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ERPNextFingerprintApp.Models
{
    public class DeductionRecord : INotifyPropertyChanged
    {
        private string _employee = string.Empty;
        private string _employeeName = string.Empty;
        private DeductionType _deductionType = DeductionType.Canteen;
        private decimal _amount;
        private DateTime _timestamp = DateTime.Now;
        private string _description = string.Empty;
        private DeductionStatus _status = DeductionStatus.Pending;
        private string _transactionId = string.Empty;

        public string Employee
        {
            get => _employee;
            set => SetProperty(ref _employee, value);
        }

        public string EmployeeName
        {
            get => _employeeName;
            set => SetProperty(ref _employeeName, value);
        }

        public DeductionType DeductionType
        {
            get => _deductionType;
            set => SetProperty(ref _deductionType, value);
        }

        public decimal Amount
        {
            get => _amount;
            set => SetProperty(ref _amount, value);
        }

        public DateTime Timestamp
        {
            get => _timestamp;
            set => SetProperty(ref _timestamp, value);
        }

        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        public DeductionStatus Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        public string TransactionId
        {
            get => _transactionId;
            set => SetProperty(ref _transactionId, value);
        }

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

    public enum DeductionType
    {
        Canteen,
        Minimart
    }

    public enum DeductionStatus
    {
        Pending,
        Processing,
        Completed,
        Failed
    }
}