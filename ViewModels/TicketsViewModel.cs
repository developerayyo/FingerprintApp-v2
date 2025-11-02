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
    public partial class TicketsViewModel : ObservableObject
    {
        private readonly ERPNextApiService _apiService;
        private readonly FingerprintService _fingerprintService;
        private List<Employee> _employees = new();
        private Employee? _currentEmployee;

        [ObservableProperty]
        private ObservableCollection<Ticket> _tickets = new();

        [ObservableProperty]
        private string _statusMessage = "Click 'Fetch Ticket' to verify your fingerprint and load available Ticket";

        [ObservableProperty]
        private bool _isLoading = false;

        [ObservableProperty]
        private bool _isFetchEnabled = true;

        [ObservableProperty]
        private bool _hasTickets = false;

        [ObservableProperty]
        private bool _hasMultipleTickets = false;

        [ObservableProperty]
        private string _totalAmount = "₦0.00";

        [ObservableProperty]
        private int _ticketCount = 0;

        public ICommand FetchTicketsCommand { get; }
        public ICommand UseTicketCommand { get; }
        public ICommand UseAllTicketsCommand { get; }
        public ICommand RefreshTicketsCommand { get; }

        public TicketsViewModel(ERPNextApiService apiService, FingerprintService fingerprintService)
        {
            _apiService = apiService ?? throw new ArgumentNullException(nameof(apiService));
            _fingerprintService = fingerprintService ?? throw new ArgumentNullException(nameof(fingerprintService));

            FetchTicketsCommand = new AsyncRelayCommand(FetchTicketsAsync, () => IsFetchEnabled);
            UseTicketCommand = new AsyncRelayCommand<Ticket>(UseTicketAsync, ticket => ticket != null && !IsLoading);
            UseAllTicketsCommand = new AsyncRelayCommand(UseAllTicketsAsync, () => HasMultipleTickets && !IsLoading);
            RefreshTicketsCommand = new AsyncRelayCommand(RefreshTicketsAsync, () => _currentEmployee != null && !IsLoading);

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
                StatusMessage = "Initializing Ticket service...";

                var initialized = await _fingerprintService.InitializeAsync();
                if (initialized)
                {
                    IsFetchEnabled = true;
                    StatusMessage = "Ticket service ready";
                    await LoadEmployeesAsync();
                }
                else
                {
                    IsFetchEnabled = false;
                    // Get detailed status for better error reporting
                    var detailedStatus = await _fingerprintService.GetDetailedStatusAsync();
                    Log.Warning($"Failed to initialize Ticket service. Status: {detailedStatus}");
                    StatusMessage = "Failed to initialize Ticket service. Check device connection and SDK installation.";
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error initializing TicketsViewModel");
                IsFetchEnabled = false;
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
                Log.Information("Loading employees for Ticket verification");
                var result = await _apiService.GetEmployeesAsync();
                
                if (result.IsSuccess && result.Data != null)
                {
                    _employees = result.Data;
                    Log.Information("Loaded {Count} employees for Ticket verification", _employees.Count);
                }
                else
                {
                    Log.Warning("Failed to load employees: {Error}", result.ErrorMessage);
                    StatusMessage = $"Failed to load employees: {result.ErrorMessage}";
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error loading employees for Ticket");
                StatusMessage = $"Error loading employees: {ex.Message}";
            }
        }

        private async Task FetchTicketsAsync()
        {
            try
            {
                IsLoading = true;
                IsFetchEnabled = false;
                StatusMessage = "Please verify your fingerprint to fetch Ticket...";

                // Clear previous data
                Tickets.Clear();
                _currentEmployee = null;
                UpdateTicketSummary();

                // Perform fingerprint verification
                var verificationResult = await _fingerprintService.VerifyAsync(_employees);

                if (verificationResult.IsSuccess && verificationResult.MatchedEmployee != null)
                {
                    _currentEmployee = verificationResult.MatchedEmployee;
                    StatusMessage = $"Fingerprint verified for {_currentEmployee.EmployeeName}. Fetching Ticket...";
                    
                    Log.Information("Fingerprint verified for employee: {EmployeeId} - {EmployeeName}", 
                        _currentEmployee.Name, _currentEmployee.EmployeeName);

                    // Fetch Ticket for the verified employee
                    var ticketsResult = await _apiService.GetUnusedTicketsAsync(_currentEmployee.Name);

                    if (ticketsResult.IsSuccess && ticketsResult.Data != null)
                    {
                        Tickets.Clear();
                        foreach (var ticket in ticketsResult.Data)
                        {
                            Tickets.Add(ticket);
                        }

                        UpdateTicketSummary();

                        if (Tickets.Any())
                        {
                            StatusMessage = $"Found {Tickets.Count} available ticket{(Tickets.Count == 1 ? "" : "s")} for {_currentEmployee.EmployeeName}";
                            Log.Information("Successfully fetched tickets for employee {EmployeeId}: {Count} tickets", 
                                Tickets.Count, _currentEmployee.Name);
                        }
                        else
                        {
                            StatusMessage = "No available Ticket found";
                            Log.Information("No unused Ticket found for employee {EmployeeId}", _currentEmployee.Name);
                        }
                    }
                    else
                    {
                        StatusMessage = $"Failed to fetch Ticket: {ticketsResult.ErrorMessage}";
                        Log.Error("Failed to fetch Ticket for employee {EmployeeId}: {Error}", 
                            _currentEmployee.Name, ticketsResult.ErrorMessage);
                    }
                }
                else
                {
                    // Check if this is a timeout error specifically
                    if (!string.IsNullOrEmpty(verificationResult.ErrorMessage) && 
                        verificationResult.ErrorMessage.Contains("timeout", StringComparison.OrdinalIgnoreCase))
                    {
                        StatusMessage = "Fingerprint capture timeout. Please try again.";
                        Log.Warning("Fingerprint capture timeout during Ticket fetch");
                    }
                    else
                    {
                        StatusMessage = "Fingerprint not recognized. Please try again.";
                        Log.Warning("Fingerprint verification failed for Ticket fetch: {Error}", 
                            verificationResult.ErrorMessage);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during ticket fetch process");
                StatusMessage = $"Error fetching Ticket: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
                IsFetchEnabled = true;
                
                // Reset fingerprint service state
                try
                {
                    await _fingerprintService.ResetServiceStateAsync();
                }
                catch (Exception resetEx)
                {
                    Log.Warning(resetEx, "Error resetting fingerprint service state after ticket fetch");
                }
            }
        }

        private async Task UseTicketAsync(Ticket? ticket)
        {
            if (ticket == null || _currentEmployee == null)
                return;

            try
            {
                IsLoading = true;
                StatusMessage = $"Please verify your fingerprint to use ticket {ticket.TicketType}...";

                // Perform fingerprint verification again for security
                var verificationResult = await _fingerprintService.VerifyAsync(_employees);

                if (verificationResult.IsSuccess && 
                    verificationResult.MatchedEmployee != null &&
                    verificationResult.MatchedEmployee.Name == _currentEmployee.Name)
                {
                    StatusMessage = $"Fingerprint verified. Using ticket {ticket.TicketType}...";
                    
                    // Get the current user ID from ERPNext
                    var currentUserResult = await _apiService.GetCurrentUserAsync();
                    if (!currentUserResult.IsSuccess || string.IsNullOrEmpty(currentUserResult.Data))
                    {
                        StatusMessage = "Error: Could not get current user information. Please login again.";
                        Log.Error("Failed to get current user when trying to use ticket {TicketId}: {Error}", 
                            ticket.Name, currentUserResult.ErrorMessage);
                        return;
                    }
                    
                    var useResult = await _apiService.UseTicketAsync(ticket.Name, currentUserResult.Data);

                    if (useResult.IsSuccess)
                    {
                        // Remove the used ticket from the list
                        Tickets.Remove(ticket);
                        UpdateTicketSummary();

                        StatusMessage = $"Ticket {ticket.TicketType} (₦{ticket.Amount:N2}) used successfully!";
                        Log.Information("Successfully used ticket {TicketId} for employee {EmployeeId}", 
                            ticket.Name, _currentEmployee.Name);

                        // Auto-refresh after 2 seconds
                        await Task.Delay(2000);
                        if (Tickets.Any())
                        {
                            StatusMessage = $"{Tickets.Count} ticket{(Tickets.Count == 1 ? "" : "s")} remaining";
                        }
                        else
                        {
                            StatusMessage = "All Ticket used. Click 'Fetch Ticket' to check for new ones.";
                        }
                    }
                    else
                    {
                        StatusMessage = $"Failed to use ticket: {useResult.ErrorMessage}";
                        Log.Error("Failed to use ticket {TicketId}: {Error}", ticket.Name, useResult.ErrorMessage);
                    }
                }
                else
                {
                    // Check if this is a timeout error specifically
                    if (!string.IsNullOrEmpty(verificationResult.ErrorMessage) && 
                        verificationResult.ErrorMessage.Contains("timeout", StringComparison.OrdinalIgnoreCase))
                    {
                        StatusMessage = "Fingerprint capture timeout. Ticket not used. Please try again.";
                        Log.Warning("Fingerprint capture timeout during ticket use");
                    }
                    else
                    {
                        StatusMessage = "Fingerprint verification failed. Ticket not used.";
                        Log.Warning("Fingerprint verification failed for ticket use: {Error}", 
                            verificationResult.ErrorMessage);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error using ticket {TicketId}", ticket.Name);
                StatusMessage = $"Error using ticket: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
                
                // Reset fingerprint service state
                try
                {
                    await _fingerprintService.ResetServiceStateAsync();
                }
                catch (Exception resetEx)
                {
                    Log.Warning(resetEx, "Error resetting fingerprint service state after ticket use");
                }
            }
        }

        private async Task UseAllTicketsAsync()
        {
            if (!Tickets.Any() || _currentEmployee == null)
                return;

            try
            {
                IsLoading = true;
                var ticketCount = Tickets.Count;
                var totalAmount = Tickets.Sum(t => t.Amount);
                
                StatusMessage = $"Please verify your fingerprint to use all {ticketCount} Ticket (₦{totalAmount:N2})...";

                // Perform fingerprint verification again for security
                var verificationResult = await _fingerprintService.VerifyAsync(_employees);

                if (verificationResult.IsSuccess && 
                    verificationResult.MatchedEmployee != null &&
                    verificationResult.MatchedEmployee.Name == _currentEmployee.Name)
                {
                    StatusMessage = $"Fingerprint verified. Using all {ticketCount} Ticket...";
                    
                    // Get the current user ID from ERPNext
                    var currentUserResult = await _apiService.GetCurrentUserAsync();
                    if (!currentUserResult.IsSuccess || string.IsNullOrEmpty(currentUserResult.Data))
                    {
                        StatusMessage = "Error: Could not get current user information. Please login again.";
                        Log.Error("Failed to get current user when trying to use all tickets: {Error}", 
                            currentUserResult.ErrorMessage);
                        return;
                    }
                    
                    var ticketsToUse = Tickets.ToList();
                    var useResult = await _apiService.UseAllTicketsAsync(ticketsToUse, currentUserResult.Data);

                    if (useResult.IsSuccess)
                    {
                        // Clear all Ticket from the list
                        Tickets.Clear();
                        UpdateTicketSummary();

                        StatusMessage = useResult.Data ?? $"All {ticketCount} Ticket used successfully!";
                        Log.Information("Successfully used all {Count} Ticket for employee {EmployeeId}", 
                            ticketCount, _currentEmployee.Name);

                        // Auto-refresh after 3 seconds
                        await Task.Delay(3000);
                        StatusMessage = "All Ticket used. Click 'Fetch Ticket' to check for new ones.";
                    }
                    else
                    {
                        StatusMessage = $"Failed to use all Ticket: {useResult.ErrorMessage}";
                        Log.Error("Failed to use all Ticket: {Error}", useResult.ErrorMessage);
                        
                        // Refresh the list to see which Ticket were actually used
                        await RefreshTicketsAsync();
                    }
                }
                else
                {
                    // Check if this is a timeout error specifically
                    if (!string.IsNullOrEmpty(verificationResult.ErrorMessage) && 
                        verificationResult.ErrorMessage.Contains("timeout", StringComparison.OrdinalIgnoreCase))
                    {
                        StatusMessage = "Fingerprint capture timeout. Ticket not used. Please try again.";
                        Log.Warning("Fingerprint capture timeout during use all Ticket");
                    }
                    else
                    {
                        StatusMessage = "Fingerprint verification failed. Ticket not used.";
                        Log.Warning("Fingerprint verification failed for use all Ticket: {Error}", 
                            verificationResult.ErrorMessage);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error using all Ticket");
                StatusMessage = $"Error using all Ticket: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
                
                // Reset fingerprint service state
                try
                {
                    await _fingerprintService.ResetServiceStateAsync();
                }
                catch (Exception resetEx)
                {
                    Log.Warning(resetEx, "Error resetting fingerprint service state after use all Ticket");
                }
            }
        }

        private async Task RefreshTicketsAsync()
        {
            if (_currentEmployee == null)
            {
                StatusMessage = "Please fetch Ticket first";
                return;
            }

            try
            {
                IsLoading = true;
                StatusMessage = "Refreshing Ticket...";

                // Fetch updated Ticket for the current employee
                var ticketsResult = await _apiService.GetUnusedTicketsAsync(_currentEmployee.Name);

                if (ticketsResult.IsSuccess && ticketsResult.Data != null)
                {
                    Tickets.Clear();
                    foreach (var ticket in ticketsResult.Data)
                    {
                        Tickets.Add(ticket);
                    }

                    UpdateTicketSummary();

                    if (Tickets.Any())
                    {
                        StatusMessage = $"Found {Tickets.Count} available ticket{(Tickets.Count == 1 ? "" : "s")} for {_currentEmployee.EmployeeName}";
                    }
                    Log.Information("Successfully fetched tickets for employee {EmployeeId}: {Count} tickets", 
                        Tickets.Count, _currentEmployee.Name);
                }
                else
                {
                    StatusMessage = $"Failed to refresh Ticket: {ticketsResult.ErrorMessage}";
                    Log.Error("Failed to refresh Ticket for employee {EmployeeId}: {Error}", 
                        _currentEmployee.Name, ticketsResult.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error refreshing Ticket");
                StatusMessage = $"Error refreshing Ticket: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void UpdateTicketSummary()
        {
            TicketCount = Tickets.Count;
            HasTickets = Tickets.Any();
            HasMultipleTickets = Tickets.Count > 1;
            TotalAmount = $"₦{Tickets.Sum(t => t.Amount):N2}";
        }

        private void OnFingerprintVerified(object? sender, FingerprintVerifiedEventArgs e)
        {
            // This event is already handled in the async methods
            // But could be used for additional UI updates if needed
        }

        private void OnFingerprintError(object? sender, string errorMessage)
        {
            // Handle timeout errors specifically
            if (errorMessage.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = "Fingerprint capture timeout. Please try again.";
                Log.Warning("Fingerprint capture timeout occurred");
            }
            else
            {
                StatusMessage = $"Fingerprint error: {errorMessage}";
                Log.Warning("Fingerprint error occurred: {Error}", errorMessage);
            }
            
            // Reset UI state to allow retry
            IsLoading = false;
            IsFetchEnabled = true;
        }

        partial void OnIsLoadingChanged(bool value)
        {
            // Update command can execute states when loading state changes
            ((AsyncRelayCommand)FetchTicketsCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)UseAllTicketsCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)RefreshTicketsCommand).NotifyCanExecuteChanged();
        }

        partial void OnHasMultipleTicketsChanged(bool value)
        {
            ((AsyncRelayCommand)UseAllTicketsCommand).NotifyCanExecuteChanged();
        }
    }
}