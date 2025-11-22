using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using DPUruNet;
using ERPNextFingerprintApp.Models;

namespace ERPNextFingerprintApp.Services
{
    /// <summary>
    /// Event args for fingerprint capture with quality information
    /// </summary>
    public class FingerprintCaptureEventArgs : EventArgs
    {
        public byte[] FingerprintData { get; }
        public int QualityScore { get; }
        public string QualityFeedback { get; }
        public Constants.CaptureQuality SdkQuality { get; }

        public FingerprintCaptureEventArgs(byte[] fingerprintData, int qualityScore, string qualityFeedback, Constants.CaptureQuality sdkQuality)
        {
            FingerprintData = fingerprintData;
            QualityScore = qualityScore;
            QualityFeedback = qualityFeedback;
            SdkQuality = sdkQuality;
        }
    }

    /// <summary>
    /// DigitalPersona OneTouch SDK wrapper for U.are.U 4500 fingerprint scanner
    /// </summary>
    public class DigitalPersonaSDK : IDisposable
    {
        private readonly ILogger _logger;
        private ReaderCollection _readers;
        private Reader _currentReader;
        private bool _isInitialized;
        private bool _isCapturing;
        private bool _disposed;
        
        // Template cache for large dataset optimization
        private readonly Dictionary<string, Fmd> _templateCache = new Dictionary<string, Fmd>();
        private readonly object _cacheLock = new object();

        /// <summary>
        /// Event fired when status changes
        /// </summary>
        public event EventHandler<string> StatusChanged;

        /// <summary>
        /// Event fired when fingerprint is captured
        /// </summary>
        public event EventHandler<FingerprintCaptureEventArgs> FingerprintCaptured;

