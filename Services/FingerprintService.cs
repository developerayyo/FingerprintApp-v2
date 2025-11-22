using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using ERPNextFingerprintApp.Models;

namespace ERPNextFingerprintApp.Services
{
    public class FingerprintService : IDisposable
    {
        private readonly ConcurrentDictionary<string, string> _fingerprintCache;
        private readonly Config _config;
        private readonly DigitalPersonaSDK _digitalPersonaSDK;
        private readonly DatabaseService _databaseService;
        private bool _disposed = false;
        private bool _isInitialized = false;
        private bool _isInitializing = false;
        private TaskCompletionSource<FingerprintCaptureResult>? _captureTaskSource;
        private string _lastSDKStatus = string.Empty;
        private readonly object _initializationLock = new object();
        private const int MinQualityThreshold = 60;
        private const int MaxRetries = 3;

        // Events for fingerprint operations
        public event EventHandler<FingerprintCapturedEventArgs>? FingerprintCaptured;
        public event EventHandler<FingerprintVerifiedEventArgs>? FingerprintVerified;
        public event EventHandler<string>? ErrorOccurred;
        public event EventHandler<EnrollmentProgress>? EnrollmentProgressChanged;
        public event EventHandler<string>? StatusChanged;

        // Public properties for status checking
        public bool IsInitialized => _isInitialized;
        public bool IsInitializing => _isInitializing;

        public FingerprintService(Config config, DatabaseService databaseService)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
            _fingerprintCache = new ConcurrentDictionary<string, string>();
            _digitalPersonaSDK = new DigitalPersonaSDK(Log.Logger);
            
            // Subscribe to DigitalPersona SDK events
            _digitalPersonaSDK.FingerprintCaptured += OnFingerprintCaptured;
            _digitalPersonaSDK.StatusChanged += OnStatusChanged;
        }

