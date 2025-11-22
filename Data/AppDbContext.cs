using Microsoft.EntityFrameworkCore;
using ERPNextFingerprintApp.Data.Entities;
using System.IO;
using System;

namespace ERPNextFingerprintApp.Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<EmployeeEntity> Employees { get; set; }
        public DbSet<DeductionQueueEntity> DeductionQueue { get; set; }
        public DbSet<TicketQueueEntity> TicketQueue { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.db");
            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
            // Ensure Employee Name is unique
            modelBuilder.Entity<EmployeeEntity>()
                .HasIndex(e => e.Name)
                .IsUnique();
        }
    }
}
