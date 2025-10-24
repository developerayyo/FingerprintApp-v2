using Newtonsoft.Json;

namespace ERPNextFingerprintApp.Models
{
    public class VerificationResult
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; } = string.Empty;

        [JsonProperty("employee")]
        public string? Employee { get; set; }

        [JsonProperty("employee_name")]
        public string? EmployeeName { get; set; }

        [JsonProperty("deduction_id")]
        public string? DeductionId { get; set; }

        [JsonProperty("workflow_state")]
        public string? WorkflowState { get; set; }
    }
}