using System;
using System.IO;
using Serilog;
using Serilog.Events;
using ERPNextFingerprintApp.Models;
using System.Diagnostics;
using System.Reflection;

namespace ERPNextFingerprintApp.Services
{
    public static class LoggerService
    {
        private static readonly string _sessionId = Guid.NewGuid().ToString("N")[..8];
        
        public static void Initialize(Config config)
        {
            try
            {
                // Ensure log directory exists
                var logDirectory = Path.GetDirectoryName(config.LogPath);
                if (!string.IsNullOrEmpty(logDirectory) && !Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }

                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                    .Enrich.FromLogContext()
                    .Enrich.WithProperty("Application", "ERPNext Fingerprint App")
                    .Enrich.WithProperty("SessionId", _sessionId)
                    .Enrich.WithProperty("ProcessId", Process.GetCurrentProcess().Id)
                    .WriteTo.Console(
                        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{SessionId}] {Message:lj}{NewLine}{Exception}")
                    .WriteTo.File(
                        config.LogPath,
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 30,
                        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] [{SessionId}] {Message:lj}{NewLine}{Exception}")
                    .CreateLogger();

                var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";
                Log.Information("=== APPLICATION STARTUP ===");
                Log.Information("Logger initialized successfully. Version: {Version}, Session: {SessionId}, PID: {ProcessId}", 
                    version, _sessionId, Process.GetCurrentProcess().Id);
                Log.Information("Log file: {LogPath}", config.LogPath);
                Log.Information("Working Directory: {WorkingDirectory}", Environment.CurrentDirectory);
                Log.Information("OS: {OS}, .NET: {DotNetVersion}", Environment.OSVersion, Environment.Version);
            }
            catch (Exception ex)
            {
                // Fallback to console logging if file logging fails
                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .Enrich.WithProperty("SessionId", _sessionId)
                    .WriteTo.Console()
                    .CreateLogger();

                Log.Error(ex, "Failed to initialize file logging. Using console logging only.");
            }
        }

        // Application lifecycle logging
        public static void LogApplicationStart()
        {
            Log.Information("=== APPLICATION STARTING ===");
            Log.Information("Application startup initiated at {StartTime}", DateTime.Now);
        }

        public static void LogApplicationReady()
        {
            Log.Information("=== APPLICATION READY ===");
            Log.Information("Application is fully initialized and ready for use");
        }

        public static void LogApplicationShutdown(string reason = "Normal shutdown")
        {
            Log.Information("=== APPLICATION SHUTDOWN ===");
            Log.Information("Application shutdown initiated. Reason: {Reason}", reason);
        }

        // UI lifecycle logging
        public static void LogWindowEvent(string windowName, string eventName, string? details = null)
        {
            Log.Debug("Window Event: {WindowName} - {EventName} {Details}", 
                windowName, eventName, details != null ? $"({details})" : "");
        }

        public static void LogUserAction(string action, string? details = null)
        {
            Log.Information("User Action: {Action} {Details}", 
                action, details != null ? $"- {details}" : "");
        }

        // Service lifecycle logging
        public static void LogServiceInitialization(string serviceName, bool success, string? errorMessage = null)
        {
            if (success)
            {
                Log.Information("Service initialized: {ServiceName}", serviceName);
            }
            else
            {
                Log.Error("Service initialization failed: {ServiceName}. Error: {ErrorMessage}", 
                    serviceName, errorMessage ?? "Unknown error");
            }
        }

        public static void LogServiceOperation(string serviceName, string operation, bool success, string? details = null)
        {
            if (success)
            {
                Log.Debug("Service operation: {ServiceName}.{Operation} {Details}", 
                    serviceName, operation, details != null ? $"- {details}" : "");
            }
            else
            {
                Log.Warning("Service operation failed: {ServiceName}.{Operation} {Details}", 
                    serviceName, operation, details != null ? $"- {details}" : "");
            }
        }

        // Exception logging with context
        public static void LogException(Exception ex, string context, object? additionalData = null)
        {
            Log.Error(ex, "Exception in {Context}. Additional data: {@AdditionalData}", context, additionalData);
        }

        public static void LogCriticalError(Exception ex, string context, string? action = null)
        {
            Log.Fatal(ex, "CRITICAL ERROR in {Context}. Recommended action: {Action}", 
                context, action ?? "Restart application");
        }

        // Performance logging
        public static void LogPerformance(string operation, TimeSpan duration, bool isSlowOperation = false)
        {
            var level = isSlowOperation ? LogEventLevel.Warning : LogEventLevel.Debug;
            Log.Write(level, "Performance: {Operation} completed in {Duration}ms {SlowFlag}", 
                operation, duration.TotalMilliseconds, isSlowOperation ? "(SLOW)" : "");
        }

        // Configuration logging
        public static void LogConfigurationLoaded(string configPath, bool success, string? errorMessage = null)
        {
            if (success)
            {
                Log.Information("Configuration loaded successfully from: {ConfigPath}", configPath);
            }
            else
            {
                Log.Error("Configuration loading failed from: {ConfigPath}. Error: {ErrorMessage}", 
                    configPath, errorMessage ?? "Unknown error");
            }
        }

        public static void LogFingerprintCapture(bool success, string? employeeId = null, string? errorMessage = null)
        {
            if (success)
            {
                Log.Information("Fingerprint captured successfully for employee: {EmployeeId}", employeeId ?? "Unknown");
            }
            else
            {
                Log.Warning("Fingerprint capture failed for employee: {EmployeeId}. Error: {ErrorMessage}", 
                    employeeId ?? "Unknown", errorMessage ?? "Unknown error");
            }
        }

        public static void LogFingerprintVerification(bool success, string? employeeId = null, string? errorMessage = null)
        {
            if (success)
            {
                Log.Information("Fingerprint verification successful for employee: {EmployeeId}", employeeId ?? "Unknown");
            }
            else
            {
                Log.Warning("Fingerprint verification failed. Employee: {EmployeeId}, Error: {ErrorMessage}", 
                    employeeId ?? "Unknown", errorMessage ?? "No match found");
            }
        }

        public static void LogApiCall(string endpoint, string method, bool success, string? errorMessage = null)
        {
            if (success)
            {
                Log.Information("API call successful: {Method} {Endpoint}", method, endpoint);
            }
            else
            {
                Log.Error("API call failed: {Method} {Endpoint}. Error: {ErrorMessage}", 
                    method, endpoint, errorMessage ?? "Unknown error");
            }
        }

        public static void LogDeductionProcessing(DeductionRecord deduction, bool success, string? errorMessage = null)
        {
            if (success)
            {
                Log.Information("Deduction processed successfully: Employee {Employee}, Type {DeductionType}, Amount {Amount}", 
                    deduction.Employee, deduction.DeductionType, deduction.Amount);
            }
            else
            {
                Log.Error("Deduction processing failed: Employee {Employee}, Type {DeductionType}, Amount {Amount}. Error: {ErrorMessage}", 
                    deduction.Employee, deduction.DeductionType, deduction.Amount, errorMessage ?? "Unknown error");
            }
        }

        public static void Shutdown()
        {
            Log.Information("=== LOGGER SHUTDOWN ===");
            Log.Information("Application session {SessionId} ended at {EndTime}", _sessionId, DateTime.Now);
            Log.CloseAndFlush();
        }
    }
}