        public async Task<bool> InitializeAsync()
        {
            lock (_initializationLock)
            {
                if (_isInitialized)
                {
                    Log.Information("Fingerprint service already initialized");
                    return true;
                }
                
                if (_isInitializing)
                {
                    Log.Warning("Fingerprint service initialization already in progress");
                    return false;
                }
                
                _isInitializing = true;
            }

            try
            {
                Log.Information("[FINGERPRINT_SERVICE] Starting DigitalPersona fingerprint service initialization");
                LoggerService.LogServiceOperation("FingerprintService", "Initialization Started", true, "Beginning SDK and device initialization");
                
                _isInitialized = false;
                
                LoggerService.LogServiceOperation("FingerprintService", "Environment Check", true, 
                    $"OS: {Environment.OSVersion}, Architecture: {Environment.Is64BitOperatingSystem}, Process: {Environment.Is64BitProcess}");
                
                await LogSDKInstallationStatus();
                
                Log.Information("[FINGERPRINT_SERVICE] Attempting to initialize DigitalPersona SDK");
                bool sdkInitialized = await _digitalPersonaSDK.InitializeAsync();
                
                if (!sdkInitialized)
                {
                    string lastStatus = GetLastSDKStatus();
                    Log.Error("[FINGERPRINT_SERVICE] Failed to initialize DigitalPersona SDK - Status: {LastStatus}", lastStatus);
                    LoggerService.LogServiceOperation("FingerprintService", "SDK Initialization", false, $"SDK initialization failed: {lastStatus}");
                    
                    if (lastStatus.Contains("not installed"))
                    {
                        ErrorOccurred?.Invoke(this, "DigitalPersona SDK is not installed. Please install the U.are.U SDK from DigitalPersona.");
                        LoggerService.LogServiceOperation("FingerprintService", "Error Analysis", false, "SDK not installed - DLL files missing");
                    }
                    else if (lastStatus.Contains("corrupted"))
                    {
                        ErrorOccurred?.Invoke(this, "DigitalPersona SDK installation is corrupted. Please reinstall the U.are.U SDK.");
                        LoggerService.LogServiceOperation("FingerprintService", "Error Analysis", false, "SDK installation corrupted");
                    }
                    else if (lastStatus.Contains("device not found"))
                    {
                        ErrorOccurred?.Invoke(this, "No compatible fingerprint device found. Please connect a U.are.U 4500 device.");
                        LoggerService.LogServiceOperation("FingerprintService", "Error Analysis", false, "No compatible device found");
                    }
                    else
                    {
                        ErrorOccurred?.Invoke(this, "Failed to initialize DigitalPersona SDK. Please check device connection and SDK installation.");
                        LoggerService.LogServiceOperation("FingerprintService", "Error Analysis", false, $"General initialization failure: {lastStatus}");
                    }
                    return false;
                }
                
                Log.Information("[FINGERPRINT_SERVICE] DigitalPersona SDK initialized successfully");
                LoggerService.LogServiceOperation("FingerprintService", "SDK Initialization", true, "SDK initialized successfully");
                
                Log.Information("[FINGERPRINT_SERVICE] Checking device connection status");
                bool deviceConnected = await _digitalPersonaSDK.CheckDeviceStatusAsync();
                
                if (deviceConnected)
                {
                    Log.Information("[FINGERPRINT_SERVICE] DigitalPersona device connected and ready");
                    LoggerService.LogServiceOperation("FingerprintService", "Device Check", true, "Device connected and ready for operations");
                }
                else
                {
                    Log.Warning("[FINGERPRINT_SERVICE] No DigitalPersona device found, but SDK initialized");
                    LoggerService.LogServiceOperation("FingerprintService", "Device Check", false, "SDK initialized but no device detected");
                }
                
                await LogDetailedServiceStatus();
                
                _isInitialized = true;
                Log.Information("[FINGERPRINT_SERVICE] DigitalPersona fingerprint service initialized successfully - SDK: {SdkStatus}, Device: {DeviceStatus}", 
                    "Initialized", deviceConnected ? "Connected" : "Not Connected");
                LoggerService.LogServiceOperation("FingerprintService", "Initialization Completed", true, 
                    $"Service ready - SDK: Initialized, Device: {(deviceConnected ? "Connected" : "Not Connected")}");
                
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to initialize fingerprint service");
                ErrorOccurred?.Invoke(this, $"Initialization failed: {ex.Message}");
                _isInitialized = false;
                return false;
            }
            finally
            {
                lock (_initializationLock)
                {
                    _isInitializing = false;
                }
            }
        }

        private string GetLastSDKStatus()
        {
            return _lastSDKStatus;
        }

        public async Task<string> GetDetailedStatusAsync()
        {
            var status = new StringBuilder();
            status.AppendLine($"Service Initialized: {_isInitialized}");
            status.AppendLine($"Service Initializing: {_isInitializing}");
            status.AppendLine($"Last SDK Status: {_lastSDKStatus}");
            
            if (_isInitialized)
            {
                bool deviceAvailable = await _digitalPersonaSDK.CheckDeviceStatusAsync();
                status.AppendLine($"Device Available: {deviceAvailable}");
            }
            
            return status.ToString();
        }

        public async Task<FingerprintCaptureResult> CaptureAsync()
        {
            if (!_isInitialized)
            {
                Log.Warning("Fingerprint service not initialized, attempting to initialize...");
                bool initialized = await InitializeAsync();
                if (!initialized)
                {
                    var error = "Fingerprint service not initialized and re-initialization failed";
                    Log.Error(error);
                    return FingerprintCaptureResult.Failure(error);
                }
            }

            try
            {
                Log.Information("Starting DigitalPersona fingerprint capture");
                
                Log.Information("Performing enhanced device cleanup before capture");
                await _digitalPersonaSDK.StopCaptureAsync();
                
                await ResetCaptureStateAsync();
                
                await Task.Delay(200);
                
                _captureTaskSource = new TaskCompletionSource<FingerprintCaptureResult>();
                
                bool captureStarted = await _digitalPersonaSDK.StartCaptureAsync();
                if (!captureStarted)
                {
                    var error = "Failed to start fingerprint capture";
                    Log.Error(error);
                    ErrorOccurred?.Invoke(this, error);
                    await ResetCaptureStateAsync();
                    return FingerprintCaptureResult.Failure(error);
                }
                
                var timeoutTask = Task.Delay(10000);
                var completedTask = await Task.WhenAny(_captureTaskSource.Task, timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    await _digitalPersonaSDK.StopCaptureAsync();
                    var error = "Fingerprint capture timeout";
                    Log.Warning(error);
                    ErrorOccurred?.Invoke(this, error);
                    await ResetCaptureStateAsync();
                    return FingerprintCaptureResult.Failure(error);
                }
                
                var result = await _captureTaskSource.Task;
                await ResetCaptureStateAsync();
                return result;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Fingerprint capture failed");
                LoggerService.LogFingerprintCapture(false, errorMessage: ex.Message);
                ErrorOccurred?.Invoke(this, $"Capture failed: {ex.Message}");
                await ResetCaptureStateAsync();
                return FingerprintCaptureResult.Failure(ex.Message);
            }
        }

