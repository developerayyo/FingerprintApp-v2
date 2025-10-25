using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using ERPNextFingerprintApp.Services;

namespace ERPNextFingerprintApp.Views
{
    public partial class LoginWindow : Window
    {
        private readonly ERPNextApiService _apiService;

        public LoginWindow()
        {
            InitializeComponent();
            
            // Get the API service from dependency injection
            _apiService = App.ServiceProvider.GetRequiredService<ERPNextApiService>();
            
            // Set focus to username textbox
            Loaded += (s, e) => UsernameTextBox.Focus();
            
            // Handle Enter key press for login
            KeyDown += LoginWindow_KeyDown;
            UsernameTextBox.KeyDown += InputField_KeyDown;
            PasswordBox.KeyDown += InputField_KeyDown;
        }

        private void LoginWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && LoginButton.IsEnabled)
            {
                _ = LoginAsync();
            }
        }

        private void InputField_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (sender == UsernameTextBox && string.IsNullOrWhiteSpace(UsernameTextBox.Text))
                {
                    return;
                }
                
                if (sender == UsernameTextBox)
                {
                    PasswordBox.Focus();
                }
                else if (sender == PasswordBox && LoginButton.IsEnabled)
                {
                    _ = LoginAsync();
                }
            }
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            await LoginAsync();
        }

        private async Task LoginAsync()
        {
            var username = UsernameTextBox.Text?.Trim();
            var password = PasswordBox.Password;

            // Validate input
            if (string.IsNullOrWhiteSpace(username))
            {
                ShowErrorMessage("Please enter your username.");
                UsernameTextBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                ShowErrorMessage("Please enter your password.");
                PasswordBox.Focus();
                return;
            }

            // Show loading state
            SetLoadingState(true);
            HideStatusMessage();

            try
            {
                Log.Information("Attempting login for user: {Username}", username);
                
                // Attempt login
                var loginResult = await _apiService.LoginAsync(username, password);
                
                if (loginResult)
                {
                    Log.Information("Login successful for user: {Username}", username);
                    ShowSuccessMessage("Login successful! Opening application...");
                    
                    // Small delay to show success message
                    await Task.Delay(1000);
                    
                    // Open main window and close login window
                    OpenMainWindow();
                }
                else
                {
                    Log.Warning("Login failed for user: {Username}", username);
                    ShowErrorMessage("Invalid username or password. Please try again.");
                    PasswordBox.Clear();
                    PasswordBox.Focus();
                }
            }
            catch (System.Net.Http.HttpRequestException ex)
            {
                Log.Error(ex, "Network error during login for user: {Username}", username);
                ShowErrorMessage("Network error. Please check your internet connection and try again.");
            }
            catch (TaskCanceledException ex)
            {
                Log.Error(ex, "Login request timed out for user: {Username}", username);
                ShowErrorMessage("Login request timed out. Please try again.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unexpected error during login for user: {Username}", username);
                ShowErrorMessage("An unexpected error occurred. Please try again.");
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private void OpenMainWindow()
        {
            try
            {
                // Create and show the main window
                var mainWindow = App.ServiceProvider.GetRequiredService<MainWindow>();
                
                // Set the main window as the application's main window
                Application.Current.MainWindow = mainWindow;
                mainWindow.Show();
                
                // Close the login window
                this.Close();
                
                Log.Information("Main window opened successfully after login");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error opening main window after login");
                ShowErrorMessage("Error opening the main application. Please restart the application.");
            }
        }

        private void SetLoadingState(bool isLoading)
        {
            LoginButton.IsEnabled = !isLoading;
            UsernameTextBox.IsEnabled = !isLoading;
            PasswordBox.IsEnabled = !isLoading;
            
            LoadingPanel.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            
            if (isLoading)
            {
                Cursor = Cursors.Wait;
            }
            else
            {
                Cursor = Cursors.Arrow;
            }
        }

        private void ShowErrorMessage(string message)
        {
            StatusTextBlock.Text = message;
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
            StatusTextBlock.Visibility = Visibility.Visible;
        }

        private void ShowSuccessMessage(string message)
        {
            StatusTextBlock.Text = message;
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
            StatusTextBlock.Visibility = Visibility.Visible;
        }

        private void HideStatusMessage()
        {
            StatusTextBlock.Visibility = Visibility.Collapsed;
        }

        protected override void OnClosed(EventArgs e)
        {
            // If the login window is closed without successful login, exit the application
            if (Application.Current.MainWindow == this)
            {
                Log.Information("Login window closed, shutting down application");
                Application.Current.Shutdown();
            }
            
            base.OnClosed(e);
        }
    }
}