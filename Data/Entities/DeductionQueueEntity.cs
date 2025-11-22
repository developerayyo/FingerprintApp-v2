using System;
using System.ComponentModel.DataAnnotations;

namespace ERPNextFingerprintApp.Data.Entities
{
    public class DeductionQueueEntity
    {
        [Key]
        public int Id { get; set; }
        public string EmployeeId { get; set; } = string.Empty;
        public string DeductionType { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public bool IsSynced { get; set; } = false;
        public int RetryCount { get; set; } = 0;
        public string? LastErrorMessage { get; set; }
    }
}
