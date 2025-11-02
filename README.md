# ERPNext Fingerprint Integration System

A comprehensive Windows desktop application that integrates fingerprint authentication with ERPNext ERP system. This application provides secure employee authentication, fingerprint enrollment, and seamless integration with ERPNext's employee management system.

## 🚀 Features

### Core Functionality
- **Fingerprint Authentication**: Secure employee login using DigitalPersona fingerprint scanners
- **ERPNext Integration**: Direct integration with ERPNext ERP system for employee management
- **Employee Registration**: Register new employees with fingerprint enrollment
- **Employee Verification**: Verify employee identity using stored fingerprints
- **Session Management**: Secure session handling with automatic logout
- **Real-time Logging**: Comprehensive logging system with file and console output

### User Interface
- **Modern WPF Interface**: Clean, professional Windows desktop application
- **Login Window**: Secure authentication with username/password
- **Main Dashboard**: Tabbed interface for registration and verification
- **Status Indicators**: Real-time connection, SDK, and device status
- **Loading Indicators**: Visual feedback during operations
- **Error Handling**: User-friendly error messages and recovery

### Technical Features
- **DigitalPersona SDK Integration**: Support for U.are.U fingerprint scanners
- **RESTful API Communication**: Secure communication with ERPNext
- **Configuration Management**: JSON-based configuration system
- **Dependency Injection**: Modern .NET architecture with DI container
- **MVVM Pattern**: Clean separation of concerns using MVVM
- **Async/Await**: Non-blocking operations for better user experience

## 📋 Prerequisites

### System Requirements
- **Operating System**: Windows 10/11 (64-bit)
- **Framework**: .NET 8.0 Runtime
- **Memory**: Minimum 4GB RAM
- **Storage**: 100MB free disk space
- **Network**: Internet connection for ERPNext communication

### Hardware Requirements
- **Fingerprint Scanner**: DigitalPersona U.are.U series scanner
- **USB Port**: Available USB port for scanner connection

### Software Dependencies
- **DigitalPersona U.are.U SDK**: Required for fingerprint scanner operation
- **ERPNext Server**: Running ERPNext instance with API access
- **Valid ERPNext Credentials**: API key and secret for authentication

## 🛠️ Installation

### 1. Install DigitalPersona SDK

1. Download the DigitalPersona U.are.U SDK from the official website
2. Run the installer as Administrator
3. Follow the installation wizard
4. Verify installation by checking:
   ```
   C:\Program Files\DigitalPersona\U.are.U SDK\Windows\Lib\.NET\
   ```
5. Ensure the following DLLs are present:
   - `DPUruNet.dll`
   - `DPCtlUruNet.dll`

### 2. Download Application

#### Option A: Download Release (Recommended)
1. Go to the [Releases](../../releases) page
2. Download the latest `ERPNextFingerprintApp.exe`
3. Create a folder for the application (e.g., `C:\ERPNextFingerprintApp`)
4. Place the executable in the folder

#### Option B: Build from Source
1. Clone the repository:
   ```bash
   git clone https://github.com/your-org/erpnext-fingerprint-app.git
   cd erpnext-fingerprint-app
   ```

2. Restore dependencies:
   ```bash
   dotnet restore
   ```

3. Build the application:
   ```bash
   dotnet build --configuration Release
   ```

4. Publish (optional):
   ```bash
   dotnet publish --configuration Release --self-contained true --runtime win-x64
   ```

### 3. Configuration Setup