        private void OnFingerprintCaptured(object? sender, FingerprintCaptureEventArgs e)
        {
            try
            {
                if (_captureTaskSource == null) return;
                if (_captureTaskSource.Task.IsCompleted) return;

                if (e.FingerprintData != null && e.FingerprintData.Length > 0)
                {
                    try
                    {
                        if (e.QualityScore < MinQualityThreshold)
                        {
                            var error = $"Poor fingerprint quality (Score: {e.QualityScore}): {e.QualityFeedback}. Please try again.";
                            Log.Warning("Fingerprint quality below threshold: {Score} < {Threshold}. Feedback: {Feedback}", 
                                e.QualityScore, MinQualityThreshold, e.QualityFeedback);
                            LoggerService.LogFingerprintCapture(false, errorMessage: error);
                            ErrorOccurred?.Invoke(this, error);
                            _captureTaskSource.TrySetResult(FingerprintCaptureResult.Failure(error));
                            return;
                        }

                        string template = Convert.ToBase64String(e.FingerprintData);
                        var result = FingerprintCaptureResult.Success(template, e.QualityScore, e.QualityFeedback);
                        
                        Log.Information("Fingerprint captured successfully with quality score: {Score}, Feedback: {Feedback}", 
                            e.QualityScore, e.QualityFeedback);
                        LoggerService.LogFingerprintCapture(true);
                        FingerprintCaptured?.Invoke(this, new FingerprintCapturedEventArgs(template));
                        
                        _captureTaskSource.TrySetResult(result);
                    }
                    catch (Exception qualityEx)
                    {
                        Log.Warning(qualityEx, "Fingerprint capture quality validation failed");
                        var error = $"Error processing fingerprint: {qualityEx.Message}. Please try again.";
                        LoggerService.LogFingerprintCapture(false, errorMessage: error);
                        ErrorOccurred?.Invoke(this, error);
                        _captureTaskSource.TrySetResult(FingerprintCaptureResult.Failure(error));
                    }
                }
                else
                {
                    var error = "Failed to capture fingerprint template";
                    LoggerService.LogFingerprintCapture(false, errorMessage: error);
                    ErrorOccurred?.Invoke(this, error);
                    _captureTaskSource.TrySetResult(FingerprintCaptureResult.Failure(error));
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error processing fingerprint capture event");
                _captureTaskSource?.TrySetResult(FingerprintCaptureResult.Failure(ex.Message));
            }
        }

        private void OnStatusChanged(object? sender, string status)
        {
            _lastSDKStatus = status;
            Log.Information("DigitalPersona device status changed: {Status}", status);
        }

        public async Task<FingerprintVerificationResult> VerifyAsync(IEnumerable<Employee> employees)
        {
            try
            {
                Log.Information("Starting enhanced fingerprint verification against {Count} employees", employees.Count());
                
                var employeesWithFingerprints = employees
                    .Where(e => !string.IsNullOrEmpty(e.FingerprintTemplate))
                    .ToList();
                
                if (!employeesWithFingerprints.Any())
                {
                    var error = "No employees with registered fingerprints found";
                    LoggerService.LogFingerprintVerification(false, null, error);
                    return FingerprintVerificationResult.Failure(error);
                }

                FingerprintCaptureResult bestCaptureResult = null;
                int bestQualityScore = 0;
                
                for (int attempt = 1; attempt <= MaxRetries; attempt++)
                {
                    Log.Information("Verification attempt {Attempt}/{MaxRetries}", attempt, MaxRetries);
                    
                    var captureResult = await CaptureAsync();
                    if (!captureResult.IsSuccess)
                    {
                        if (attempt == MaxRetries)
                        {
                            return FingerprintVerificationResult.Failure($"Capture failed after {MaxRetries} attempts: {captureResult.ErrorMessage}");
                        }
                        await Task.Delay(1500);
                        continue;
                    }

                    int currentQualityScore = ExtractQualityScore(captureResult);
                    
                    if (bestCaptureResult == null || currentQualityScore > bestQualityScore)
                    {
                        bestCaptureResult = captureResult;
                        bestQualityScore = currentQualityScore;
                    }
                    
                    if (currentQualityScore >= MinQualityThreshold)
                    {
                        break;
                    }
                    
                    if (attempt < MaxRetries)
                    {
                        string qualityFeedback = GetQualityFeedback(currentQualityScore);
                        StatusChanged?.Invoke(this, $"Quality too low (attempt {attempt}/{MaxRetries}). {qualityFeedback}");
                        await Task.Delay(2000);
                    }
                }
                
                if (bestCaptureResult == null)
                {
                    return FingerprintVerificationResult.Failure("Failed to capture fingerprint after multiple attempts");
                }
                
                var matchResults = new List<(Employee employee, int score, bool isMatch)>();
                
                foreach (var employee in employeesWithFingerprints)
                {
                    try
                    {
                        var comparisonResult = await _digitalPersonaSDK.CompareTemplatesWithQualityAsync(
                            employee.FingerprintTemplate, 
                            bestCaptureResult.Template,
                            bestQualityScore);
                        
                        matchResults.Add((employee, comparisonResult.score, comparisonResult.isMatch));
                        
                        if (comparisonResult.isMatch)
                        {
                            Log.Information("Fingerprint match found for employee: {EmployeeName}", employee.Name);
                            LoggerService.LogFingerprintVerification(true, employee.Name);
                            FingerprintVerified?.Invoke(this, new FingerprintVerifiedEventArgs(employee));
                            return FingerprintVerificationResult.Success(employee);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Error verifying fingerprint for employee {EmployeeName}", employee.Name);
                    }
                }
                
                var bestMatch = matchResults.OrderBy(r => r.score).FirstOrDefault();
                string detailedError = bestMatch.employee != null 
                    ? $"No matching fingerprint found. Best match: {bestMatch.employee.EmployeeName} (Score: {bestMatch.score})."
                    : "No matching fingerprint found.";
                
                LoggerService.LogFingerprintVerification(false, errorMessage: detailedError);
                return FingerprintVerificationResult.Failure(detailedError);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Enhanced fingerprint verification failed");
                LoggerService.LogFingerprintVerification(false, errorMessage: ex.Message);
                ErrorOccurred?.Invoke(this, $"Verification failed: {ex.Message}");
                return FingerprintVerificationResult.Failure(ex.Message);
            }
        }

        public async Task<FingerprintVerificationResult> VerifyFastAsync(IEnumerable<Employee> employees)
        {
            try
            {
                var employeesWithFingerprints = employees.Where(e => !string.IsNullOrEmpty(e.FingerprintTemplate)).ToList();
                
                if (!employeesWithFingerprints.Any())
                {
                    return FingerprintVerificationResult.Failure("No employees with registered fingerprints found");
                }

                var captureResult = await CaptureAsync();
                if (!captureResult.IsSuccess)
                {
                    return FingerprintVerificationResult.Failure($"Capture failed: {captureResult.ErrorMessage}");
                }

                foreach (var employee in employeesWithFingerprints)
                {
                    try
                    {
                        var isMatch = await _digitalPersonaSDK.CompareTemplatesAsync(employee.FingerprintTemplate, captureResult.Template);
                        
                        if (isMatch)
                        {
                            LoggerService.LogFingerprintVerification(true, employee.Name);
                            FingerprintVerified?.Invoke(this, new FingerprintVerifiedEventArgs(employee));
                            return FingerprintVerificationResult.Success(employee);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Error verifying fingerprint for employee {EmployeeName}", employee.Name);
                    }
                }
                
                return FingerprintVerificationResult.Failure("No matching fingerprint found");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Fast fingerprint verification failed");
                return FingerprintVerificationResult.Failure(ex.Message);
            }
        }

        public async Task<FingerprintVerificationResult> VerifyWithIdentificationAsync(List<Employee> employees, string capturedTemplate)
        {
            try
            {
                if (employees == null || employees.Count == 0) return FingerprintVerificationResult.Failure("No employees provided");
                if (string.IsNullOrEmpty(capturedTemplate)) return FingerprintVerificationResult.Failure("No captured template provided");

                var templateDictionary = new Dictionary<string, string>();
                foreach (var employee in employees)
                {
                    if (!string.IsNullOrEmpty(employee.FingerprintTemplate))
                    {
                        templateDictionary[employee.Name] = employee.FingerprintTemplate;
                    }
                }
                
                if (templateDictionary.Count == 0) return FingerprintVerificationResult.Failure("No employees with fingerprint templates found");

                string? matchedEmployeeName = await _digitalPersonaSDK.IdentifyTemplateAsync(capturedTemplate, templateDictionary);
                
                if (!string.IsNullOrEmpty(matchedEmployeeName))
                {
                    var matchedEmployee = employees.FirstOrDefault(e => e.Name == matchedEmployeeName);
                    if (matchedEmployee != null)
                    {
                        LoggerService.LogFingerprintVerification(true, matchedEmployee.Name);
                        FingerprintVerified?.Invoke(this, new FingerprintVerifiedEventArgs(matchedEmployee));
                        return FingerprintVerificationResult.Success(matchedEmployee);
                    }
                }

                LoggerService.LogFingerprintVerification(false, errorMessage: "No match found");
                return FingerprintVerificationResult.Failure("Fingerprint verification failed - no match found");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during SDK fingerprint identification");
                return FingerprintVerificationResult.Failure($"Identification error: {ex.Message}");
            }
        }

        /// <summary>
        /// Verify fingerprint against local SQLite database (Offline Mode)
        /// </summary>
        public async Task<FingerprintVerificationResult> VerifyAgainstLocalDbAsync(string capturedTemplate)
        {
            try
            {
                Log.Information("Starting verification against local database");
                
                // Get active employees from local DB
                var activeEmployees = await _databaseService.GetActiveEmployeesAsync();
                
                if (!activeEmployees.Any())
                {
                    return FingerprintVerificationResult.Failure("No active employees found in local database");
                }

                // Convert EmployeeEntity to Employee model
                var employees = activeEmployees.Select(e => new Employee
                {
                    Name = e.Name, // ID
                    EmployeeName = e.EmployeeName,
                    Department = e.Department,
                    Designation = e.Designation,
                    FingerprintTemplate = e.FingerprintTemplate
                }).ToList();

                // Use existing verification logic
                return await VerifyWithIdentificationAsync(employees, capturedTemplate);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error verifying against local database");
                return FingerprintVerificationResult.Failure($"Local verification failed: {ex.Message}");
            }
        }

        private int ExtractQualityScore(FingerprintCaptureResult captureResult)
        {
            if (captureResult?.QualityScore > 0) return captureResult.QualityScore;
            if (captureResult?.Template == null) return 0;
            return Math.Min(100, Math.Max(0, 50 + (captureResult.Template.Length / 50)));
        }

        private string GetQualityFeedback(int qualityScore, string sdkFeedback = "")
        {
            if (!string.IsNullOrEmpty(sdkFeedback)) return sdkFeedback;
            return qualityScore switch
            {
                < 30 => "Very poor quality. Clean finger and scanner, press firmly.",
                < 50 => "Poor quality. Ensure finger is clean and dry, center on scanner.",
                < 70 => "Fair quality. Try repositioning finger or pressing more firmly.",
                < 85 => "Good quality. Small adjustment may improve recognition.",
                _ => "Excellent quality."
            };
        }

        public void CacheFingerprint(string employeeId, string template)
        {
            if (_config.FingerprintCacheEnabled)
            {
                _fingerprintCache.TryAdd(employeeId, template);
            }
        }

        public async Task ResetServiceStateAsync()
        {
            try
            {
                await ResetCaptureStateAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error resetting fingerprint service state");
            }
        }

        public DeviceInfo GetDeviceInfo()
        {
            return _digitalPersonaSDK.GetDeviceInfo();
        }

        private async Task LogSDKInstallationStatus()
        {
            // Simplified for brevity, logic preserved in principle
            try
            {
                string[] possiblePaths = {
                    @"C:\Program Files\DigitalPersona\U.are.U SDK\Windows\Lib\.NET\DPUruNet.dll",
                    @"C:\Program Files (x86)\DigitalPersona\U.are.U SDK\Windows\Lib\.NET\DPUruNet.dll"
                };

                bool sdkFound = possiblePaths.Any(System.IO.File.Exists);
                if (!sdkFound) await CheckAlternativeSDKLocations();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error checking SDK installation status");
            }
        }

        private async Task CheckAlternativeSDKLocations()
        {
            // Simplified
            await CheckRegistryForSDK();
        }

        private async Task CheckRegistryForSDK()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\DigitalPersona"))
                {
                    if (key != null) Log.Information("DigitalPersona registry key found");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error checking registry for SDK");
            }
        }

        private async Task LogDetailedServiceStatus()
        {
            try
            {
                var deviceInfo = GetDeviceInfo();
                Log.Information("Service Status - Initialized: {Initialized}, Device: {DeviceName}", _isInitialized, deviceInfo.DeviceName);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error logging detailed service status");
            }
        }

        private async Task ResetCaptureStateAsync()
        {
            try
            {
                if (_captureTaskSource != null && !_captureTaskSource.Task.IsCompleted)
                {
                    _captureTaskSource.TrySetCanceled();
                }
                _captureTaskSource = null;
                await _digitalPersonaSDK.StopCaptureAsync();
                await Task.Delay(100);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error during capture state reset");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _digitalPersonaSDK?.Dispose();
                _fingerprintCache.Clear();
                _disposed = true;
            }
        }

        public async Task<FingerprintEnrollmentResult> EnrollFingerprintAsync(int requiredScans = 4, int qualityThreshold = 70)
        {
            if (!_isInitialized) return FingerprintEnrollmentResult.Failure("Fingerprint service not initialized", 0);

            var templates = new List<string>();
            var scanCount = 0;
            var maxRetries = 2;

            try
            {
                for (int i = 0; i < requiredScans; i++)
                {
                    var retryCount = 0;
                    bool scanSuccessful = false;

                    while (!scanSuccessful && retryCount < maxRetries)
                    {
                        var progress = new EnrollmentProgress
                        {
                            CurrentScan = i + 1,
                            TotalScans = requiredScans,
                            QualityPercentage = CalculateQualityPercentage(templates.Count, i + 1),
                            Message = $"Place finger for scan {i + 1} of {requiredScans}",
                            IsCompleted = false
                        };
                        EnrollmentProgressChanged?.Invoke(this, progress);

                        var captureResult = await this.CaptureAsync();
                        
                        if (captureResult.IsSuccess)
                        {
                            templates.Add(captureResult.Template);
                            scanCount++;
                            scanSuccessful = true;
                            await Task.Delay(2000);
                        }
                        else
                        {
                            retryCount++;
                            if (retryCount >= maxRetries) return FingerprintEnrollmentResult.Failure($"Failed to capture scan {i + 1}", scanCount);
                            await Task.Delay(2000);
                        }
                    }
                }

                var enrollmentTemplate = CreateEnrollmentTemplate(templates);
                
                var finalProgress = new EnrollmentProgress
                {
                    CurrentScan = requiredScans,
                    TotalScans = requiredScans,
                    QualityPercentage = 100,
                    Message = "Enrollment completed successfully!",
                    IsCompleted = true
                };
                EnrollmentProgressChanged?.Invoke(this, finalProgress);

                return FingerprintEnrollmentResult.Success(enrollmentTemplate, scanCount, 100);
            }
            catch (Exception ex)
            {
                return FingerprintEnrollmentResult.Failure($"Enrollment failed: {ex.Message}", scanCount);
            }
        }

        private string CreateEnrollmentTemplate(List<string> templates)
        {
            if (templates == null || templates.Count == 0) throw new InvalidOperationException("No templates available");
            return templates.First();
        }

        private int CalculateQualityPercentage(int completedScans, int currentScan)
        {
            return Math.Min(100, 60 + (completedScans * 10) + (currentScan * 5));
        }

        public async Task<FingerprintEnrollmentResult> EnrollFingerprintWithControlAsync(CancellationToken cancellationToken = default)
        {
            if (!_isInitialized) return FingerprintEnrollmentResult.Failure("Fingerprint service not initialized", 0);

            var currentScan = 0;
            const int totalScans = 4;

            try
            {
                EventHandler<string> statusHandler = (sender, status) =>
                {
                    if (status.Contains("Place finger on scanner - Scan"))
                    {
                        var parts = status.Split(' ');
                        for (int i = 0; i < parts.Length; i++)
                        {
                            if (parts[i] == "Scan" && i + 1 < parts.Length)
                            {
                                var scanInfo = parts[i + 1];
                                if (scanInfo.Contains('/'))
                                {
                                    var scanParts = scanInfo.Split('/');
                                    if (scanParts.Length == 2 && int.TryParse(scanParts[0], out int scanNum))
                                    {
                                        currentScan = scanNum;
                                        var progress = new EnrollmentProgress
                                        {
                                            CurrentScan = currentScan,
                                            TotalScans = totalScans,
                                            QualityPercentage = (int)((double)currentScan / totalScans * 100),
                                            Message = status,
                                            IsCompleted = false
                                        };
                                        EnrollmentProgressChanged?.Invoke(this, progress);
                                    }
                                }
                                break;
                            }
                        }
                    }
                    else if (status.Contains("captured successfully"))
                    {
                        var progress = new EnrollmentProgress
                        {
                            CurrentScan = currentScan,
                            TotalScans = totalScans,
                            QualityPercentage = (int)((double)currentScan / totalScans * 100),
                            Message = status,
                            IsCompleted = false
                        };
                        EnrollmentProgressChanged?.Invoke(this, progress);
                    }
                    else if (status.Contains("Enrollment completed successfully"))
                    {
                        var progress = new EnrollmentProgress
                        {
                            CurrentScan = totalScans,
                            TotalScans = totalScans,
                            QualityPercentage = 100,
                            Message = "Enrollment completed successfully!",
                            IsCompleted = true
                        };
                        EnrollmentProgressChanged?.Invoke(this, progress);
                    }
                };

                _digitalPersonaSDK.StatusChanged += statusHandler;

                try
                {
                    var initialProgress = new EnrollmentProgress
                    {
                        CurrentScan = 0,
                        TotalScans = totalScans,
                        QualityPercentage = 0,
                        Message = "Starting enrollment...",
                        IsCompleted = false
                    };
                    EnrollmentProgressChanged?.Invoke(this, initialProgress);

                    var enrollmentResult = await _digitalPersonaSDK.EnrollFingerprintImprovedAsync(4, 3);

                    if (enrollmentResult.IsSuccess)
                    {
                        var finalProgress = new EnrollmentProgress
                        {
                            CurrentScan = enrollmentResult.CapturedScans,
                            TotalScans = totalScans,
                            QualityPercentage = 100,
                            Message = "Enrollment completed successfully!",
                            IsCompleted = true
                        };
                        EnrollmentProgressChanged?.Invoke(this, finalProgress);

                        return FingerprintEnrollmentResult.Success(
                            enrollmentResult.Template ?? string.Empty, 
                            enrollmentResult.CapturedScans, 
                            100);
                    }
                    else
                    {
                        var failureProgress = new EnrollmentProgress
                        {
                            CurrentScan = enrollmentResult.CapturedScans,
                            TotalScans = totalScans,
                            QualityPercentage = 0,
                            Message = "Enrollment failed",
                            IsCompleted = true
                        };
                        EnrollmentProgressChanged?.Invoke(this, failureProgress);

                        return FingerprintEnrollmentResult.Failure(
                            enrollmentResult.ErrorMessage ?? "Unknown enrollment error", 
                            enrollmentResult.CapturedScans);
                    }
                }
                finally
                {
                    _digitalPersonaSDK.StatusChanged -= statusHandler;
                }
            }
            catch (Exception ex)
            {
                return FingerprintEnrollmentResult.Failure($"Enrollment failed: {ex.Message}", 0);
            }
        }
    }

    public class FingerprintCaptureResult
    {
        public bool IsSuccess { get; private set; }
        public string Template { get; private set; } = string.Empty;
        public string ErrorMessage { get; private set; } = string.Empty;
        public int QualityScore { get; private set; }
        public string QualityFeedback { get; private set; } = string.Empty;

        private FingerprintCaptureResult() { }

        public static FingerprintCaptureResult Success(string template, int qualityScore = 0, string qualityFeedback = "")
        {
            return new FingerprintCaptureResult
            {
                IsSuccess = true,
                Template = template,
                QualityScore = qualityScore,
                QualityFeedback = qualityFeedback
            };
        }

        public static FingerprintCaptureResult Failure(string errorMessage)
        {
            return new FingerprintCaptureResult
            {
                IsSuccess = false,
                ErrorMessage = errorMessage
            };
        }
    }

    public class FingerprintVerificationResult
    {
        public bool IsSuccess { get; private set; }
        public Employee? MatchedEmployee { get; private set; }
        public string ErrorMessage { get; private set; } = string.Empty;

        private FingerprintVerificationResult() { }

        public static FingerprintVerificationResult Success(Employee employee)
        {
            return new FingerprintVerificationResult
            {
                IsSuccess = true,
                MatchedEmployee = employee
            };
        }

        public static FingerprintVerificationResult Failure(string errorMessage)
        {
            return new FingerprintVerificationResult
            {
                IsSuccess = false,
                ErrorMessage = errorMessage
            };
        }
    }

    public class FingerprintCapturedEventArgs : EventArgs
    {
        public string Template { get; }

        public FingerprintCapturedEventArgs(string template)
        {
            Template = template;
        }
    }

    public class FingerprintVerifiedEventArgs : EventArgs
    {
        public Employee Employee { get; }

        public FingerprintVerifiedEventArgs(Employee employee)
        {
            Employee = employee;
        }
    }

    public class EnrollmentProgress
    {
        public int CurrentScan { get; set; }
        public int TotalScans { get; set; }
        public int QualityPercentage { get; set; }
        public string Message { get; set; } = string.Empty;
        public bool IsCompleted { get; set; }
        
        public double ProgressPercentage => TotalScans > 0 ? (double)CurrentScan / TotalScans * 100 : 0;
    }

    public class FingerprintEnrollmentResult
    {
        public bool IsSuccess { get; private set; }
        public string Template { get; private set; } = string.Empty;
        public int ScanCount { get; private set; }
        public int ScansCompleted { get; private set; }
        public int QualityPercentage { get; private set; }
        public string ErrorMessage { get; private set; } = string.Empty;

        private FingerprintEnrollmentResult() { }

        public static FingerprintEnrollmentResult Success(string template, int scanCount, int qualityPercentage = 100)
        {
            return new FingerprintEnrollmentResult
            {
                IsSuccess = true,
                Template = template,
                ScanCount = scanCount,
                ScansCompleted = scanCount,
                QualityPercentage = qualityPercentage
            };
        }

        public static FingerprintEnrollmentResult Failure(string errorMessage, int scanCount = 0)
        {
            return new FingerprintEnrollmentResult
            {
                IsSuccess = false,
                ErrorMessage = errorMessage,
                ScanCount = scanCount
            };
        }
    }
}