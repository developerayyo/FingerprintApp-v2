using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using ERPNextFingerprintApp.Models;
using ERPNextFingerprintApp.Services;

namespace ERPNextFingerprintApp.ViewModels
{
    public partial class RegistrationViewModel : ObservableObject
    {
        private readonly ERPNextApiService _apiService;
        private readonly FingerprintService _fingerprintService;
        private readonly Config _config;

        [ObservableProperty]
        private ObservableCollection<Employee> _employees = new();

        [ObservableProperty]
        private Employee? _selectedEmployee;

        [ObservableProperty]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        private bool _isLoading = false;

        [ObservableProperty]
        private bool _isCaptureEnabled = false;

        [ObservableProperty]
        private bool _isSaveEnabled = false;

        [ObservableProperty]
        private string _capturedTemplate = string.Empty;

        [ObservableProperty]
        private bool _isEnrollmentInProgress = false;

        [ObservableProperty]
        private string _enrollmentProgress = string.Empty;

        [ObservableProperty]
        private int _currentScan = 0;

        [ObservableProperty]
        private int _totalScans = 4;

        [ObservableProperty]
        private int _qualityPercentage = 0;

        public ICommand LoadEmployeesCommand { get; }
        public ICommand CaptureFingerprintCommand { get; }
        public ICommand EnrollFingerprintCommand { get; }
        public ICommand SaveToERPNextCommand { get; }

