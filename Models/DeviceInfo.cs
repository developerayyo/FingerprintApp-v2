namespace ERPNextFingerprintApp.Models
{
    /// <summary>
    /// Contains information about the connected fingerprint device
    /// </summary>
    public class DeviceInfo
    {
        /// <summary>
        /// Whether a fingerprint device is connected
        /// </summary>
        public bool IsConnected { get; set; }

        /// <summary>
        /// Name/description of the connected device
        /// </summary>
        public string DeviceName { get; set; } = string.Empty;

        /// <summary>
        /// Number of connected fingerprint devices
        /// </summary>
        public int DeviceCount { get; set; }

        /// <summary>
        /// Whether the connected device is a U.are.U 4500 scanner
        /// </summary>
        public bool IsUareU4500 { get; set; }

        /// <summary>
        /// Current status of the device
        /// </summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// Additional device details (optional)
        /// </summary>
        public string? Details { get; set; }
    }
}