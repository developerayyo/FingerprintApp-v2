using System;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using ERPNextFingerprintApp.ViewModels;
using ERPNextFingerprintApp.Services;
using System.Diagnostics;

namespace ERPNextFingerprintApp
{
    public partial class MainWindow : Window
    {
        private readonly RegistrationViewModel _registrationViewModel;
        private readonly VerificationViewModel _verificationViewModel;
        private readonly ERPNextApiService _apiService;
        private readonly DispatcherTimer _timeTimer;
        private readonly DispatcherTimer _heartbeatTimer;

        public MainWindow(
            RegistrationViewModel registrationViewModel,
            VerificationViewModel verificationViewModel,
            ERPNextApiService apiService)
        {
            try
            {
                LoggerService.LogWindowEvent("MainWindow", "Constructor Started");
                
                InitializeComponent();
                LoggerService.LogWindowEvent("MainWindow", "InitializeComponent Completed");

                _registrationViewModel = registrationViewModel;
                _verificationViewModel = verificationViewModel;
                _apiService = apiService;

                // Set up ViewModels
                DataContext = this;
                LoggerService.LogWindowEvent("MainWindow", "DataContext Set");
                
                // Initialize timer for current time display
                _timeTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _timeTimer.Tick += TimeTimer_Tick;
                _timeTimer.Start();
                LoggerService.LogWindowEvent("MainWindow", "Timer Initialized and Started");

                // Initialize heartbeat timer for application health monitoring
                _heartbeatTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(30) // Log heartbeat every 30 seconds
                };
                _heartbeatTimer.Tick += HeartbeatTimer_Tick;
                _heartbeatTimer.Start();
                LoggerService.LogWindowEvent("MainWindow", "Heartbeat Timer Initialized and Started");

                // Subscribe to ViewModel events
                _registrationViewModel.PropertyChanged += RegistrationViewModel_PropertyChanged;
                _verificationViewModel.PropertyChanged += VerificationViewModel_PropertyChanged;
                LoggerService.LogWindowEvent("MainWindow", "ViewModel Events Subscribed");

                // Wire up refresh button events
                RefreshStatusButton.Click += RefreshStatusButton_Click;
                RefreshVerificationStatusButton.Click += RefreshVerificationStatusButton_Click;
                LoggerService.LogWindowEvent("MainWindow", "Refresh Button Events Wired");

                // Load initial data
                Loaded += MainWindow_Loaded;
                Closing += MainWindow_Closing;
                LoggerService.LogWindowEvent("MainWindow", "Event Handlers Attached");
                
                LoggerService.LogWindowEvent("MainWindow", "Constructor Completed Successfully");
            }
            catch (Exception ex)
            {
                LoggerService.LogCriticalError(ex, "MainWindow Constructor");
                throw;
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                LoggerService.LogWindowEvent("MainWindow", "Loaded Event Started");
                FooterStatusText.Text = "Initializing application...";
                
                // Test ERPNext connection
                LoggerService.LogServiceOperation("MainWindow", "Testing ERPNext Connection", true);
                var apiService = App.ServiceProvider.GetRequiredService<ERPNextApiService>();
                var connectionTest = await apiService.TestConnectionAsync();
                
                if (connectionTest.IsSuccess)
                {
                    ConnectionStatusText.Text = "● Connected";
                    ConnectionStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80));
                    FooterStatusText.Text = "Connected to ERPNext successfully";
                    LoggerService.LogServiceOperation("ERPNext", "Connection Test", true, "Connection successful");
                }
                else
                {
                    ConnectionStatusText.Text = "● Disconnected";
                    ConnectionStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(244, 67, 54));
                    FooterStatusText.Text = $"ERPNext connection failed: {connectionTest.ErrorMessage}";
                    LoggerService.LogServiceOperation("ERPNext", "Connection Test", false, connectionTest.ErrorMessage);
                }

                // Load employees for both ViewModels
                LoggerService.LogServiceOperation("MainWindow", "Loading Employees", true, "Starting employee data load");
                await ((CommunityToolkit.Mvvm.Input.AsyncRelayCommand)_registrationViewModel.LoadEmployeesCommand).ExecuteAsync(null);
                await ((CommunityToolkit.Mvvm.Input.AsyncRelayCommand)_verificationViewModel.LoadEmployeesCommand).ExecuteAsync(null);
                LoggerService.LogServiceOperation("MainWindow", "Loading Employees", true, "Employee data loaded successfully");

                // Update SDK and device status
                LoggerService.LogServiceOperation("MainWindow", "Updating SDK and Device Status", true, "Starting initial status update");
                await UpdateSdkAndDeviceStatus();
                LoggerService.LogServiceOperation("MainWindow", "Updating SDK and Device Status", true, "Initial status update completed");

                FooterStatusText.Text = "Ready";
                stopwatch.Stop();
                LoggerService.LogPerformance("MainWindow Initialization", stopwatch.Elapsed, stopwatch.ElapsedMilliseconds > 5000);
                LoggerService.LogWindowEvent("MainWindow", "Loaded Event Completed", $"Duration: {stopwatch.ElapsedMilliseconds}ms");
                Log.Information("MainWindow initialization completed successfully in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                FooterStatusText.Text = "Initialization failed";
                ConnectionStatusText.Text = "● Error";
                ConnectionStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(244, 67, 54));
                
                LoggerService.LogException(ex, "MainWindow Initialization", new { ElapsedMs = stopwatch.ElapsedMilliseconds });
                LoggerService.LogWindowEvent("MainWindow", "Loaded Event Failed", $"Duration: {stopwatch.ElapsedMilliseconds}ms, Error: {ex.Message}");
                
                MessageBox.Show($"Failed to initialize application: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                LoggerService.LogWindowEvent("MainWindow", "Closing Event Started");
                LoggerService.LogUserAction("Application Close", "User initiated application close");
                
                // Stop timers
                _timeTimer?.Stop();
                _heartbeatTimer?.Stop();
                LoggerService.LogWindowEvent("MainWindow", "Timers Stopped");
                
                // Unsubscribe from events
                _registrationViewModel.PropertyChanged -= RegistrationViewModel_PropertyChanged;
                _verificationViewModel.PropertyChanged -= VerificationViewModel_PropertyChanged;
                LoggerService.LogWindowEvent("MainWindow", "Event Handlers Unsubscribed");
                
                LoggerService.LogWindowEvent("MainWindow", "Closing Event Completed");
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "MainWindow Closing");
                // Don't cancel the close operation even if cleanup fails
            }
        }

        private void TimeTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                CurrentTimeText.Text = DateTime.Now.ToString("HH:mm:ss");
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "Time Timer Tick");
                // Don't stop the timer for minor display issues
            }
        }

        private void RegistrationViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            try
            {
                if (e.PropertyName == nameof(RegistrationViewModel.StatusMessage))
                {
                    Dispatcher.Invoke(() =>
                    {
                        FooterStatusText.Text = _registrationViewModel.StatusMessage ?? "Ready";
                        LoggerService.LogServiceOperation("RegistrationViewModel", "Status Update", true, _registrationViewModel.StatusMessage);
                    });
                }
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "Registration ViewModel Property Changed", new { PropertyName = e.PropertyName });
            }
        }

        private void VerificationViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            try
            {
                if (e.PropertyName == nameof(VerificationViewModel.StatusMessage))
                {
                    Dispatcher.Invoke(() =>
                    {
                        FooterStatusText.Text = _verificationViewModel.StatusMessage ?? "Ready";
                        LoggerService.LogServiceOperation("VerificationViewModel", "Status Update", true, _verificationViewModel.StatusMessage);
                    });
                }
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "Verification ViewModel Property Changed", new { PropertyName = e.PropertyName });
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                LoggerService.LogWindowEvent("MainWindow", "OnClosed Started");
                
                _timeTimer?.Stop();
                
                // Unsubscribe from events (safety check)
                _registrationViewModel.PropertyChanged -= RegistrationViewModel_PropertyChanged;
                _verificationViewModel.PropertyChanged -= VerificationViewModel_PropertyChanged;

                LoggerService.LogWindowEvent("MainWindow", "OnClosed Completed");
                base.OnClosed(e);
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "MainWindow OnClosed");
                base.OnClosed(e);
            }
        }

        protected override void OnActivated(EventArgs e)
        {
            LoggerService.LogWindowEvent("MainWindow", "Activated");
            base.OnActivated(e);
        }

        protected override void OnDeactivated(EventArgs e)
        {
            LoggerService.LogWindowEvent("MainWindow", "Deactivated");
            base.OnDeactivated(e);
        }

        protected override void OnStateChanged(EventArgs e)
        {
            LoggerService.LogWindowEvent("MainWindow", "State Changed", $"New state: {WindowState}");
            base.OnStateChanged(e);
        }

        private void HeartbeatTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                var process = Process.GetCurrentProcess();
                var memoryUsage = process.WorkingSet64 / (1024 * 1024); // MB
                var uptime = DateTime.Now - Process.GetCurrentProcess().StartTime;
                
                Log.Information("[HEARTBEAT] Application running normally - Uptime: {Uptime}, Memory: {MemoryMB}MB, Threads: {ThreadCount}", 
                    uptime.ToString(@"hh\:mm\:ss"), memoryUsage, process.Threads.Count);
                
                LoggerService.LogPerformance("Application Heartbeat", uptime, false);
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "Heartbeat Timer");
            }
        }

        // Refresh button event handlers
        private async void RefreshStatusButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LoggerService.LogUserAction("Refresh Status", "User clicked refresh status button in Registration tab");
                await UpdateSdkAndDeviceStatus();
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "Refresh Status Button Click");
            }
        }

        private async void RefreshVerificationStatusButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LoggerService.LogUserAction("Refresh Status", "User clicked refresh status button in Verification tab");
                await UpdateSdkAndDeviceStatus();
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "Refresh Verification Status Button Click");
            }
        }

        private async System.Threading.Tasks.Task UpdateSdkAndDeviceStatus()
        {
            try
            {
                LoggerService.LogServiceOperation("StatusUpdate", "SDK and Device Status Check", true, "Starting status update");
                
                // Get fingerprint service from DI container
                var fingerprintService = App.ServiceProvider.GetRequiredService<FingerprintService>();
                
                // Check SDK status
                bool sdkInstalled = await CheckSdkInstallation();
                bool deviceConnected = await CheckDeviceConnection(fingerprintService);
                
                // Update header status
                Dispatcher.Invoke(() =>
                {
                    // Update header SDK status
                    SdkStatusText.Text = sdkInstalled ? "Installed" : "Not Installed";
                    SdkStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                        sdkInstalled ? System.Windows.Media.Color.FromRgb(76, 175, 80) : System.Windows.Media.Color.FromRgb(255, 107, 107));
                    
                    // Update header device status
                    DeviceStatusText.Text = deviceConnected ? "Connected" : "Not Connected";
                    DeviceStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                        deviceConnected ? System.Windows.Media.Color.FromRgb(76, 175, 80) : System.Windows.Media.Color.FromRgb(255, 107, 107));
                    
                    // Update Registration tab status
                    RegistrationSdkStatusText.Text = sdkInstalled ? "Installed" : "Not Installed";
                    RegistrationSdkStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                        sdkInstalled ? System.Windows.Media.Color.FromRgb(76, 175, 80) : System.Windows.Media.Color.FromRgb(255, 107, 107));
                    
                    RegistrationDeviceStatusText.Text = deviceConnected ? "Connected" : "Not Connected";
                    RegistrationDeviceStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                        deviceConnected ? System.Windows.Media.Color.FromRgb(76, 175, 80) : System.Windows.Media.Color.FromRgb(255, 107, 107));
                    
                    // Update Verification tab status
                    VerificationSdkStatusText.Text = sdkInstalled ? "Installed" : "Not Installed";
                    VerificationSdkStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                        sdkInstalled ? System.Windows.Media.Color.FromRgb(76, 175, 80) : System.Windows.Media.Color.FromRgb(255, 107, 107));
                    
                    VerificationDeviceStatusText.Text = deviceConnected ? "Connected" : "Not Connected";
                    VerificationDeviceStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                        deviceConnected ? System.Windows.Media.Color.FromRgb(76, 175, 80) : System.Windows.Media.Color.FromRgb(255, 107, 107));
                });
                
                // Log status update
                LoggerService.LogServiceOperation("StatusUpdate", "SDK and Device Status Check", true, 
                    $"SDK: {(sdkInstalled ? "Installed" : "Not Installed")}, Device: {(deviceConnected ? "Connected" : "Not Connected")}");
                
                Log.Information("[STATUS_UPDATE] SDK Status: {SdkStatus}, Device Status: {DeviceStatus}", 
                    sdkInstalled ? "Installed" : "Not Installed", 
                    deviceConnected ? "Connected" : "Not Connected");
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "Update SDK and Device Status");
                
                // Set error status on UI
                Dispatcher.Invoke(() =>
                {
                    var errorBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 107, 107));
                    
                    SdkStatusText.Text = "Error";
                    SdkStatusText.Foreground = errorBrush;
                    DeviceStatusText.Text = "Error";
                    DeviceStatusText.Foreground = errorBrush;
                    
                    RegistrationSdkStatusText.Text = "Error";
                    RegistrationSdkStatusText.Foreground = errorBrush;
                    RegistrationDeviceStatusText.Text = "Error";
                    RegistrationDeviceStatusText.Foreground = errorBrush;
                    
                    VerificationSdkStatusText.Text = "Error";
                    VerificationSdkStatusText.Foreground = errorBrush;
                    VerificationDeviceStatusText.Text = "Error";
                    VerificationDeviceStatusText.Foreground = errorBrush;
                });
            }
        }

        private async System.Threading.Tasks.Task<bool> CheckSdkInstallation()
        {
            try
            {
                // Check if DigitalPersona U.are.U SDK DLL files exist
                string[] requiredDlls = {
                    "DPUruNet.dll",
                    "DPCtlUruNet.dll"
                };

                string sdkPath = @"C:\Program Files\DigitalPersona\U.are.U SDK\Windows\Lib\.NET";
                int foundDlls = 0;

                foreach (string dllName in requiredDlls)
                {
                    string fullPath = System.IO.Path.Combine(sdkPath, dllName);
                    if (System.IO.File.Exists(fullPath))
                    {
                        foundDlls++;
                        LoggerService.LogServiceOperation("SDK Check", "DLL Found", true, $"Found at: {fullPath}");
                    }
                }

                if (foundDlls > 0)
                {
                    LoggerService.LogServiceOperation("SDK Check", "SDK Detected", true, $"Found {foundDlls} DigitalPersona U.are.U SDK DLL files");
                    return true;
                }

                LoggerService.LogServiceOperation("SDK Check", "DLL Not Found", false, "DigitalPersona U.are.U SDK DLL files not found in standard locations");
                return false;
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "Check SDK Installation");
                return false;
            }
        }

        private async System.Threading.Tasks.Task<bool> CheckDeviceConnection(FingerprintService fingerprintService)
        {
            try
            {
                // Try to initialize the fingerprint service to check device connection
                var result = await fingerprintService.InitializeAsync();
                LoggerService.LogServiceOperation("Device Check", "Connection Test", result, result ? "Device connected successfully" : "Device connection failed");
                return result;
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "Check Device Connection");
                return false;
            }
        }

        // Properties for data binding
        public RegistrationViewModel RegistrationViewModel => _registrationViewModel;
        public VerificationViewModel VerificationViewModel => _verificationViewModel;

        private async void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LoggerService.LogWindowEvent("MainWindow", "Logout Initiated");

                // Show confirmation dialog
                var result = MessageBox.Show(
                    "Are you sure you want to logout?", 
                    "Confirm Logout", 
                    MessageBoxButton.YesNo, 
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // Perform logout
                    await _apiService.LogoutAsync();
                    LoggerService.LogWindowEvent("MainWindow", "Logout Completed");

                    // Create and show login window
                    var loginWindow = App.ServiceProvider.GetRequiredService<Views.LoginWindow>();
                    
                    // Set the login window as the application's main window
                    Application.Current.MainWindow = loginWindow;
                    loginWindow.Show();
                    
                    // Close the main window
                    this.Close();
                    
                    Log.Information("User logged out successfully and returned to login screen");
                }
                else
                {
                    LoggerService.LogWindowEvent("MainWindow", "Logout Cancelled");
                }
            }
            catch (Exception ex)
            {
                LoggerService.LogException(ex, "Logout Process");
                Log.Error(ex, "Error during logout process");
                
                MessageBox.Show(
                    $"An error occurred during logout: {ex.Message}", 
                    "Logout Error", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Error);
            }
        }
    }
}