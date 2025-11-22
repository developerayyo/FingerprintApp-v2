using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Serilog;
using ERPNextFingerprintApp.Data;
using ERPNextFingerprintApp.Data.Entities;
using ERPNextFingerprintApp.Models;

namespace ERPNextFingerprintApp.Services
{
    public class DatabaseService
    {
        public DatabaseService()
        {
            // Ensure database is created
            try
            {
                using (var context = new AppDbContext())
                {
                    context.Database.EnsureCreated();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to ensure database creation");
            }
        }

        public async Task SaveEmployeesAsync(List<Employee> employees)
        {
            try
            {
                using (var context = new AppDbContext())
                {
                    // Get existing IDs to decide update vs insert
                    var existingIds = await context.Employees.Select(e => e.Name).ToListAsync();
                    
                    var newEntities = new List<EmployeeEntity>();
                    
                    foreach (var emp in employees)
                    {
                        var entity = new EmployeeEntity
                        {
                            Name = emp.Name,
                            EmployeeName = emp.EmployeeName,
                            Department = emp.Department,
                            Designation = emp.Designation,
                            FingerprintTemplate = emp.FingerprintTemplate ?? string.Empty,
                            IsActive = emp.IsActive,
                            LastSynced = DateTime.Now
                        };

                        if (existingIds.Contains(emp.Name))
                        {
                            context.Employees.Update(entity);
                        }
                        else
                        {
                            newEntities.Add(entity);
                        }
                    }

                    if (newEntities.Any())
                    {
                        await context.Employees.AddRangeAsync(newEntities);
                    }

                    await context.SaveChangesAsync();
                    Log.Information("Saved {Count} employees to local database", employees.Count);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error saving employees to local database");
                throw;
            }
        }

        public async Task<List<Employee>> GetActiveEmployeesAsync()
        {
            try
            {
                using (var context = new AppDbContext())
                {
                    var entities = await context.Employees
                        .Where(e => e.IsActive && !string.IsNullOrEmpty(e.FingerprintTemplate))
                        .ToListAsync();

                    return entities.Select(e => new Employee
                    {
                        Name = e.Name,
                        EmployeeName = e.EmployeeName,
                        Department = e.Department,
                        Designation = e.Designation,
                        FingerprintTemplate = e.FingerprintTemplate,
                        IsActive = e.IsActive
                    }).ToList();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error retrieving employees from local database");
                return new List<Employee>();
            }
        }

        public async Task QueueDeductionAsync(string employeeId, string type, decimal amount, string description)
        {
            await QueueDeductionAsync(new DeductionQueueEntity
            {
                EmployeeId = employeeId,
                DeductionType = type,
                Amount = amount,
                Description = description,
                CreatedAt = DateTime.Now,
                IsSynced = false
            });
        }

        public async Task QueueDeductionAsync(DeductionQueueEntity entity)
        {
            try
            {
                using (var context = new AppDbContext())
                {
                    await context.DeductionQueue.AddAsync(entity);
                    await context.SaveChangesAsync();
                    Log.Information("Queued offline deduction for employee {EmployeeId}", entity.EmployeeId);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error queuing deduction");
                throw;
            }
        }

        public async Task<List<DeductionQueueEntity>> GetPendingDeductionsAsync()
        {
            try
            {
                using (var context = new AppDbContext())
                {
                    return await context.DeductionQueue
                        .Where(d => !d.IsSynced)
                        .OrderBy(d => d.CreatedAt)
                        .ToListAsync();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error retrieving pending deductions");
                return new List<DeductionQueueEntity>();
            }
        }

        public async Task MarkDeductionSyncedAsync(int id)
        {
            try
            {
                using (var context = new AppDbContext())
                {
                    var entity = await context.DeductionQueue.FindAsync(id);
                    if (entity != null)
                    {
                        entity.IsSynced = true;
                        await context.SaveChangesAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error marking deduction as synced");
            }
        }

        public async Task UpdateDeductionErrorAsync(int id, string error)
        {
            try
            {
                using (var context = new AppDbContext())
                {
                    var entity = await context.DeductionQueue.FindAsync(id);
                    if (entity != null)
                    {
                        entity.LastErrorMessage = error;
                        entity.RetryCount++;
                        await context.SaveChangesAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error updating deduction error");
            }
        }

        public async Task QueueTicketUsageAsync(TicketQueueEntity entity)
        {
            try
            {
                using (var context = new AppDbContext())
                {
                    await context.TicketQueue.AddAsync(entity);
                    await context.SaveChangesAsync();
                    Log.Information("Queued offline ticket usage for ticket {TicketId}", entity.TicketId);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error queuing ticket usage");
                throw;
            }
        }

        public async Task<List<TicketQueueEntity>> GetPendingTicketUsagesAsync()
        {
            try
            {
                using (var context = new AppDbContext())
                {
                    return await context.TicketQueue
                        .Where(t => !t.IsSynced)
                        .OrderBy(t => t.UsedAt)
                        .ToListAsync();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error retrieving pending ticket usages");
                return new List<TicketQueueEntity>();
            }
        }

        public async Task MarkTicketUsageSyncedAsync(int id)
        {
            try
            {
                using (var context = new AppDbContext())
                {
                    var entity = await context.TicketQueue.FindAsync(id);
                    if (entity != null)
                    {
                        entity.IsSynced = true;
                        await context.SaveChangesAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error marking ticket usage as synced");
            }
        }

        public async Task UpdateTicketUsageErrorAsync(int id, string error)
        {
            try
            {
                using (var context = new AppDbContext())
                {
                    var entity = await context.TicketQueue.FindAsync(id);
                    if (entity != null)
                    {
                        entity.LastErrorMessage = error;
                        entity.RetryCount++;
                        await context.SaveChangesAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error updating ticket usage error");
            }
        }
    }
}