1. Copy `config.json.example` to `config.json` in the application directory
2. Edit `config.json` with your ERPNext details (see [Configuration](#-configuration))
3. Ensure the configuration file is in the same directory as the executable

## ⚙️ Configuration

### Configuration File: `config.json`

The application uses a JSON configuration file that must be placed in the same directory as the executable. Copy `config.json.example` to `config.json` and modify the values:

```json
{
  "erp_url": "https://your-erpnext-domain.com",
  "log_path": ".\\logs\\FingerprintApp.log",
  "fingerprint_cache_enabled": true,
  "auto_save_to_erpnext": true,
  "connection_timeout": 30,
  "max_retry_attempts": 3
}
```

### Configuration Parameters

| Parameter | Type | Description | Default |
|-----------|------|-------------|---------|
| `erp_url` | string | ERPNext server URL (with https://) | Required |
| `log_path` | string | Path for log files | `".\\logs\\FingerprintApp.log"` |
| `fingerprint_cache_enabled` | boolean | Enable fingerprint caching | `true` |
| `auto_save_to_erpnext` | boolean | Auto-save data to ERPNext | `true` |
| `connection_timeout` | integer | API timeout in seconds | `30` |
| `max_retry_attempts` | integer | Maximum retry attempts for failed requests | `3` |

### ERPNext User Setup

The application uses **session-based authentication** with username/password login:

1. **Create ERPNext User Account**:
   - Login to ERPNext as Administrator
   - Go to: User → Add User
   - Create user account with appropriate permissions
   - User will login with these credentials in the application

2. **Required Permissions**:
   - Employee: Read, Write, Create
   - User: Read, Write, Create
   - File: Read, Write, Create

**Note**: The application uses secure session cookies for all API calls after login - no API keys required!

## 🚀 Usage

### First Time Setup

1. **Connect Fingerprint Scanner**:
   - Connect your DigitalPersona scanner via USB
   - Wait for Windows to recognize the device
   - Check Device Manager for proper installation

2. **Launch Application**:
   - Run `ERPNextFingerprintApp.exe`
   - The application will create necessary directories and log files

3. **Login**:
   - Enter your ERPNext username and password
   - Click "Login" or press Enter
   - The application will authenticate with ERPNext

### Employee Registration

1. **Navigate to Registration Tab**
2. **Enter Employee Details**:
   - Employee ID (must match ERPNext Employee ID)
   - Full Name
   - Department (optional)
   - Additional details as required

3. **Fingerprint Enrollment**:
   - Click "Start Enrollment"
   - Follow on-screen instructions
   - Place finger on scanner multiple times
   - Wait for enrollment completion

4. **Save to ERPNext**:
   - Review entered information
   - Click "Save to ERPNext"
   - Verify successful registration

### Employee Verification

1. **Navigate to Verification Tab**
2. **Set Deduction Details**:
   - Enter deduction amount
   - Select deduction type (Loan, Advance, etc.)

3. **Start Verification**:
   - Click "Start Verification"
   - Place finger on scanner
   - Wait for verification result

4. **Automatic Processing** (happens immediately after verification):
   - Employee information is displayed
   - Verification status (Success/Failed) is shown
   - **If successful**: Deduction is automatically created in ERPNext
   - Transaction ID is generated and displayed
   - Recent deductions list is updated

5. **View Results**:
   - Employee details and verification timestamp
   - Deduction processing status
   - Transaction ID for successful deductions
   - Recent deductions history (last 10 transactions)
   - Automatic reset after 3 seconds for next verification

### Application Features

#### Status Indicators
- **Connection**: Shows ERPNext connection status
- **Time**: Current system time
- **SDK**: DigitalPersona SDK status
- **Device**: Fingerprint scanner status

#### Logging
- All operations are logged to the configured log file
- Logs include timestamps, session IDs, and detailed information
- Log files are rotated daily with 30-day retention

#### Session Management
- Automatic session timeout
- Secure logout functionality
- Session persistence across operations

## 🔧 Troubleshooting

### Common Issues

#### 1. "SDK Not Installed" Error
**Problem**: DigitalPersona SDK not found
**Solution**:
- Verify SDK installation path
- Reinstall DigitalPersona U.are.U SDK
- Run application as Administrator
- Check Windows Event Viewer for detailed errors

#### 2. "Device Not Connected" Error
**Problem**: Fingerprint scanner not detected
**Solution**:
- Check USB connection
- Verify device in Device Manager
- Try different USB port
- Restart the application
- Update scanner drivers

#### 3. "Connection Failed" Error
**Problem**: Cannot connect to ERPNext
**Solution**:
- Verify ERPNext URL in config.json
- Check internet connection
- Check ERPNext server status
- Review firewall settings

#### 4. "Authentication Failed" Error
**Problem**: Invalid login credentials
**Solution**:
- Verify username and password
- Check ERPNext user permissions
- Ensure user account is active
- Reset password if necessary

#### 5. Configuration Issues
**Problem**: Application won't start or crashes
**Solution**:
- Verify config.json syntax
- Check file permissions
- Review log files for errors
- Restore from config.json.example

### Log File Analysis

Log files are located at the path specified in `config.json` (default: `./logs/`). Each log entry includes:
- Timestamp
- Log level (INFO, WARN, ERROR)
- Session ID
- Detailed message
- Stack trace (for errors)

**Example log entry**:
```
[2024-10-25 14:30:15.123 +01:00 INF] [a1b2c3d4] Employee verification completed successfully for ID: EMP001
```

### Performance Optimization

1. **Fingerprint Cache**: Enable caching for faster verification
2. **Connection Timeout**: Adjust based on network conditions
3. **Retry Attempts**: Configure based on network reliability
4. **Log Level**: Reduce logging for production environments

## 🏗️ Architecture

### Project Structure
```
ERPNextFingerprintApp/
├── Models/                 # Data models and entities
│   ├── Config.cs          # Configuration model
│   ├── Employee.cs        # Employee data model
│   ├── VerificationResult.cs
│   └── EnrollmentResult.cs
├── Services/              # Business logic services
│   ├── ERPNextApiService.cs    # ERPNext API integration
│   ├── FingerprintService.cs   # Fingerprint operations
│   ├── DigitalPersonaSDK.cs    # SDK wrapper
│   └── LoggerService.cs        # Logging service
├── ViewModels/            # MVVM view models
│   ├── RegistrationViewModel.cs
│   └── VerificationViewModel.cs
├── Views/                 # WPF user interface
│   ├── LoginWindow.xaml
│   ├── LoginWindow.xaml.cs
│   ├── MainWindow.xaml
│   └── MainWindow.xaml.cs
├── Utils/                 # Utility classes
│   ├── JsonHelper.cs      # JSON serialization
│   ├── ApiResponseHandler.cs
│   └── SecurityHelper.cs
├── App.xaml              # Application entry point
├── App.xaml.cs           # Application logic
└── config.json           # Configuration file
```

### Technology Stack
- **.NET 8.0**: Modern .NET framework
- **WPF**: Windows Presentation Foundation for UI
- **MVVM**: Model-View-ViewModel pattern
- **Dependency Injection**: Microsoft.Extensions.DependencyInjection
- **Logging**: Serilog with file and console sinks
- **HTTP Client**: Built-in HttpClient for API communication
- **JSON**: Newtonsoft.Json for serialization
- **DigitalPersona SDK**: Fingerprint scanner integration

### Design Patterns
- **MVVM**: Separation of UI and business logic
- **Dependency Injection**: Loose coupling and testability
- **Repository Pattern**: Data access abstraction
- **Observer Pattern**: Event-driven architecture
- **Factory Pattern**: Service creation and management

## 🔒 Security

### Data Protection
- **Encrypted Communication**: HTTPS for all API calls
- **Secure Authentication**: Session-based ERPNext authentication
- **Fingerprint Security**: Biometric data encrypted at rest
- **Session Management**: Secure session handling with timeouts
- **Input Validation**: Comprehensive input sanitization

### Best Practices
- **Session Security**: Automatic session timeouts and secure logout
- **Logging**: Sensitive data excluded from logs
- **Error Handling**: Graceful error handling without data exposure
- **Updates**: Regular security updates and patches
- **Access Control**: Role-based permissions in ERPNext

## 🤝 Contributing

### Development Setup
1. Clone the repository
2. Install .NET 8.0 SDK
3. Install DigitalPersona SDK
4. Open in Visual Studio or VS Code
5. Restore NuGet packages
6. Configure development settings

### Code Standards
- Follow C# coding conventions
- Use async/await for I/O operations
- Implement proper error handling
- Add comprehensive logging
- Write unit tests for business logic
- Document public APIs

### Pull Request Process
1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests if applicable
5. Update documentation
6. Submit pull request

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🆘 Support

### Getting Help
- **Documentation**: Check this README and inline code comments
- **Issues**: Create an issue on GitHub for bugs or feature requests
- **Discussions**: Use GitHub Discussions for questions and ideas
- **Email**: Contact support at [support@lordsminttech.com.ng](mailto:support@lordsminttech.com.ng)

### Reporting Issues
When reporting issues, please include:
- Operating system version
- .NET version
- DigitalPersona SDK version
- Application version
- Steps to reproduce
- Error messages and log files
- Screenshots if applicable

## 🔄 Changelog

### Version 2.0.0 (Current)
- Added modern login system with session management
- Implemented comprehensive logging with Serilog
- Enhanced error handling and user feedback
- Improved UI with modern WPF styling
- Added configuration management system
- Implemented dependency injection architecture
- Enhanced security with proper authentication
- Added comprehensive documentation

### Version 1.0.0
- Initial release with basic fingerprint functionality
- ERPNext integration for employee management
- DigitalPersona SDK integration
- Basic WPF user interface

## 🙏 Acknowledgments

- **DigitalPersona**: For the U.are.U SDK
- **ERPNext**: For the excellent ERP platform
- **Microsoft**: For .NET and WPF frameworks
- **Serilog**: For comprehensive logging capabilities
- **Newtonsoft**: For JSON serialization

---

**Developed by Lord's Mint Tech Nig Ltd**  
For technical support, please contact: [support@lordsminttech.com.ng](mailto:support@lordsminttech.com.ng)