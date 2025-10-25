using System;
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using ERPNextFingerprintApp.Models;
using ERPNextFingerprintApp.Services;
using ERPNextFingerprintApp.ViewModels;
using ERPNextFingerprintApp.Utils;
using ERPNextFingerprintApp.Views;
using System.Diagnostics;

namespace ERPNextFingerprintApp
{
    public partial class App : Application
    {
        private IHost? _host;
        public static IServiceProvider ServiceProvider { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            var stopwatch = Stopwatch.StartNew();
            
            // Set up global exception handlers
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            
            try
            {
                Console.WriteLine("=== APPLICATION STARTUP INITIATED ===");
                Console.WriteLine($"Startup time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                Console.WriteLine($"Process ID: {Process.GetCurrentProcess().Id}");
                Console.WriteLine($"Working Directory: {Environment.CurrentDirectory}");
                
                // Load configuration first
                Console.WriteLine("Loading configuration...");
                var config = LoadConfiguration();
                Console.WriteLine($"Configuration loaded successfully from: {Path.GetFullPath("config.json")}");

                // Initialize logging with enhanced configuration
                Console.WriteLine("Initializing logging system...");
                LoggerService.Initialize(config);
                LoggerService.LogApplicationStart();
                
                Log.Information("Configuration details: ERPNext URL: {Url}, Log Path: {LogPath}", 
                    config.ErpUrl, config.LogPath);

                // Build dependency injection host
                Log.Information("Building dependency injection host...");
                Console.WriteLine("Building dependency injection host...");
                _host = CreateHostBuilder(config).Build();
                Log.Information("Dependency injection host built successfully");

                // Configure services
                Log.Information("Configuring services...");
                Console.WriteLine("Configuring services...");
                ConfigureServices(_host.Services, config);
                Log.Information("Services configured successfully");

                // Start the host
                Log.Information("Starting application host...");
                Console.WriteLine("Starting application host...");
                _host.Start();
                Log.Information("Application host started successfully");

                // Set service provider
                ServiceProvider = _host.Services;
                Log.Information("Service provider set successfully");

                // Create and show login window
                Log.Information("Creating login window...");
                Console.WriteLine("Creating login window...");
                var loginWindow = ServiceProvider.GetRequiredService<LoginWindow>();
                Log.Information("Login window created successfully");
                
                LoggerService.LogWindowEvent("LoginWindow", "Created");
                
                Console.WriteLine("Showing login window...");
                loginWindow.Show();
                LoggerService.LogWindowEvent("LoginWindow", "Shown");
                
                Log.Information("Application startup completed successfully in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
                LoggerService.LogApplicationReady();
                Console.WriteLine($"=== APPLICATION STARTUP COMPLETED in {stopwatch.ElapsedMilliseconds}ms ===");

                base.OnStartup(e);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                var errorMessage = $"Application startup failed after {stopwatch.ElapsedMilliseconds}ms: {ex.Message}";
                
                Console.WriteLine($"=== STARTUP ERROR ===");
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine($"Stack Trace: {ex.StackTrace}");
                
                if (Log.Logger != null)
                {
                    LoggerService.LogCriticalError(ex, "Application Startup", "Check configuration and dependencies");
                }
                
                MessageBox.Show(errorMessage, "Startup Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                
                LoggerService.LogApplicationShutdown("Startup failure");
                Shutdown(1);
            }
        }

        private Config LoadConfiguration()
        {
            try
            {
                var configPath = Path.Combine(Environment.CurrentDirectory, "config.json");
                Console.WriteLine($"Loading configuration from: {configPath}");
                
                if (!File.Exists(configPath))
                {
                    throw new FileNotFoundException($"Configuration file not found: {configPath}");
                }

                var config = JsonHelper.LoadConfig(configPath);
                LoggerService.LogConfigurationLoaded(configPath, true);
                return config;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Configuration loading failed: {ex.Message}");
                LoggerService.LogConfigurationLoaded("config.json", false, ex.Message);
                throw;
            }
        }

        private static IHostBuilder CreateHostBuilder(Config config) =>
            Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    // Register configuration
                    services.AddSingleton(config);

                    // Register services
                    services.AddSingleton<ERPNextApiService>();
                    services.AddSingleton<FingerprintService>();

                    // Register ViewModels
                    services.AddTransient<RegistrationViewModel>();
                    services.AddTransient<VerificationViewModel>();

                    // Register Windows
                    services.AddTransient<LoginWindow>();
                    services.AddTransient<MainWindow>();
                });

        private static void ConfigureServices(IServiceProvider services, Config config)
        {
            try
            {
                // Initialize ERPNext API Service
                Log.Information("Initializing ERPNext API Service...");
                var apiService = services.GetRequiredService<ERPNextApiService>();
                LoggerService.LogServiceInitialization("ERPNextApiService", true);

                // Initialize Fingerprint Service
                Log.Information("Initializing Fingerprint Service...");
                var fingerprintService = services.GetRequiredService<FingerprintService>();
                LoggerService.LogServiceInitialization("FingerprintService", true);

                Log.Information("All services initialized successfully");
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "Service Configuration");
                throw;
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                var reason = e.ApplicationExitCode == 0 ? "Normal shutdown" : $"Exit code: {e.ApplicationExitCode}";
                LoggerService.LogApplicationShutdown(reason);
                
                Log.Information("Application exit initiated. Exit code: {ExitCode}", e.ApplicationExitCode);
                LoggerService.Shutdown();
                _host?.Dispose();
                
                Console.WriteLine($"=== APPLICATION SHUTDOWN COMPLETED ===");
                Console.WriteLine($"Exit code: {e.ApplicationExitCode}");
            }
            catch (Exception ex)
            {
                // Log to console as Serilog might be disposed
                Console.WriteLine($"Error during shutdown: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            finally
            {
                base.OnExit(e);
            }
        }

        // Global exception handlers
        protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
        {
            LoggerService.LogApplicationShutdown($"Session ending: {e.ReasonSessionEnding}");
            base.OnSessionEnding(e);
        }

        private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            LoggerService.LogCriticalError(e.Exception, "Unhandled Dispatcher Exception");
            
            var result = MessageBox.Show(
                $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nWould you like to continue running the application?",
                "Unexpected Error",
                MessageBoxButton.YesNo,
                MessageBoxImage.Error);

            if (result == MessageBoxResult.No)
            {
                LoggerService.LogApplicationShutdown("User chose to exit after unhandled exception");
                Shutdown(1);
            }
            else
            {
                e.Handled = true;
                Log.Information("User chose to continue after unhandled exception");
            }
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;
            var message = exception?.Message ?? "Unknown error";
            var stackTrace = exception?.StackTrace ?? "No stack trace available";
            
            Console.WriteLine($"=== UNHANDLED DOMAIN EXCEPTION ===");
            Console.WriteLine($"Is Terminating: {e.IsTerminating}");
            Console.WriteLine($"Exception: {message}");
            Console.WriteLine($"Stack Trace: {stackTrace}");
            
            LoggerService.LogCriticalError(exception ?? new Exception(message), "Unhandled Domain Exception", 
                $"IsTerminating: {e.IsTerminating}");
            
            if (e.IsTerminating)
            {
                LoggerService.LogApplicationShutdown("Application terminating due to unhandled domain exception");
            }
        }

        private void TaskScheduler_UnobservedTaskException(object sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
        {
            Console.WriteLine($"=== UNOBSERVED TASK EXCEPTION ===");
            Console.WriteLine($"Exception: {e.Exception.Message}");
            Console.WriteLine($"Stack Trace: {e.Exception.StackTrace}");
            
            LoggerService.LogCriticalError(e.Exception, "Unobserved Task Exception");
            
            // Mark as observed to prevent application termination
            e.SetObserved();
            Log.Information("Unobserved task exception marked as observed");
        }
    }
}