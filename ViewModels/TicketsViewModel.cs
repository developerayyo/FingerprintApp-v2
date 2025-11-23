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
        private readonly DatabaseService _databaseService;
        private readonly SyncService _syncService;
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
        private ObservableCollection<Ticket> _recentTickets = new();

        [ObservableProperty]
        private string _totalAmount = "₦0.00";

        [ObservableProperty]
        private int _ticketCount = 0;

        public ICommand FetchTicketsCommand { get; }
        public ICommand UseTicketCommand { get; }
        public ICommand UseAllTicketsCommand { get; }
        public ICommand RefreshTicketsCommand { get; }

        public TicketsViewModel(ERPNextApiService apiService, FingerprintService fingerprintService, DatabaseService databaseService, SyncService syncService)
        {
            _apiService = apiService ?? throw new ArgumentNullException(nameof(apiService));
            _fingerprintService = fingerprintService ?? throw new ArgumentNullException(nameof(fingerprintService));
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
            _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));

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
                StatusMessage = "Syncing employee data...";

                // Trigger sync in background
                _ = _syncService.SyncEmployeesAsync();

                // Load from local DB
                var activeEmployees = await _databaseService.GetActiveEmployeesAsync();
                
                _employees = activeEmployees.Select(e => new Employee
                {
                    Name = e.Name,
                    EmployeeName = e.EmployeeName,
                    Department = e.Department,
                    Designation = e.Designation,
                    FingerprintTemplate = e.FingerprintTemplate
                }).ToList();

                Log.Information("Loaded {Count} employees for Ticket verification", _employees.Count);
                StatusMessage = $"Loaded {_employees.Count} employees";
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
                StatusMessage = "Place finger on scanner to fetch Ticket...";

                // Clear previous data
                Tickets.Clear();
                _currentEmployee = null;
                UpdateTicketSummary();

                // Capture fingerprint first
                var captureResult = await _fingerprintService.CaptureAsync();
                if (!captureResult.IsSuccess)
                {
                    StatusMessage = $"Capture failed: {captureResult.ErrorMessage}";
                    return;
                }

                StatusMessage = "Verifying fingerprint...";

                // Verify against local DB
                var verificationResult = await _fingerprintService.VerifyAgainstLocalDbAsync(captureResult.Template);

                if (verificationResult.IsSuccess && verificationResult.MatchedEmployee != null)
                {
                    _currentEmployee = verificationResult.MatchedEmployee;
                    StatusMessage = $"Verified: {_currentEmployee.EmployeeName}. Fetching Ticket...";
                    
                    Log.Information("Fingerprint verified for employee: {EmployeeId} - {EmployeeName}", 
                        _currentEmployee.Name, _currentEmployee.EmployeeName);

                    // Fetch Ticket for the verified employee (API only)
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
                    var errorMessage = verificationResult.ErrorMessage ?? "Verification failed";
                    StatusMessage = $"Verification failed: {errorMessage}";
                    Log.Warning("Fingerprint verification failed for Ticket fetch: {Error}", errorMessage);
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
            }
        }

        private async Task UseTicketAsync(Ticket? ticket)
        {
            if (ticket == null || _currentEmployee == null)
                return;

            try
            {
                IsLoading = true;
                StatusMessage = $"Using ticket {ticket.TicketType}...";
                
                // Get current user (operator)
                string currentUser = _apiService.CurrentUsername;
                if (string.IsNullOrEmpty(currentUser))
                {
                    var currentUserResult = await _apiService.GetCurrentUserAsync();
                    if (currentUserResult.IsSuccess) currentUser = currentUserResult.Data;
                }

                if (string.IsNullOrEmpty(currentUser))
                {
                    StatusMessage = "Error: Could not identify current operator.";
                    return;
                }
                
                var useResult = await _apiService.UseTicketAsync(ticket.Name, currentUser);

                if (useResult.IsSuccess)
                {
                    // Add to recent transactions
                    var usedTicket = new Ticket
                    {
                        Name = ticket.Name,
                        Employee = ticket.Employee,
                        EmployeeName = ticket.EmployeeName,
                        TicketType = ticket.TicketType,
                        Amount = ticket.Amount,
                        Status = "Used"
                    };
                    
                    RecentTickets.Insert(0, usedTicket);
                    
                    // Keep only last 10 transactions
                    while (RecentTickets.Count > 10)
                    {
                        RecentTickets.RemoveAt(RecentTickets.Count - 1);
                    }
                    
                    // Remove the used ticket from the list
                    Tickets.Remove(ticket);
                    UpdateTicketSummary();

                    StatusMessage = $"Ticket {ticket.TicketType} (₦{ticket.Amount:N2}) used successfully!";
                    Log.Information("Successfully used ticket {TicketId} for employee {EmployeeId}", 
                        ticket.Name, _currentEmployee.Name);
                }
                else
                {
                    StatusMessage = $"Failed to use ticket: {useResult.ErrorMessage}";
                    Log.Error("Failed to use ticket {TicketId}: {Error}", ticket.Name, useResult.ErrorMessage);
                }

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
            catch (Exception ex)
            {
                Log.Error(ex, "Error using ticket {TicketId}", ticket.Name);
                StatusMessage = $"Error using ticket: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
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
                
                StatusMessage = $"Place finger to use all {ticketCount} Ticket (₦{totalAmount:N2})...";

                // Capture fingerprint first
                var captureResult = await _fingerprintService.CaptureAsync();
                if (!captureResult.IsSuccess)
                {
                    StatusMessage = $"Capture failed: {captureResult.ErrorMessage}";
                    return;
                }

                StatusMessage = "Verifying fingerprint...";

                // Verify against local DB
                var verificationResult = await _fingerprintService.VerifyAgainstLocalDbAsync(captureResult.Template);

                if (verificationResult.IsSuccess && 
                    verificationResult.MatchedEmployee != null &&
                    verificationResult.MatchedEmployee.Name == _currentEmployee.Name)
                {
                    StatusMessage = $"Verified. Using all {ticketCount} Ticket...";
                    
                    // Get current user (operator)
                    string currentUser = _apiService.CurrentUsername;
                    if (string.IsNullOrEmpty(currentUser))
                    {
                        var currentUserResult = await _apiService.GetCurrentUserAsync();
                        if (currentUserResult.IsSuccess) currentUser = currentUserResult.Data;
                    }

                    if (string.IsNullOrEmpty(currentUser))
                    {
                        StatusMessage = "Error: Could not identify current operator.";
                        return;
                    }
                    
                    var ticketsToUse = Tickets.ToList();
                    var useResult = await _apiService.UseAllTicketsAsync(ticketsToUse, currentUser);

                    if (useResult.IsSuccess)
                    {
                        // Add all used tickets to recent transactions
                        foreach (var ticket in ticketsToUse)
                        {
                            var usedTicket = new Ticket
                            {
                                Name = ticket.Name,
                                Employee = ticket.Employee,
                                EmployeeName = ticket.EmployeeName,
                                TicketType = ticket.TicketType,
                                Amount = ticket.Amount,
                                Status = "Used"
                            };
                            
                            RecentTickets.Insert(0, usedTicket);
                        }
                        
                        // Keep only last 10 transactions
                        while (RecentTickets.Count > 10)
                        {
                            RecentTickets.RemoveAt(RecentTickets.Count - 1);
                        }
                        
                        // Clear all Ticket from the list
                        Tickets.Clear();
                        UpdateTicketSummary();

                        StatusMessage = useResult.Data ?? $"All {ticketCount} Ticket used successfully!";
                        Log.Information("Successfully used all {Count} Ticket for employee {EmployeeId}", 
                            ticketCount, _currentEmployee.Name);
                    }
                    else
                    {
                        StatusMessage = $"Failed to use all Ticket: {useResult.ErrorMessage}";
                        Log.Error("Failed to use all Ticket: {Error}", useResult.ErrorMessage);
                        
                        // Refresh the list to see which Ticket were actually used
                        await RefreshTicketsAsync();
                    }

                    // Auto-refresh after 3 seconds
                    await Task.Delay(3000);
                    StatusMessage = "All Ticket used. Click 'Fetch Ticket' to check for new ones.";
                }
                else
                {
                    var errorMessage = verificationResult.ErrorMessage ?? "Verification failed";
                    StatusMessage = $"Verification failed: {errorMessage}";
                    Log.Warning("Fingerprint verification failed for use all Ticket: {Error}", errorMessage);
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