        public DigitalPersonaSDK(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Initialize the DigitalPersona SDK and detect devices
        /// </summary>
        public async Task<bool> InitializeAsync()
        {
            try
            {
                _logger.Information("Initializing DigitalPersona OneTouch SDK...");

                // Check if DigitalPersona SDK is properly installed
                if (!IsDigitalPersonaSDKAvailable())
                {
                    _logger.Error("DigitalPersona SDK is not properly installed or DLL files are missing");
                    StatusChanged?.Invoke(this, "DigitalPersona SDK not installed");
                    return false;
                }

                // Get available readers
                _readers = ReaderCollection.GetReaders();
                
                _logger.Information("SDK found {ReaderCount} total readers", _readers?.Count ?? 0);
                
                if (_readers == null || _readers.Count == 0)
                {
                    _logger.Warning("No DigitalPersona readers found");
                    StatusChanged?.Invoke(this, "No fingerprint devices found");
                    return false;
                }

                // Log all found devices for debugging
                for (int i = 0; i < _readers.Count; i++)
                {
                    var reader = _readers[i];
                    _logger.Information("Reader {Index}: Name='{Name}', SerialNumber='{Serial}', Vendor='{Vendor}', Product='{Product}'", 
                        i, reader.Description.Name, reader.Description.SerialNumber, 
                        reader.Description.Id.VendorName, reader.Description.Id.ProductName);
                }

                // Find U.are.U 4500 device
                foreach (Reader reader in _readers)
                {
                    _logger.Information("Checking if reader '{Name}' is U.are.U 4500 device", reader.Description.Name);
                    if (IsUareU4500Device(reader))
                    {
                        _currentReader = reader;
                        break;
                    }
                }

                if (_currentReader == null)
                {
                    _logger.Warning("No U.are.U 4500 device found");
                    StatusChanged?.Invoke(this, "U.are.U 4500 device not found");
                    return false;
                }

                // Open the reader
                Constants.ResultCode result = _currentReader.Open(Constants.CapturePriority.DP_PRIORITY_COOPERATIVE);
                
                if (result != Constants.ResultCode.DP_SUCCESS)
                {
                    _logger.Error("Failed to open DigitalPersona reader. Result: {Result}", result);
                    StatusChanged?.Invoke(this, $"Failed to open device: {result}");
                    return false;
                }

                _isInitialized = true;
                _logger.Information("DigitalPersona SDK initialized successfully with device: {DeviceName}", _currentReader.Description.Name);
                StatusChanged?.Invoke(this, "DigitalPersona device ready");
                
                return true;
            }
            catch (System.IO.FileNotFoundException ex) when (ex.Message.Contains("DPUruNet.dll") || ex.Message.Contains("DPCtlUruNet.dll") || ex.Message.Contains("DPFPApiNet.dll") || ex.Message.Contains("DPFPApi.dll"))
            {
                _logger.Error(ex, "DigitalPersona SDK DLL files not found. Please install the DigitalPersona U.are.U SDK");
                StatusChanged?.Invoke(this, "DigitalPersona SDK not installed - DLL files missing");
                return false;
            }
            catch (System.DllNotFoundException ex) when (ex.Message.Contains("DPUruNet.dll") || ex.Message.Contains("DPCtlUruNet.dll") || ex.Message.Contains("DPFPApi.dll"))
            {
                _logger.Error(ex, "DigitalPersona SDK native DLL not found. Please install the DigitalPersona U.are.U SDK");
                StatusChanged?.Invoke(this, "DigitalPersona SDK not installed - Native DLL missing");
                return false;
            }
            catch (System.EntryPointNotFoundException ex) when (ex.Message.Contains("DPFPCreateCapture"))
            {
                _logger.Error(ex, "DigitalPersona SDK entry point not found. SDK version mismatch or corrupted installation");
                StatusChanged?.Invoke(this, "DigitalPersona SDK installation corrupted");
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to initialize DigitalPersona SDK");
                StatusChanged?.Invoke(this, "SDK initialization failed");
                return false;
            }
        }

        /// <summary>
        /// Check if DigitalPersona SDK is available and properly installed
        /// </summary>
        private bool IsDigitalPersonaSDKAvailable()
        {
            try
            {
                // Check if the main SDK DLL files exist (updated for newer SDK structure)
                string sdkPath = @"C:\Program Files\DigitalPersona\U.are.U SDK\Windows";
                string[] requiredDlls = {
                    Path.Combine(sdkPath, "Lib\\.NET\\DPUruNet.dll"),
                    Path.Combine(sdkPath, "Lib\\.NET\\DPCtlUruNet.dll")
                };

                bool anyFound = false;
                foreach (string dll in requiredDlls)
                {
                    if (File.Exists(dll))
                    {
                        anyFound = true;
                        _logger.Information("Found DigitalPersona SDK file: {DllPath}", dll);
                    }
                    else
                    {
                        _logger.Warning("DigitalPersona SDK file not found: {DllPath}", dll);
                    }
                }

                return anyFound;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error checking DigitalPersona SDK availability");
                return false;
            }
        }

        /// <summary>
        /// Check if any DigitalPersona devices are connected
        /// </summary>
        public async Task<bool> CheckDeviceStatusAsync()
        {
            try
            {
                if (!_isInitialized)
                {
                    return await InitializeAsync();
                }

                // Check if current reader is still available
                if (_currentReader != null)
                {
                    var result = _currentReader.GetStatus();
                    if (result == Constants.ResultCode.DP_SUCCESS)
                    {
                        bool isReady = _currentReader.Status.Status == Constants.ReaderStatuses.DP_STATUS_READY;
                        
                        _logger.Information("Device status check - Ready: {IsReady}, Status: {Status}", isReady, _currentReader.Status.Status);
                        
                        if (isReady)
                        {
                            StatusChanged?.Invoke(this, "Device ready");
                        }
                        else
                        {
                            StatusChanged?.Invoke(this, $"Device status: {_currentReader.Status.Status}");
                        }
                        
                        return isReady;
                    }
                    else
                    {
                        _logger.Error($"Failed to get device status: {result}");
                        StatusChanged?.Invoke(this, "Device status check failed");
                        return false;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error checking device status");
                StatusChanged?.Invoke(this, "Device status check failed");
                return false;
            }
        }

        /// <summary>
        /// Check the device status before starting capture operations.
        /// This follows the SDK example pattern for proper device management.
        /// </summary>
        /// <returns>True if device is ready for capture, false otherwise</returns>
        public async Task<bool> GetStatusAsync()
        {
            try
            {
                if (_currentReader == null)
                {
                    _logger.Warning("Cannot check status - no reader available");
                    return false;
                }

                _logger.Debug("Checking reader status...");
                Constants.ResultCode result = _currentReader.GetStatus();

                if (result != Constants.ResultCode.DP_SUCCESS)
                {
                    _logger.Error("Failed to get reader status. Result: {Result}", result);
                    StatusChanged?.Invoke(this, $"Status check failed: {result}");
                    return false;
                }

                var status = _currentReader.Status.Status;
                _logger.Debug("Reader status: {Status}", status);

                switch (status)
                {
                    case Constants.ReaderStatuses.DP_STATUS_READY:
                        _logger.Debug("Reader is ready for capture");
                        return true;

                    case Constants.ReaderStatuses.DP_STATUS_BUSY:
                        _logger.Debug("Reader is busy, waiting...");
                        StatusChanged?.Invoke(this, "Device busy, waiting...");
                        await Task.Delay(50); // Follow SDK example pattern
                        // Recursive call to check again after delay
                        return await GetStatusAsync();

                    case Constants.ReaderStatuses.DP_STATUS_NEED_CALIBRATION:
                        _logger.Information("Reader needs calibration, performing calibration...");
                        StatusChanged?.Invoke(this, "Calibrating device...");
                        
                        var calibrationResult = _currentReader.Calibrate();
                        if (calibrationResult != Constants.ResultCode.DP_SUCCESS)
                        {
                            _logger.Error("Calibration failed. Result: {Result}", calibrationResult);
                            StatusChanged?.Invoke(this, $"Calibration failed: {calibrationResult}");
                            return false;
                        }
                        
                        _logger.Information("Calibration completed successfully");
                        StatusChanged?.Invoke(this, "Calibration completed");
                        
                        // Wait a moment after calibration then check status again
                        await Task.Delay(100);
                        return await GetStatusAsync();



                    default:
                        _logger.Warning("Unknown reader status: {Status}", status);
                        StatusChanged?.Invoke(this, $"Unknown device status: {status}");
                        return false;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Exception occurred while checking reader status");
                StatusChanged?.Invoke(this, "Status check error");
                return false;
            }
        }

        /// <summary>
        /// Prepare reader for capture by checking status and handling calibration/busy states.
        /// This method should be called before any capture operation.
        /// </summary>
        /// <returns>True if reader is ready for capture, false otherwise</returns>
        public async Task<bool> PrepareReaderForCaptureAsync()
        {
            if (!_isInitialized)
            {
                _logger.Warning("SDK not initialized, cannot prepare reader for capture");
                return false;
            }

            if (_currentReader == null)
            {
                _logger.Warning("No reader available for capture preparation");
                return false;
            }

            _logger.Debug("Preparing reader for capture...");
            
            // Check status and handle calibration/busy states
            bool isReady = await GetStatusAsync();
            
            if (isReady)
            {
                _logger.Debug("Reader is ready for capture");
                StatusChanged?.Invoke(this, "Device ready for capture");
            }
            else
            {
                _logger.Warning("Reader is not ready for capture");
            }
            
            return isReady;
        }

        /// <summary>
        /// Start fingerprint capture with retry logic for device busy errors
        /// </summary>
        public async Task<bool> StartCaptureAsync()
        {
            const int maxRetries = 3;
            const int retryDelayMs = 1000;
            
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    if (_currentReader == null || !_isInitialized)
                    {
                        _logger.Warning("Cannot start capture: SDK not initialized or no reader available");
                        StatusChanged?.Invoke(this, "No fingerprint reader available");
                        return false;
                    }

                    if (_isCapturing)
                    {
                        _logger.Information("Capture already in progress");
                        return true;
                    }

                    _logger.Information("Starting fingerprint capture (attempt {Attempt}/{MaxRetries})...", attempt, maxRetries);
                    
                    // Prepare reader for capture (includes status check, calibration, and busy handling)
                    bool isReaderReady = await PrepareReaderForCaptureAsync();
                    if (!isReaderReady)
                    {
                        _logger.Error("Reader is not ready for capture on attempt {Attempt}", attempt);
                        
                        if (attempt < maxRetries)
                        {
                            _logger.Information("Retrying after reader preparation failure...");
                            await Task.Delay(retryDelayMs);
                            continue;
                        }
                        
                        StatusChanged?.Invoke(this, "Device not ready for capture");
                        return false;
                    }

                    StatusChanged?.Invoke(this, "Place finger on scanner...");

                    // Hook up capture handler
                    _currentReader.On_Captured += OnCaptured;

                    // Start capture
                    Constants.ResultCode result = _currentReader.CaptureAsync(
                        Constants.Formats.Fid.ANSI, 
                        Constants.CaptureProcessing.DP_IMG_PROC_DEFAULT, 
                        _currentReader.Capabilities.Resolutions[0]);
                    
                    if (result == Constants.ResultCode.DP_SUCCESS)
                    {
                        _isCapturing = true;
                        _logger.Information("Fingerprint capture started successfully on attempt {Attempt}", attempt);
                        return true;
                    }
                    else if (result == Constants.ResultCode.DP_DEVICE_BUSY)
                    {
                        _logger.Warning("Device busy on attempt {Attempt}/{MaxRetries}: {Result}", attempt, maxRetries, result);
                        _currentReader.On_Captured -= OnCaptured;
                        
                        if (attempt < maxRetries)
                        {
                            StatusChanged?.Invoke(this, $"Device busy - retrying... ({attempt}/{maxRetries})");
                            
                            // Force cleanup and wait before retry
                            await StopCaptureAsync();
                            await Task.Delay(retryDelayMs * attempt); // Increasing delay
                            continue;
                        }
                        
                        StatusChanged?.Invoke(this, "Device busy - please try again later");
                        return false;
                    }
                    else
                    {
                        _logger.Error($"Failed to start capture on attempt {attempt}: {result}");
                        _currentReader.On_Captured -= OnCaptured;
                        
                        if (attempt < maxRetries)
                        {
                            await Task.Delay(retryDelayMs);
                            continue;
                        }
                        
                        StatusChanged?.Invoke(this, $"Capture failed: {result}");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error starting fingerprint capture on attempt {Attempt}", attempt);
                    
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(retryDelayMs);
                        continue;
                    }
                    
                    StatusChanged?.Invoke(this, "Capture start failed");
                    return false;
                }
            }
            
            return false;
        }

        /// <summary>
        /// Stop fingerprint capture with enhanced device cleanup
        /// </summary>
        public async Task<bool> StopCaptureAsync()
        {
            try
            {
                if (_currentReader == null)
                {
                    _isCapturing = false;
                    return true;
                }

                _logger.Information("Stopping fingerprint capture...");
                
                // Always attempt to cancel any pending capture operation to prevent DP_DEVICE_BUSY
                Constants.ResultCode cancelResult = _currentReader.CancelCapture();
                if (cancelResult != Constants.ResultCode.DP_SUCCESS)
                {
                    _logger.Warning("Cancel capture returned: {Result}", cancelResult);
                }
                else
                {
                    _logger.Information("Capture operation cancelled successfully");
                }
                
                // Remove capture handler to prevent memory leaks
                try
                {
                    _currentReader.On_Captured -= OnCaptured;
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Error removing capture handler");
                }
                
                // Reset capturing state
                _isCapturing = false;
                
                // Wait for device to fully reset
                await Task.Delay(500);
                
                // Verify device is ready after cleanup
                try
                {
                    var statusResult = _currentReader.GetStatus();
                    _logger.Information("Device status after cleanup: {Status}", statusResult);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Could not check device status after cleanup");
                }
                
                _logger.Information("Fingerprint capture stopped and device cleaned up");
                StatusChanged?.Invoke(this, "Capture stopped");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error stopping fingerprint capture");
                _isCapturing = false; // Force reset state even on error
                return false;
            }
        }

        /// <summary>
        /// Get device information
        /// </summary>
        public DeviceInfo GetDeviceInfo()
        {
            if (!_isInitialized || _currentReader == null)
            {
                return null;
            }

            try
            {
                var description = _currentReader.Description;
                return new DeviceInfo
                {
                    DeviceName = description.Name,
                    IsConnected = true,
                    DeviceCount = 1,
                    IsUareU4500 = description.Name.Contains("4500")
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting device info");
                return null;
            }
        }

        /// <summary>
        /// Callback for when a fingerprint is captured
        /// </summary>
        private void OnCaptured(CaptureResult captureResult)
        {
            try
            {
                _isCapturing = false;

                // Enhanced quality validation following SDK best practices
                if (!CheckCaptureResult(captureResult))
                {
                    return;
                }

                // Get numerical quality score from capture result
                int captureScore = captureResult.Score;
                _logger.Information("Fingerprint captured successfully with quality: {Quality}, Score: {Score}", 
                    captureResult.Quality, captureScore);
                
                // Extract features using FeatureExtraction.CreateFmdFromFid() following SDK best practices
                DataResult<Fmd> fmdResult = FeatureExtraction.CreateFmdFromFid(
                    captureResult.Data, 
                    Constants.Formats.Fmd.ANSI);

                if (fmdResult.ResultCode != Constants.ResultCode.DP_SUCCESS)
                {
                    _logger.Error("Feature extraction failed with result: {Result}", fmdResult.ResultCode);
                    StatusChanged?.Invoke(this, $"Feature extraction failed: {fmdResult.ResultCode}");
                    return;
                }

                // Calculate NFIQ quality score for the captured fingerprint
                int nfiqScore = CalculateNFIQQuality(captureResult.Data);
                
                // Get template quality score from the FMD
                int templateQuality = GetTemplateQuality(fmdResult.Data);

                // Convert FMD to byte array for storage/transmission
                byte[] fmdTemplate = fmdResult.Data.Bytes;
                
                _logger.Information("Fingerprint template extracted successfully. Template size: {Size} bytes, NFIQ Score: {NFIQ}, Template Quality: {TemplateQuality}", 
                    fmdTemplate.Length, nfiqScore, templateQuality);
                
                // Provide detailed quality feedback
                string qualityFeedback = GetQualityFeedback(captureScore, nfiqScore, templateQuality);
                StatusChanged?.Invoke(this, qualityFeedback);
                
                // Convert SDK quality to numerical score for better assessment
                int sdkQualityScore = ConvertSdkQualityToScore(captureResult.Quality);
                
                // Use the higher of the two quality scores for better accuracy
                int finalQualityScore = Math.Max(sdkQualityScore, captureScore);
                
                FingerprintCaptured?.Invoke(this, new FingerprintCaptureEventArgs(
                    fmdTemplate, 
                    finalQualityScore, 
                    qualityFeedback, 
                    captureResult.Quality));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error processing captured fingerprint");
                StatusChanged?.Invoke(this, "Error processing fingerprint");
            }
        }

        /// <summary>
        /// Enhanced capture result validation following SDK best practices
        /// </summary>
        private bool CheckCaptureResult(CaptureResult captureResult)
        {
            try
            {
                // Check for null data or unsuccessful result
                if (captureResult.Data == null || captureResult.ResultCode != Constants.ResultCode.DP_SUCCESS)
                {
                    if (captureResult.ResultCode != Constants.ResultCode.DP_SUCCESS)
                    {
                        _logger.Warning("Capture failed with result: {Result}", captureResult.ResultCode);
                        StatusChanged?.Invoke(this, $"Capture failed: {captureResult.ResultCode}");
                    }
                    else
                    {
                        _logger.Warning("No fingerprint data captured");
                        StatusChanged?.Invoke(this, "No fingerprint data captured");
                    }
                    return false;
                }

                // Enhanced quality validation with specific messages
                if (captureResult.Quality != Constants.CaptureQuality.DP_QUALITY_GOOD)
                {
                    string qualityMessage = captureResult.Quality switch
                    {
                        Constants.CaptureQuality.DP_QUALITY_TIMED_OUT => "Capture timed out - place finger on scanner within 10 seconds",
                        Constants.CaptureQuality.DP_QUALITY_CANCELED => "Capture was canceled - please try again",
                        Constants.CaptureQuality.DP_QUALITY_NO_FINGER => "No finger detected - place finger firmly on scanner",
                        Constants.CaptureQuality.DP_QUALITY_FAKE_FINGER => "Invalid finger detected - use a real finger",
                        Constants.CaptureQuality.DP_QUALITY_FINGER_TOO_LEFT => "Move finger to the right and center on scanner",
                        Constants.CaptureQuality.DP_QUALITY_FINGER_TOO_RIGHT => "Move finger to the left and center on scanner",
                        Constants.CaptureQuality.DP_QUALITY_FINGER_TOO_HIGH => "Move finger down and center on scanner",
                        Constants.CaptureQuality.DP_QUALITY_FINGER_TOO_LOW => "Move finger up and center on scanner",
                        Constants.CaptureQuality.DP_QUALITY_FINGER_OFF_CENTER => "Center finger on scanner and press firmly",
                        Constants.CaptureQuality.DP_QUALITY_SCAN_SKEWED => "Keep finger straight and aligned on scanner",
                        Constants.CaptureQuality.DP_QUALITY_SCAN_TOO_SHORT => "Press finger down longer on scanner",
                        Constants.CaptureQuality.DP_QUALITY_SCAN_TOO_LONG => "Lift finger sooner after placing on scanner",
                        Constants.CaptureQuality.DP_QUALITY_SCAN_TOO_SLOW => "Place finger more quickly on scanner",
                        Constants.CaptureQuality.DP_QUALITY_SCAN_TOO_FAST => "Place finger more slowly and hold steady",
                        Constants.CaptureQuality.DP_QUALITY_SCAN_WRONG_DIRECTION => "Place finger vertically on scanner",
                        Constants.CaptureQuality.DP_QUALITY_READER_DIRTY => "Clean scanner surface and try again",
                        _ => $"Poor quality capture - clean finger and scanner, then try again"
                    };
                    
                    _logger.Warning("Capture quality issue: {Quality} - {Message}", captureResult.Quality, qualityMessage);
                    StatusChanged?.Invoke(this, $"{qualityMessage}. Try again.");
                    return false;
                }

                _logger.Information("Capture quality validation passed: Quality={Quality}", 
                    captureResult.Quality);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error validating capture result");
                StatusChanged?.Invoke(this, "Error validating capture");
                return false;
            }
        }

        /// <summary>
        /// Check if the reader is a U.are.U 4500 device
        /// </summary>
        private bool IsUareU4500Device(Reader reader)
        {
            // Check both Product name and Name field for device identification
            string productName = reader?.Description?.Id?.ProductName;
            string deviceName = reader?.Description?.Name;
            
            if (string.IsNullOrEmpty(productName) && string.IsNullOrEmpty(deviceName))
            {
                _logger.Warning("Reader has null product name and device name, skipping");
                return false;
            }

            // Primary check: Product name (more reliable for device identification)
            if (!string.IsNullOrEmpty(productName))
            {
                string lowerProductName = productName.ToLowerInvariant();
                bool hasUareU = lowerProductName.Contains("u.are.u");
                bool has4500 = lowerProductName.Contains("4500");
                bool isMatch = hasUareU && has4500;
                
                _logger.Information("Product '{ProductName}' -> '{LowerName}': hasUareU={HasUareU}, has4500={Has4500}, isMatch={IsMatch}", 
                    productName, lowerProductName, hasUareU, has4500, isMatch);
                
                if (isMatch) return true;
            }

            // Fallback check: Device name
            if (!string.IsNullOrEmpty(deviceName))
            {
                string lowerDeviceName = deviceName.ToLowerInvariant();
                bool hasUareU = lowerDeviceName.Contains("u.are.u");
                bool has4500 = lowerDeviceName.Contains("4500");
                bool isMatch = hasUareU && has4500;
                
                _logger.Information("Device Name '{DeviceName}' -> '{LowerName}': hasUareU={HasUareU}, has4500={Has4500}, isMatch={IsMatch}", 
                    deviceName, lowerDeviceName, hasUareU, has4500, isMatch);
                
                return isMatch;
            }
            
            return false;
        }

        /// <summary>
        /// Improved multi-capture enrollment with proper device state management
        /// </summary>
        public async Task<EnrollmentResult> EnrollFingerprintImprovedAsync(int requiredScans = 4, int maxRetries = 3)
        {
            if (!_isInitialized || _currentReader == null)
            {
                var error = "SDK not initialized or no reader available";
                _logger.Error(error);
                return EnrollmentResult.Failure(error);
            }

            var capturedTemplates = new List<Fmd>();
            var captureAttempts = 0;
            var retryCount = 0;

            try
            {
                _logger.Information("Starting improved multi-capture enrollment - {RequiredScans} scans required", requiredScans);
                StatusChanged?.Invoke(this, $"Starting enrollment - {requiredScans} scans required");

                while (capturedTemplates.Count < requiredScans && retryCount < maxRetries)
                {
                    try
                    {
                        captureAttempts++;
                        _logger.Information("Capture attempt {Attempt} for scan {ScanNumber}/{RequiredScans}", 
                            captureAttempts, capturedTemplates.Count + 1, requiredScans);

                        StatusChanged?.Invoke(this, $"Place finger on scanner - Scan {capturedTemplates.Count + 1}/{requiredScans}");

                        // Ensure device is ready before capture
                        await EnsureDeviceReadyAsync();

                        // Capture fingerprint with timeout
                        var captureResult = await CaptureAsync();

                        if (captureResult.ResultCode == Constants.ResultCode.DP_SUCCESS)
                        {
                            _logger.Information("Capture successful - Quality: {Quality}", captureResult.Quality);
                            
                            // Convert to template
                            var fmdResult = FeatureExtraction.CreateFmdFromFid(captureResult.Data, Constants.Formats.Fmd.ANSI);
                            if (fmdResult.ResultCode == Constants.ResultCode.DP_SUCCESS)
                            {
                                capturedTemplates.Add(fmdResult.Data);
                                StatusChanged?.Invoke(this, $"Scan {capturedTemplates.Count}/{requiredScans} captured successfully");
                                
                                // Reset retry count on successful capture
                                retryCount = 0;

                                // Enhanced cleanup and waiting between captures
                                if (capturedTemplates.Count < requiredScans)
                                {
                                    StatusChanged?.Invoke(this, "Remove finger and wait...");
                                    
                                    // Explicit device cleanup to prevent busy state
                                    try
                                    {
                                        _logger.Information("Performing device cleanup between captures");
                                        
                                        // Cancel any pending operations
                                        var cancelResult = _currentReader.CancelCapture();
                                        if (cancelResult == Constants.ResultCode.DP_SUCCESS)
                                        {
                                            _logger.Information("Previous capture operation cancelled successfully");
                                        }
                                        
                                        // Wait longer for device to reset
                                        await Task.Delay(3000); // Increased to 3 seconds
                                        
                                        // Verify device is ready before next capture
                                        await EnsureDeviceReadyAsync();
                                    }
                                    catch (Exception cleanupEx)
                                    {
                                        _logger.Warning(cleanupEx, "Error during device cleanup - continuing anyway");
                                        await Task.Delay(2000); // Fallback delay
                                    }
                                }
                            }
                            else
                            {
                                _logger.Warning("Failed to create template from capture: {Result}", fmdResult.ResultCode);
                                StatusChanged?.Invoke(this, "Failed to process fingerprint - try again");
                                await Task.Delay(1000);
                            }
                        }
                        else if (captureResult.ResultCode == Constants.ResultCode.DP_DEVICE_BUSY)
                        {
                            _logger.Warning("Device busy on attempt {Attempt} - waiting before retry", captureAttempts);
                            StatusChanged?.Invoke(this, "Device busy - waiting...");
                            await Task.Delay(2000); // 2 second delay for device busy
                            retryCount++;
                        }
                        else
                        {
                            _logger.Warning("Capture failed: {Result}", captureResult.ResultCode);
                            StatusChanged?.Invoke(this, $"Capture failed: {captureResult.ResultCode} - try again");
                            await Task.Delay(1000);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, "Error during capture attempt {Attempt}", captureAttempts);
                        retryCount++;
                        if (retryCount < maxRetries)
                        {
                            StatusChanged?.Invoke(this, "Error occurred - retrying...");
                            await Task.Delay(2000);
                        }
                    }
                }

                if (capturedTemplates.Count >= requiredScans)
                {
                    _logger.Information("Captured {Count} templates. Creating enrollment FMD...", capturedTemplates.Count);
                    StatusChanged?.Invoke(this, "Creating enrollment template...");

                    // Create enrollment FMD from captured templates
                    var enrollmentFmd = CreateEnrollmentFmd(capturedTemplates);
                    
                    if (enrollmentFmd != null)
                    {
                        var enrollmentTemplate = Convert.ToBase64String(enrollmentFmd.Bytes);
                        _logger.Information("Enrollment FMD created successfully");
                        StatusChanged?.Invoke(this, "Enrollment completed successfully!");
                        return EnrollmentResult.Success(enrollmentTemplate, capturedTemplates.Count);
                    }
                    else
                    {
                         var error = "Failed to create enrollment FMD from captured templates";
                        _logger.Error(error);
                        StatusChanged?.Invoke(this, "Enrollment failed - could not create template");
                        return EnrollmentResult.Failure(error);
                    }
                }
                else
                {
                    var error = $"Failed to capture required number of scans. Got {capturedTemplates.Count}/{requiredScans} after {retryCount} retries";
                    _logger.Error(error);
                    StatusChanged?.Invoke(this, "Enrollment failed - insufficient scans");
                    return EnrollmentResult.Failure(error);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during improved enrollment");
                StatusChanged?.Invoke(this, "Enrollment failed due to error");
                return EnrollmentResult.Failure($"Enrollment failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Create an enrollment FMD from a list of captured FMDs
        /// </summary>
        private Fmd CreateEnrollmentFmd(List<Fmd> fmds)
        {
            try
            {
                if (fmds == null || fmds.Count == 0)
                {
                    _logger.Warning("No FMDs provided for enrollment");
                    return null;
                }

                // Use DPUruNet.Enrollment to create the enrollment FMD
                DataResult<Fmd> resultEnrollment = DPUruNet.Enrollment.CreateEnrollmentFmd(Constants.Formats.Fmd.ANSI, fmds);

                if (resultEnrollment.ResultCode == Constants.ResultCode.DP_SUCCESS)
                {
                    return resultEnrollment.Data;
                }
                else
                {
                    _logger.Error("Failed to create enrollment FMD: {ResultCode}", resultEnrollment.ResultCode);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error creating enrollment FMD");
                return null;
            }
        }

        /// <summary>
        /// Ensures the device is ready for capture by checking status and waiting if necessary
        /// </summary>
        private async Task EnsureDeviceReadyAsync()
        {
            if (_currentReader == null) return;

            try
            {
                // Check if device is currently capturing
                if (_isCapturing)
                {
                    _logger.Information("Device is currently capturing - waiting for completion");
                    await Task.Delay(2000); // Increased wait time
                }

                // Wait for device to become ready with retry logic
                const int maxRetries = 10;
                const int retryDelayMs = 500;
                
                for (int retry = 0; retry < maxRetries; retry++)
                {
                    var statusResult = _currentReader.GetStatus();
                    
                    if (statusResult == Constants.ResultCode.DP_SUCCESS)
                    {
                        var deviceStatus = _currentReader.Status.Status;
                        
                        if (deviceStatus == Constants.ReaderStatuses.DP_STATUS_READY)
                        {
                            _logger.Information("Device is ready for capture");
                            return; // Device is ready
                        }
                        
                        _logger.Information("Device not ready (Status: {Status}) - waiting (attempt {Retry}/{MaxRetries})", 
                            deviceStatus, retry + 1, maxRetries);
                        
                        // Progressive delay - longer waits for later retries
                        int delayMs = retryDelayMs * (retry + 1);
                        await Task.Delay(delayMs);
                    }
                    else
                    {
                        _logger.Warning("Failed to get device status: {StatusResult} - waiting (attempt {Retry}/{MaxRetries})", 
                            statusResult, retry + 1, maxRetries);
                        await Task.Delay(retryDelayMs);
                    }
                }
                
                _logger.Warning("Device did not become ready after {MaxRetries} attempts", maxRetries);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error checking device status");
            }
        }

        /// <summary>
        /// Synchronous capture method for enrollment
        /// </summary>
        private async Task<CaptureResult> CaptureAsync()
        {
            var tcs = new TaskCompletionSource<CaptureResult>();
            CaptureResult result = null;

            try
            {
                // Set up capture handler
                void OnCaptureComplete(CaptureResult captureResult)
                {
                    result = captureResult;
                    tcs.SetResult(captureResult);
                }

                _currentReader.On_Captured += OnCaptureComplete;

                // Start capture
                var captureResult = _currentReader.CaptureAsync(
                    Constants.Formats.Fid.ANSI,
                    Constants.CaptureProcessing.DP_IMG_PROC_DEFAULT,
                    _currentReader.Capabilities.Resolutions[0]);

                if (captureResult != Constants.ResultCode.DP_SUCCESS)
                 {
                     _currentReader.On_Captured -= OnCaptureComplete;
                     return new CaptureResult(captureResult, Constants.CaptureQuality.DP_QUALITY_CANCELED, 0, null);
                 }

                 // Wait for capture with timeout
                 var timeoutTask = Task.Delay(10000); // 10 second timeout
                 var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

                 _currentReader.On_Captured -= OnCaptureComplete;

                 if (completedTask == timeoutTask)
                 {
                     _logger.Warning("Capture timed out - cancelling operation");
                     _currentReader.CancelCapture();
                     return new CaptureResult(Constants.ResultCode.DP_DEVICE_FAILURE, Constants.CaptureQuality.DP_QUALITY_CANCELED, 0, null);
                 }

                 // Explicit cleanup after successful capture
                 if (result != null && result.ResultCode == Constants.ResultCode.DP_SUCCESS)
                 {
                     _logger.Information("Capture completed successfully - performing cleanup");
                     
                     // Small delay to ensure capture is fully processed
                     await Task.Delay(200);
                     
                     // Reset capturing state
                     _isCapturing = false;
                 }

                 return result;
             }
             catch (Exception ex)
             {
                 _logger.Error(ex, "Error during capture");
                 return new CaptureResult(Constants.ResultCode.DP_DEVICE_FAILURE, Constants.CaptureQuality.DP_QUALITY_CANCELED, 0, null);
             }
        }

        // SDK Constants for proper threshold calculation
        private const int DPFJ_PROBABILITY_ONE = 0x7fffffff;
        
        /// <summary>
        /// Convert SDK CaptureQuality to numerical score following SDK best practices
        /// </summary>
        /// <param name="quality">SDK capture quality enum</param>
        /// <returns>Quality score from 0-100</returns>
        private int ConvertSdkQualityToScore(Constants.CaptureQuality quality)
        {
            return quality switch
            {
                Constants.CaptureQuality.DP_QUALITY_GOOD => 85,
                Constants.CaptureQuality.DP_QUALITY_TIMED_OUT => 20,
                Constants.CaptureQuality.DP_QUALITY_CANCELED => 0,
                Constants.CaptureQuality.DP_QUALITY_NO_FINGER => 0,
                Constants.CaptureQuality.DP_QUALITY_FAKE_FINGER => 0,
                Constants.CaptureQuality.DP_QUALITY_FINGER_TOO_LEFT => 40,
                Constants.CaptureQuality.DP_QUALITY_FINGER_TOO_RIGHT => 40,
                Constants.CaptureQuality.DP_QUALITY_FINGER_TOO_HIGH => 40,
                Constants.CaptureQuality.DP_QUALITY_FINGER_TOO_LOW => 40,
                Constants.CaptureQuality.DP_QUALITY_FINGER_OFF_CENTER => 45,
                Constants.CaptureQuality.DP_QUALITY_SCAN_SKEWED => 50,
                Constants.CaptureQuality.DP_QUALITY_SCAN_TOO_SHORT => 35,
                Constants.CaptureQuality.DP_QUALITY_SCAN_TOO_LONG => 55,
                Constants.CaptureQuality.DP_QUALITY_SCAN_TOO_SLOW => 50,
                Constants.CaptureQuality.DP_QUALITY_SCAN_TOO_FAST => 45,
                Constants.CaptureQuality.DP_QUALITY_SCAN_WRONG_DIRECTION => 40,
                Constants.CaptureQuality.DP_QUALITY_READER_DIRTY => 25,
                _ => 30 // Default for unknown quality issues
            };
        }
        
        /// <summary>
        /// Check capture quality based on SDK standards
        /// </summary>
        /// <param name="captureResult">The capture result to validate</param>
        /// <returns>True if quality is acceptable, false otherwise</returns>
        public bool CheckCaptureQuality(CaptureResult captureResult)
        {
            if (captureResult.Data == null || captureResult.ResultCode != Constants.ResultCode.DP_SUCCESS)
            {
                if (captureResult.ResultCode != Constants.ResultCode.DP_SUCCESS)
                {
                    _logger.Warning("Capture failed with result: {Result}", captureResult.ResultCode);
                    return false;
                }

                // Check for fake finger or other quality issues
                if (captureResult.Quality != Constants.CaptureQuality.DP_QUALITY_CANCELED)
                {
                    _logger.Warning("Poor capture quality: {Quality}", captureResult.Quality);
                    return false;
                }
                return false;
            }

            return true;
        }

        /// <summary>
        /// Compare two fingerprint templates using DigitalPersona SDK with proper threshold
        /// </summary>
        /// <param name="storedTemplate">Base64 encoded stored template</param>
        /// <param name="capturedTemplate">Captured template as byte array</param>
        /// <returns>True if templates match, false otherwise</returns>
        public Task<bool> CompareTemplatesAsync(string storedTemplate, string capturedTemplate)
        {
            try
            {
                if (string.IsNullOrEmpty(storedTemplate) || string.IsNullOrEmpty(capturedTemplate))
                {
                    _logger.Warning("Invalid templates provided for comparison");
                    return Task.FromResult(false);
                }

                // Convert both templates from base64 to byte arrays
                byte[] storedBytes;
                byte[] capturedBytes;
                
                try
                {
                    storedBytes = Convert.FromBase64String(storedTemplate);
                    capturedBytes = Convert.FromBase64String(capturedTemplate);
                }
                catch (FormatException ex)
                {
                    _logger.Warning(ex, "Failed to decode templates from base64");
                    return Task.FromResult(false);
                }

                // Validate template sizes before attempting to create FMD objects
                // Valid ANSI templates are typically 400+ bytes, corrupted templates are much smaller
                const int MIN_VALID_TEMPLATE_SIZE = 200; // Minimum size for a valid ANSI template
                
                if (storedBytes.Length < MIN_VALID_TEMPLATE_SIZE)
                {
                    _logger.Warning($"Stored template appears corrupted (size: {storedBytes.Length} bytes, minimum expected: {MIN_VALID_TEMPLATE_SIZE} bytes). Skipping comparison.");
                    return Task.FromResult(false);
                }
                
                if (capturedBytes.Length < MIN_VALID_TEMPLATE_SIZE)
                {
                    _logger.Warning($"Captured template appears corrupted (size: {capturedBytes.Length} bytes, minimum expected: {MIN_VALID_TEMPLATE_SIZE} bytes). Skipping comparison.");
                    return Task.FromResult(false);
                }

                // Create FMD objects directly from byte arrays since both are already in ANSI format
                Fmd storedFmd;
                Fmd capturedFmd;
                
                // Debug information
                _logger.Debug($"Stored template byte array size: {storedBytes.Length}");
                _logger.Debug($"Captured template byte array size: {capturedBytes.Length}");
                _logger.Debug($"ANSI format constant: {(int)Constants.Formats.Fmd.ANSI}");
                _logger.Debug($"Wrapper version: {Constants.WRAPPER_VERSION}");
                
                try
                {
                    storedFmd = new Fmd(storedBytes, (int)Constants.Formats.Fmd.ANSI, Constants.WRAPPER_VERSION);
                    capturedFmd = new Fmd(capturedBytes, (int)Constants.Formats.Fmd.ANSI, Constants.WRAPPER_VERSION);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to create FMD objects from byte arrays");
                    return Task.FromResult(false);
                }

                // Perform comparison using DigitalPersona SDK
                CompareResult compareResult = Comparison.Compare(storedFmd, 0, capturedFmd, 0);
                
                if (compareResult.ResultCode != Constants.ResultCode.DP_SUCCESS)
                {
                    _logger.Warning("Template comparison failed with result: {Result}", compareResult.ResultCode);
                    return Task.FromResult(false);
                }

                // Use proper SDK threshold calculation as per DigitalPersona documentation
                // This is the same threshold used in the official SDK samples
                int thresholdScore = DPFJ_PROBABILITY_ONE * 1 / 100000; // Approximately 21,474
                bool isMatch = compareResult.Score < thresholdScore;

                _logger.Information("Template comparison completed - Score: {Score}, Threshold: {Threshold}, Match: {IsMatch}", 
                    compareResult.Score, thresholdScore, isMatch);

                return Task.FromResult(isMatch);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error comparing fingerprint templates");
                return Task.FromResult(false);
            }
        }

        /// <summary>
        /// Perform 1:N identification using DigitalPersona SDK's optimized Identify method
        /// This is the proper way to handle large datasets as per SDK documentation
        /// </summary>
        /// <param name="capturedTemplate">Base64 encoded captured template</param>
        /// <param name="storedTemplates">Dictionary of employee name to base64 encoded templates</param>
        /// <returns>Employee name if match found, null otherwise</returns>
        public Task<string?> IdentifyTemplateAsync(string capturedTemplate, Dictionary<string, string> storedTemplates)
        {
            try
            {
                if (string.IsNullOrEmpty(capturedTemplate) || storedTemplates == null || storedTemplates.Count == 0)
                {
                    _logger.Warning("Invalid parameters for identification");
                    return Task.FromResult<string?>(null);
                }

                // Convert captured template
                byte[] capturedBytes;
                try
                {
                    capturedBytes = Convert.FromBase64String(capturedTemplate);
                }
                catch (FormatException ex)
                {
                    _logger.Warning(ex, "Failed to decode captured template from base64");
                    return Task.FromResult<string?>(null);
                }

                // Validate captured template size
                const int MIN_VALID_TEMPLATE_SIZE = 200;
                if (capturedBytes.Length < MIN_VALID_TEMPLATE_SIZE)
                {
                    _logger.Warning($"Captured template appears corrupted (size: {capturedBytes.Length} bytes)");
                    return Task.FromResult<string?>(null);
                }

                // Create captured FMD
                Fmd capturedFmd;
                try
                {
                    capturedFmd = new Fmd(capturedBytes, (int)Constants.Formats.Fmd.ANSI, Constants.WRAPPER_VERSION);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to create FMD from captured template");
                    return Task.FromResult<string?>(null);
                }

                // Convert stored templates to FMD array and track employee names with caching optimization
                var validTemplates = new List<Fmd>();
                var employeeNames = new List<string>();

                foreach (var kvp in storedTemplates)
                {
                    try
                    {
                        if (string.IsNullOrEmpty(kvp.Value))
                            continue;

                        // Check cache first for performance optimization
                        Fmd storedFmd;
                        string cacheKey = $"{kvp.Key}_{kvp.Value.GetHashCode()}";
                        
                        lock (_cacheLock)
                        {
                            if (_templateCache.TryGetValue(cacheKey, out storedFmd))
                            {
                                validTemplates.Add(storedFmd);
                                employeeNames.Add(kvp.Key);
                                continue;
                            }
                        }

                        // Template not in cache, process it
                        byte[] storedBytes = Convert.FromBase64String(kvp.Value);
                        
                        if (storedBytes.Length < MIN_VALID_TEMPLATE_SIZE)
                        {
                            _logger.Debug($"Skipping corrupted template for employee {kvp.Key} (size: {storedBytes.Length} bytes)");
                            continue;
                        }

                        storedFmd = new Fmd(storedBytes, (int)Constants.Formats.Fmd.ANSI, Constants.WRAPPER_VERSION);
                        
                        // Cache the processed template for future use
                        lock (_cacheLock)
                        {
                            if (!_templateCache.ContainsKey(cacheKey))
                            {
                                _templateCache[cacheKey] = storedFmd;
                                
                                // Prevent cache from growing too large (limit to 1000 templates)
                                if (_templateCache.Count > 1000)
                                {
                                    var oldestKey = _templateCache.Keys.First();
                                    _templateCache.Remove(oldestKey);
                                }
                            }
                        }
                        
                        validTemplates.Add(storedFmd);
                        employeeNames.Add(kvp.Key);
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, $"Failed to process template for employee {kvp.Key}");
                        continue;
                    }
                }

                if (validTemplates.Count == 0)
                {
                    _logger.Warning("No valid templates found for identification");
                    return Task.FromResult<string?>(null);
                }

                // Use SDK's optimized Identify method with proper threshold
                int thresholdScore = DPFJ_PROBABILITY_ONE * 1 / 100000; // Same as SDK sample
                
                _logger.Information($"Starting identification against {validTemplates.Count} templates with threshold {thresholdScore}");

                IdentifyResult identifyResult = Comparison.Identify(capturedFmd, 0, validTemplates.ToArray(), thresholdScore, validTemplates.Count);
                
                if (identifyResult.ResultCode != Constants.ResultCode.DP_SUCCESS)
                {
                    _logger.Warning("Identification failed with result: {Result}", identifyResult.ResultCode);
                    return Task.FromResult<string?>(null);
                }

                if (identifyResult.Indexes == null || identifyResult.Indexes.Length == 0)
                {
                    _logger.Information("No matches found during identification");
                    return Task.FromResult<string?>(null);
                }

                // Return the first match (best match)
                int[][] indexes = identifyResult.Indexes;
                int matchIndex = indexes[0][0];
                string matchedEmployeeName = employeeNames[matchIndex];
                
                _logger.Information($"Identification successful - Found {identifyResult.Indexes.Length} matches, best match: Employee {matchedEmployeeName}");
                
                return Task.FromResult<string?>(matchedEmployeeName);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during fingerprint identification");
                return Task.FromResult<string?>(null);
            }
        }

        /// <summary>
        /// Calculate NFIQ quality score for the captured fingerprint
        /// </summary>
        private int CalculateNFIQQuality(Fid fingerprintData)
        {
            try
            {
                // Use DigitalPersona SDK's NFIQ quality assessment
                var qualityScore = Quality.NfiqFid(fingerprintData, 0, QualityAlgorithm.QUALITY_NFIQ_NIST);
                
                _logger.Information("NFIQ quality assessment successful: Score={Score}", qualityScore);
                return qualityScore;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error calculating NFIQ quality score");
                return -1; // Indicate failure
            }
        }

        /// <summary>
        /// Get template quality score from the FMD
        /// </summary>
        private int GetTemplateQuality(Fmd template)
        {
            try
            {
                // Access the quality property from the first view in the FMD
                if (template?.Views != null && template.Views.Count > 0)
                {
                    var firstView = template.Views[0];
                    int quality = firstView.Quality;
                    _logger.Information("Template quality extracted: {Quality}", quality);
                    return quality;
                }
                else
                {
                    _logger.Warning("Template quality not available in FMD - no views found");
                    return -1; // Indicate unavailable
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error extracting template quality");
                return -1; // Indicate failure
            }
        }

        /// <summary>
        /// Generate quality feedback message based on various quality metrics
        /// </summary>
        private string GetQualityFeedback(int captureScore, int nfiqScore, int templateQuality)
        {
            try
            {
                var feedback = new List<string>();
                
                // Analyze capture score (higher is generally better)
                if (captureScore > 80)
                {
                    feedback.Add("Excellent capture quality");
                }
                else if (captureScore > 60)
                {
                    feedback.Add("Good capture quality");
                }
                else if (captureScore > 40)
                {
                    feedback.Add("Fair capture quality");
                }
                else
                {
                    feedback.Add("Poor capture quality");
                }
                
                // Analyze NFIQ score (1-5 scale, 1 is best)
                if (nfiqScore > 0)
                {
                    string nfiqFeedback = nfiqScore switch
                    {
                        1 => "Excellent NFIQ quality",
                        2 => "Good NFIQ quality", 
                        3 => "Fair NFIQ quality",
                        4 => "Poor NFIQ quality",
                        5 => "Very poor NFIQ quality",
                        _ => $"NFIQ score: {nfiqScore}"
                    };
                    feedback.Add(nfiqFeedback);
                }
                
                // Analyze template quality
                if (templateQuality > 0)
                {
                    if (templateQuality > 80)
                    {
                        feedback.Add("High template quality");
                    }
                    else if (templateQuality > 60)
                    {
                        feedback.Add("Good template quality");
                    }
                    else
                    {
                        feedback.Add("Low template quality");
                    }
                }
                
                // Provide overall assessment and recommendations
                string overallFeedback = "Fingerprint captured successfully";
                
                if (captureScore < 50 || nfiqScore > 3 || templateQuality < 50)
                {
                    overallFeedback += " - Consider recapturing for better quality";
                }
                else if (captureScore > 70 && nfiqScore <= 2 && templateQuality > 70)
                {
                    overallFeedback += " - Excellent quality for verification";
                }
                
                return $"{overallFeedback}. {string.Join(", ", feedback)}.";
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error generating quality feedback");
                return "Fingerprint captured successfully";
            }
        }

        /// <summary>
        /// Enhanced template comparison with quality-adaptive scoring
        /// </summary>
        /// <param name="storedTemplate">Base64 encoded stored template</param>
        /// <param name="capturedTemplate">Captured template as byte array</param>
        /// <param name="captureQuality">Quality score of the captured template</param>
        /// <returns>Comparison result with score and match status</returns>
        public Task<(int score, bool isMatch)> CompareTemplatesWithQualityAsync(string storedTemplate, string capturedTemplate, int captureQuality)
        {
            try
            {
                if (string.IsNullOrEmpty(storedTemplate) || string.IsNullOrEmpty(capturedTemplate))
                {
                    _logger.Warning("Invalid templates provided for quality comparison");
                    return Task.FromResult((int.MaxValue, false));
                }

                // Convert both templates from base64 to byte arrays
                byte[] storedBytes;
                byte[] capturedBytes;
                
                try
                {
                    storedBytes = Convert.FromBase64String(storedTemplate);
                    capturedBytes = Convert.FromBase64String(capturedTemplate);
                }
                catch (FormatException ex)
                {
                    _logger.Warning(ex, "Failed to decode templates from base64 for quality comparison");
                    return Task.FromResult((int.MaxValue, false));
                }

                // Validate template sizes
                const int MIN_VALID_TEMPLATE_SIZE = 200;
                
                if (storedBytes.Length < MIN_VALID_TEMPLATE_SIZE || capturedBytes.Length < MIN_VALID_TEMPLATE_SIZE)
                {
                    _logger.Warning("Template size validation failed for quality comparison - Stored: {StoredSize}, Captured: {CapturedSize}", 
                        storedBytes.Length, capturedBytes.Length);
                    return Task.FromResult((int.MaxValue, false));
                }

                // Create FMD objects
                Fmd storedFmd;
                Fmd capturedFmd;
                
                try
                {
                    storedFmd = new Fmd(storedBytes, (int)Constants.Formats.Fmd.ANSI, Constants.WRAPPER_VERSION);
                    capturedFmd = new Fmd(capturedBytes, (int)Constants.Formats.Fmd.ANSI, Constants.WRAPPER_VERSION);
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to create FMD objects for quality comparison");
                    return Task.FromResult((int.MaxValue, false));
                }

                // Perform comparison using DigitalPersona SDK
                CompareResult compareResult = Comparison.Compare(storedFmd, 0, capturedFmd, 0);
                
                if (compareResult.ResultCode != Constants.ResultCode.DP_SUCCESS)
                {
                    _logger.Warning("Quality-enhanced template comparison failed with result: {Result}", compareResult.ResultCode);
                    return Task.FromResult((int.MaxValue, false));
                }

                // Quality-adaptive threshold calculation
                int baseThreshold = 75000; // Standard threshold
                int qualityAdjustment = CalculateQualityAdjustment(captureQuality);
                int adaptiveThreshold = baseThreshold + qualityAdjustment;
                
                bool isMatch = compareResult.Score < adaptiveThreshold;

                _logger.Information("Quality-enhanced comparison - Score: {Score}, Base Threshold: {BaseThreshold}, " +
                    "Quality: {Quality}, Adjustment: {Adjustment}, Final Threshold: {FinalThreshold}, Match: {IsMatch}", 
                    compareResult.Score, baseThreshold, captureQuality, qualityAdjustment, adaptiveThreshold, isMatch);

                return Task.FromResult((compareResult.Score, isMatch));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in quality-enhanced template comparison");
                return Task.FromResult((int.MaxValue, false));
            }
        }

        /// <summary>
        /// Calculate quality-based threshold adjustment
        /// </summary>
        /// <param name="captureQuality">Quality score (0-100)</param>
        /// <returns>Threshold adjustment value</returns>
        private int CalculateQualityAdjustment(int captureQuality)
        {
            // Higher quality captures can use stricter thresholds (negative adjustment)
            // Lower quality captures need more lenient thresholds (positive adjustment)
            
            return captureQuality switch
            {
                >= 90 => -15000,  // Excellent quality - stricter threshold
                >= 80 => -10000,  // Very good quality - moderately stricter
                >= 70 => -5000,   // Good quality - slightly stricter
                >= 60 => 0,       // Acceptable quality - standard threshold
                >= 50 => 10000,   // Fair quality - more lenient
                >= 40 => 20000,   // Poor quality - much more lenient
                _ => 30000        // Very poor quality - very lenient
            };
        }

        /// <summary>
        /// Clear the template cache to free memory
        /// </summary>
        public void ClearTemplateCache()
        {
            lock (_cacheLock)
            {
                _templateCache.Clear();
                _logger.Information("Template cache cleared - freed memory for {Count} cached templates", _templateCache.Count);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            try
            {
                if (_isCapturing && _currentReader != null)
                {
                    _currentReader.On_Captured -= OnCaptured;
                    _isCapturing = false;
                }

                if (_currentReader != null)
                {
                    _currentReader.Dispose();
                    _currentReader = null;
                }

                // Clear template cache
                lock (_cacheLock)
                {
                    _templateCache.Clear();
                }

                _readers = null;
                _isInitialized = false;
                _isCapturing = false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error disposing DigitalPersona SDK");
            }
            finally
            {
                _disposed = true;
            }
        }
    }
}