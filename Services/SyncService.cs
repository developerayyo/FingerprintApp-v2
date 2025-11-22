using System;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using ERPNextFingerprintApp.Models;

namespace ERPNextFingerprintApp.Services
{
    public class SyncService
    {
        private readonly ERPNextApiService _apiService;
        private readonly DatabaseService _databaseService;

        private System.Timers.Timer _timer;

        public SyncService(ERPNextApiService apiService, DatabaseService databaseService)
        {
            _apiService = apiService;
            _databaseService = databaseService;
        }

        public void StartBackgroundSync(TimeSpan interval)
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Dispose();
            }

            _timer = new System.Timers.Timer(interval.TotalMilliseconds);
            _timer.Elapsed += async (s, e) => await SyncAllAsync();
            _timer.AutoReset = true;
            _timer.Start();
            Log.Information("Background sync started with interval: {Interval}", interval);
        }

        public void StopBackgroundSync()
        {
            _timer?.Stop();
            Log.Information("Background sync stopped");
        }

        public async Task SyncAllAsync()
        {
            await PushOfflineTransactionsAsync();
            await SyncEmployeesAsync();
        }

        public async Task<bool> SyncEmployeesAsync()
        {
            try
            {
                Log.Information("Starting employee sync...");
                var result = await _apiService.GetEmployeesAsync();
                if (result.IsSuccess && result.Data != null)
                {
                    await _databaseService.SaveEmployeesAsync(result.Data);
                    Log.Information("Employee sync completed successfully");
                    return true;
                }
                else
                {
                    Log.Warning("Employee sync failed: {Error}", result.ErrorMessage);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during employee sync");
                return false;
            }
        }

        public async Task PushOfflineTransactionsAsync()
        {
            try
            {
                Log.Information("Checking for offline transactions to push...");
                
                // Sync Deductions
                var pendingDeductions = await _databaseService.GetPendingDeductionsAsync();
                
                if (pendingDeductions.Any())
                {
                    Log.Information("Found {Count} pending deductions", pendingDeductions.Count);

                    foreach (var deduction in pendingDeductions)
                    {
                        var result = await _apiService.CreateDeductionAsync(
                            deduction.EmployeeId, 
                            deduction.DeductionType, 
                            deduction.Amount, 
                            deduction.Description);

                        if (result.IsSuccess)
                        {
                            await _databaseService.MarkDeductionSyncedAsync(deduction.Id);
                            Log.Information("Synced deduction {Id} for employee {EmployeeId}", deduction.Id, deduction.EmployeeId);
                        }
                        else
                        {
                            await _databaseService.UpdateDeductionErrorAsync(deduction.Id, result.ErrorMessage);
                            Log.Warning("Failed to sync deduction {Id}: {Error}", deduction.Id, result.ErrorMessage);
                        }
                    }
                }

                // Sync Tickets
                var pendingTickets = await _databaseService.GetPendingTicketUsagesAsync();
                
                if (pendingTickets.Any())
                {
                    Log.Information("Found {Count} pending ticket usages", pendingTickets.Count);

                    foreach (var ticket in pendingTickets)
                    {
                        var result = await _apiService.UseTicketAsync(ticket.TicketId, ticket.UsedByEmployeeId);

                        if (result.IsSuccess)
                        {
                            await _databaseService.MarkTicketUsageSyncedAsync(ticket.Id);
                            Log.Information("Synced ticket usage {Id} for ticket {TicketId}", ticket.Id, ticket.TicketId);
                        }
                        else
                        {
                            await _databaseService.UpdateTicketUsageErrorAsync(ticket.Id, result.ErrorMessage);
                            Log.Warning("Failed to sync ticket usage {Id}: {Error}", ticket.Id, result.ErrorMessage);
                        }
                    }
                }

                if (!pendingDeductions.Any() && !pendingTickets.Any())
                {
                    Log.Information("No pending transactions to sync");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error pushing offline transactions");
            }
        }
    }
}