        public RegistrationViewModel(ERPNextApiService apiService, FingerprintService fingerprintService, Config config)
        {
            _apiService = apiService ?? throw new ArgumentNullException(nameof(apiService));
            _fingerprintService = fingerprintService ?? throw new ArgumentNullException(nameof(fingerprintService));
            _config = config ?? throw new ArgumentNullException(nameof(config));

            LoadEmployeesCommand = new AsyncRelayCommand(LoadEmployeesAsync);
            CaptureFingerprintCommand = new AsyncRelayCommand(CaptureFingerprintAsync, () => IsCaptureEnabled);
            EnrollFingerprintCommand = new AsyncRelayCommand(EnrollFingerprintAsync, () => IsCaptureEnabled && !IsEnrollmentInProgress);
            SaveToERPNextCommand = new AsyncRelayCommand(SaveToERPNextAsync, () => IsSaveEnabled);

            // Subscribe to fingerprint service events
            _fingerprintService.FingerprintCaptured += OnFingerprintCaptured;
            _fingerprintService.ErrorOccurred += OnFingerprintError;
            _fingerprintService.EnrollmentProgressChanged += OnEnrollmentProgressChanged;

            // Initialize
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Initializing fingerprint service...";

                var initialized = await _fingerprintService.InitializeAsync();
                if (initialized)
                {
                    IsCaptureEnabled = true;
                    StatusMessage = "Fingerprint service ready";
                    await LoadEmployeesAsync();
                }
                else
                {
                    IsCaptureEnabled = false;
                    // Get detailed status for better error reporting
                    var detailedStatus = await _fingerprintService.GetDetailedStatusAsync();
                    Log.Warning($"Failed to initialize fingerprint service. Status: {detailedStatus}");
                    StatusMessage = "Failed to initialize fingerprint service. Check device connection and SDK installation.";
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to initialize registration view model");
                IsCaptureEnabled = false;
                StatusMessage = $"Initialization error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadEmployeesAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Loading employees from ERPNext...";

                var result = await _apiService.GetEmployeesAsync();
                if (result.IsSuccess && result.Data != null)
                {
                    Employees.Clear();
                    foreach (var employee in result.Data)
                    {
                        Employees.Add(employee);
                    }

                    StatusMessage = $"Loaded {Employees.Count} employees";
                    Log.Information("Loaded {Count} employees for registration", Employees.Count);
                }
                else
                {
                    StatusMessage = $"Failed to load employees: {result.ErrorMessage}";
                    Log.Error("Failed to load employees: {ErrorMessage}", result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error loading employees");
                StatusMessage = $"Error loading employees: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task CaptureFingerprintAsync()
        {
            if (SelectedEmployee == null)
            {
                StatusMessage = "Please select an employee first";
                return;
            }

            try
            {
                IsLoading = true;
                IsCaptureEnabled = false;
                StatusMessage = "Place finger on scanner...";

                var result = await _fingerprintService.CaptureAsync();
                if (result.IsSuccess)
                {
                    CapturedTemplate = result.Template;
                    IsSaveEnabled = true;
                    StatusMessage = "Fingerprint captured successfully";
                    Log.Information("Fingerprint captured for employee {EmployeeId}", SelectedEmployee.Name);
                    
                    // Auto-save to ERPNext if enabled
                    if (_config.AutoSaveToERPNext)
                    {
                        Log.Information("Auto-save enabled, automatically saving to ERPNext for employee {EmployeeId}", SelectedEmployee.Name);
                        await SaveToERPNextAsync();
                    }
                }
                else
                {
                    StatusMessage = $"Capture failed: {result.ErrorMessage}";
                    Log.Warning("Fingerprint capture failed for employee {EmployeeId}: {Error}", 
                        SelectedEmployee.Name, result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during fingerprint capture");
                StatusMessage = $"Capture error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
                IsCaptureEnabled = true;
                
                // Reset service state to prevent hanging on subsequent operations
                try
                {
                    await _fingerprintService.ResetServiceStateAsync();
                }
                catch (Exception resetEx)
                {
                    Log.Warning(resetEx, "Error resetting fingerprint service state after capture");
                }
            }
        }

        private async Task SaveToERPNextAsync()
        {
            if (SelectedEmployee == null || string.IsNullOrEmpty(CapturedTemplate))
            {
                StatusMessage = "No fingerprint data to save";
                return;
            }

            try
            {
                IsLoading = true;
                IsSaveEnabled = false;
                StatusMessage = "Saving fingerprint to ERPNext...";

                var result = await _apiService.UpdateEmployeeFingerprintAsync(SelectedEmployee.Name, CapturedTemplate);
                if (result.IsSuccess)
                {
                    SelectedEmployee.FingerprintTemplate = CapturedTemplate;
                    _fingerprintService.CacheFingerprint(SelectedEmployee.Name, CapturedTemplate);
                    
                    StatusMessage = "Fingerprint saved successfully";
                    Log.Information("Fingerprint saved for employee {EmployeeId}", SelectedEmployee.Name);
                    
                    // Reset for next registration
                    CapturedTemplate = string.Empty;
                    SelectedEmployee = null;
                }
                else
                {
                    StatusMessage = $"Save failed: {result.ErrorMessage}";
                    Log.Error("Failed to save fingerprint for employee {EmployeeId}: {Error}", 
                        SelectedEmployee.Name, result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error saving fingerprint to ERPNext");
                StatusMessage = $"Save error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
                IsSaveEnabled = !string.IsNullOrEmpty(CapturedTemplate);
            }
        }

        private void OnFingerprintCaptured(object? sender, FingerprintCapturedEventArgs e)
        {
            // This event is already handled in CaptureFingerprintAsync
            // But could be used for additional UI updates if needed
        }

        private void OnFingerprintError(object? sender, string errorMessage)
        {
            StatusMessage = $"Fingerprint error: {errorMessage}";
            IsLoading = false;
            IsCaptureEnabled = true;
        }

        private async Task EnrollFingerprintAsync()
        {
            if (SelectedEmployee == null)
            {
                StatusMessage = "Please select an employee first";
                return;
            }

            try
            {
                IsEnrollmentInProgress = true;
                IsCaptureEnabled = false;
                IsSaveEnabled = false;
                CurrentScan = 0;
                QualityPercentage = 0;
                EnrollmentProgress = "Starting enrollment...";
                StatusMessage = "Multi-scan enrollment starting...";

                var result = await _fingerprintService.EnrollFingerprintWithControlAsync();
                
                if (result.IsSuccess)
                {
                    CapturedTemplate = result.Template;
                    IsSaveEnabled = true;
                    StatusMessage = $"Enrollment completed successfully! Quality: {result.QualityPercentage}%";
                    EnrollmentProgress = $"Enrollment complete - {result.ScansCompleted} scans processed";
                    Log.Information("Multi-scan enrollment completed for employee {EmployeeId} with {ScansCompleted} scans", 
                        SelectedEmployee.Name, result.ScansCompleted);
                    
                    // Auto-save to ERPNext if enabled
                    if (_config.AutoSaveToERPNext)
                    {
                        Log.Information("Auto-save enabled, automatically saving to ERPNext for employee {EmployeeId}", SelectedEmployee.Name);
                        await SaveToERPNextAsync();
                    }
                }
                else
                {
                    StatusMessage = $"Enrollment failed: {result.ErrorMessage}";
                    EnrollmentProgress = "Enrollment failed";
                    Log.Warning("Multi-scan enrollment failed for employee {EmployeeId}: {Error}", 
                        SelectedEmployee.Name, result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during multi-scan enrollment");
                StatusMessage = $"Enrollment error: {ex.Message}";
                EnrollmentProgress = "Enrollment error occurred";
            }
            finally
            {
                IsEnrollmentInProgress = false;
                IsCaptureEnabled = true;
                
                // Reset service state to prevent hanging on subsequent operations
                try
                {
                    await _fingerprintService.ResetServiceStateAsync();
                }
                catch (Exception resetEx)
                {
                    Log.Warning(resetEx, "Error resetting fingerprint service state after enrollment");
                }
            }
        }

        private void OnEnrollmentProgressChanged(object? sender, EnrollmentProgress e)
        {
            CurrentScan = e.CurrentScan;
            QualityPercentage = e.QualityPercentage;
            EnrollmentProgress = e.Message;
            
            // Update status message with current progress
            StatusMessage = $"Scan {e.CurrentScan}/{TotalScans} - {e.Message} (Quality: {e.QualityPercentage}%)";
        }

        partial void OnSelectedEmployeeChanged(Employee? value)
        {
            // Reset capture state when employee changes
            CapturedTemplate = string.Empty;
            IsSaveEnabled = false;
            
            if (value != null)
            {
                StatusMessage = $"Selected: {value.DisplayName}";
            }
        }
    }
}