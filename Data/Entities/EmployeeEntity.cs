using System;
using System.ComponentModel.DataAnnotations;

namespace ERPNextFingerprintApp.Data.Entities
{
    public class EmployeeEntity
    {
        [Key]
        public string Name { get; set; } = string.Empty; // ERPNext ID (e.g., HR-EMP-00001)
        public string EmployeeName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Designation { get; set; } = string.Empty;
        public string FingerprintTemplate { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public DateTime LastSynced { get; set; } = DateTime.Now;
    }
}
