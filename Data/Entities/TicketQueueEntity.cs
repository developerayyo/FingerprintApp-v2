using System;
using System.ComponentModel.DataAnnotations;

namespace ERPNextFingerprintApp.Data.Entities
{
    public class TicketQueueEntity
    {
        [Key]
        public int Id { get; set; }
        public string TicketId { get; set; } = string.Empty;
        public string UsedByEmployeeId { get; set; } = string.Empty;
        public DateTime UsedAt { get; set; } = DateTime.Now;
        public bool IsSynced { get; set; } = false;
        public int RetryCount { get; set; } = 0;
        public string? LastErrorMessage { get; set; }
    }
}
