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
    public partial class VerificationViewModel : ObservableObject
    {
        private readonly ERPNextApiService _apiService;
        private readonly FingerprintService _fingerprintService;
        private List<Employee> _employees = new();

        [ObservableProperty]
        private Employee? _verifiedEmployee;

        [ObservableProperty]
        private DeductionType _selectedDeductionType = DeductionType.Canteen;

        [ObservableProperty]
        private decimal _deductionAmount = 0;

        [ObservableProperty]
        private string _statusMessage = "Ready";

        [ObservableProperty]
        private bool _isLoading = false;

        [ObservableProperty]
        private bool _isScanEnabled = false;

        [ObservableProperty]
        private bool _isProcessEnabled = false;

        [ObservableProperty]
        private ObservableCollection<DeductionRecord> _recentDeductions = new();

        public Array DeductionTypes => Enum.GetValues(typeof(DeductionType));

        public ICommand LoadEmployeesCommand { get; }
        public ICommand ScanFingerprintCommand { get; }
        public ICommand ProcessDeductionCommand { get; }
        public ICommand RefreshEmployeesCommand { get; }

        public VerificationViewModel(ERPNextApiService apiService, FingerprintService fingerprintService, Config config)
        {
            _apiService = apiService ?? throw new ArgumentNullException(nameof(apiService));
            _fingerprintService = fingerprintService ?? throw new ArgumentNullException(nameof(fingerprintService));

            LoadEmployeesCommand = new AsyncRelayCommand(LoadEmployeesAsync);
            ScanFingerprintCommand = new AsyncRelayCommand(ScanFingerprintAsync, () => IsScanEnabled);
            ProcessDeductionCommand = new AsyncRelayCommand(ProcessDeductionAsync, () => IsProcessEnabled);
            RefreshEmployeesCommand = new AsyncRelayCommand(RefreshEmployeesAsync);

            // Subscribe to fingerprint service events
            _fingerprintService.FingerprintVerified += OnFingerprintVerified;
            _fingerprintService.ErrorOccurred += OnFingerprintError;

            // Initialize
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Initializing verification service...";

                var initialized = await _fingerprintService.InitializeAsync();
                if (initialized)
                {
                    IsScanEnabled = true;
                    StatusMessage = "Verification service ready";
                    await RefreshEmployeesAsync();
                }
                else
                {
                    IsScanEnabled = false;
                    // Get detailed status for better error reporting
                    var detailedStatus = await _fingerprintService.GetDetailedStatusAsync();
                    Log.Warning($"Failed to initialize verification service. Status: {detailedStatus}");
                    StatusMessage = "Failed to initialize verification service. Check device connection and SDK installation.";
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to initialize verification view model");
                IsScanEnabled = false;
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
                StatusMessage = "Loading employee data...";

                var result = await _apiService.GetEmployeesAsync();
                if (result.IsSuccess && result.Data != null)
                {
                    _employees = result.Data;
                    StatusMessage = $"Loaded {_employees.Count} employees for verification";
                    Log.Information("Loaded {Count} employees for verification", _employees.Count);
                }
                else
                {
                    StatusMessage = $"Failed to load employees: {result.ErrorMessage}";
                    Log.Error("Failed to load employees for verification: {ErrorMessage}", result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading employees: {ex.Message}";
                Log.Error(ex, "Error loading employees for verification");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task RefreshEmployeesAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Loading employee data...";

                var result = await _apiService.GetEmployeesAsync();
                if (result.IsSuccess && result.Data != null)
                {
                    _employees = result.Data;
                    StatusMessage = $"Loaded {_employees.Count} employees for verification";
                    Log.Information("Loaded {Count} employees for verification", _employees.Count);
                }
                else
                {
                    StatusMessage = $"Failed to load employees: {result.ErrorMessage}";
                    Log.Error("Failed to load employees for verification: {ErrorMessage}", result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error refreshing employees");
                StatusMessage = $"Error loading employees: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task ScanFingerprintAsync()
        {
            if (DeductionAmount <= 0)
            {
                StatusMessage = "Please enter a valid deduction amount first";
                return;
            }

            try
            {
                IsLoading = true;
                IsScanEnabled = false;
                VerifiedEmployee = null;
                IsProcessEnabled = false;
                StatusMessage = "Place finger on scanner for verification...";

                // Use the same verification method as Tickets (which works reliably)
                StatusMessage = "Verifying fingerprint and processing deduction...";
                var verificationResult = await _fingerprintService.VerifyAsync(_employees);

                if (verificationResult.IsSuccess && verificationResult.MatchedEmployee != null)
                {
                    var employee = verificationResult.MatchedEmployee;
                    VerifiedEmployee = employee;
                    
                    StatusMessage = $"Fingerprint verified for {employee.EmployeeName}. Creating deduction...";
                    Log.Information("Fingerprint verified for employee: {EmployeeId} - {EmployeeName}", 
                        employee.Name, employee.EmployeeName);
                    
                    // Create deduction in ERPNext
                    var deductionResult = await _apiService.CreateDeductionAsync(
                        employee.Name,
                        SelectedDeductionType.ToString(),
                        DeductionAmount,
                        $"{SelectedDeductionType} deduction via fingerprint verification");
                    
                    if (deductionResult.IsSuccess)
                    {
                        // Create deduction record for display
                        var deduction = new DeductionRecord
                        {
                            Employee = employee.Name,
                            EmployeeName = employee.EmployeeName,
                            DeductionType = SelectedDeductionType,
                            Amount = DeductionAmount,
                            Description = $"{SelectedDeductionType} deduction via fingerprint verification",
                            Timestamp = DateTime.Now,
                            Status = DeductionStatus.Completed,
                            TransactionId = deductionResult.Data ?? Guid.NewGuid().ToString("N")[..8].ToUpper()
                        };

                        RecentDeductions.Insert(0, deduction);
                        
                        // Keep only last 10 deductions
                        while (RecentDeductions.Count > 10)
                        {
                            RecentDeductions.RemoveAt(RecentDeductions.Count - 1);
                        }

                        StatusMessage = $"Success: {employee.EmployeeName} - Deduction processed (ID: {deductionResult.Data})";
                        Log.Information("Employee verified and deduction created: {EmployeeId} - {EmployeeName} - Amount: {Amount}", 
                            employee.Name, employee.EmployeeName, DeductionAmount);
                    }
                    else
                    {
                        StatusMessage = $"Fingerprint verified but deduction failed: {deductionResult.ErrorMessage}";
                        Log.Error("Failed to create deduction for verified employee {EmployeeId}: {Error}", 
                            employee.Name, deductionResult.ErrorMessage);
                    }

                    // Reset for next transaction after showing success message
                    await Task.Delay(1000);
                    ResetVerification();
                }
                else
                {
                    var errorMessage = verificationResult.ErrorMessage ?? "Verification failed";
                    StatusMessage = $"Verification failed: {errorMessage}";
                    Log.Warning("Fingerprint verification failed: {Error}", errorMessage);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during fingerprint verification");
                StatusMessage = $"Verification error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
                IsScanEnabled = true;
            }
        }

        private async Task ProcessDeductionAsync()
        {
            // This method is no longer needed as deduction processing is now handled
            // directly in ScanFingerprintAsync method via ERPNext verification endpoint
            StatusMessage = "Deduction processing is now integrated with fingerprint verification";
            await Task.Delay(1000);
            StatusMessage = "Ready for fingerprint verification";
        }

        private void ResetVerification()
        {
            VerifiedEmployee = null;
            DeductionAmount = 0;
            IsProcessEnabled = false;
            StatusMessage = "Ready for next verification";
        }

        private void OnFingerprintVerified(object? sender, FingerprintVerifiedEventArgs e)
        {
            // This event is already handled in ScanFingerprintAsync
            // But could be used for additional UI updates if needed
        }

        private void OnFingerprintError(object? sender, string errorMessage)
        {
            StatusMessage = $"Fingerprint error: {errorMessage}";
            IsLoading = false;
            IsScanEnabled = true;
        }

        partial void OnDeductionAmountChanged(decimal value)
        {
            IsProcessEnabled = VerifiedEmployee != null && value > 0;
        }

        partial void OnSelectedDeductionTypeChanged(DeductionType value)
        {
            // Could set default amounts based on deduction type
            if (DeductionAmount == 0)
            {
                DeductionAmount = value switch
                {
                    DeductionType.Canteen => 500,
                    DeductionType.Minimart => 1000,
                    _ => 0
                };
            }
        }
    }
}