# ERPNext Fingerprint App - Deployment Guide

This guide provides comprehensive instructions for deploying the ERPNext Fingerprint Application across different environments.

## 📋 Table of Contents

1. [Deployment Methods](#deployment-methods)
2. [Prerequisites](#prerequisites)
3. [Quick Deployment](#quick-deployment)
4. [Advanced Deployment](#advanced-deployment)
5. [Enterprise Deployment](#enterprise-deployment)
6. [Troubleshooting](#troubleshooting)

## 🚀 Deployment Methods

### Method 1: Self-Contained Deployment (Recommended)
**Best for:** Most environments, no .NET runtime dependency
- **Size:** ~150MB
- **Dependencies:** Only DigitalPersona SDK required
- **Pros:** No .NET runtime needed, fully portable
- **Cons:** Larger file size

### Method 2: Framework-Dependent Deployment
**Best for:** Environments with .NET 8.0 already installed
- **Size:** ~5MB
- **Dependencies:** .NET 8.0 Runtime + DigitalPersona SDK
- **Pros:** Smaller size, shared runtime
- **Cons:** Requires .NET runtime on target system

### Method 3: MSI Installer (Future)
**Best for:** Enterprise environments with centralized deployment
- **Status:** Planned for future release
- **Features:** Windows Installer, Group Policy deployment

## 📦 Prerequisites

### System Requirements
- **OS:** Windows 10/11 (64-bit)
- **RAM:** Minimum 4GB, Recommended 8GB
- **Storage:** 500MB free space
- **Network:** Internet connection for ERPNext API

### Required Software
1. **DigitalPersona U.are.U SDK**
   - Download from DigitalPersona website
   - Install as Administrator
   - Verify installation at: `C:\Program Files\DigitalPersona\U.are.U SDK\`

2. **.NET 8.0 Runtime** (Framework-dependent only)
   ```powershell
   # Install via winget
   winget install Microsoft.DotNet.Runtime.8
   
   # Or download from Microsoft
   # https://dotnet.microsoft.com/download/dotnet/8.0
   ```

### Hardware Requirements
- **Fingerprint Scanner:** DigitalPersona U.are.U compatible device
- **USB Port:** For scanner connection
- **Network:** Stable connection to ERPNext server

## ⚡ Quick Deployment

### Step 1: Create Deployment Package

```powershell
# Navigate to project directory
cd "C:\Path\To\FingerprintApp v2"

# Run deployment script
.\deploy.ps1 -DeploymentType standalone -IncludeConfig
```

### Step 2: Deploy to Target System

```powershell
# Copy deployment folder to target system
# Extract to desired location (e.g., C:\ERPNextFingerprintApp)

# Run installer script
.\install.ps1 -InstallPath "C:\ERPNextFingerprintApp" -CreateShortcut -CheckPrerequisites
```

### Step 3: Configure Application

1. Edit `config.json`:
   ```json
   {
     "erp_url": "https://your-erpnext-server.com",
     "log_path": ".\\logs\\FingerprintApp.log",
     "fingerprint_cache_enabled": true,
     "auto_save_to_erpnext": true,
     "connection_timeout": 30,
     "max_retry_attempts": 3
   }
   ```

2. Test the application:
   ```powershell
   .\ERPNextFingerprintApp.exe
   ```

## 🔧 Advanced Deployment

### Custom Build Configuration

```powershell
# Build with specific optimizations
dotnet publish `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  --output "./deploy/custom" `
  -p:PublishSingleFile=true `
  -p:PublishTrimmed=true `
  -p:TrimMode=link
```

### Automated Deployment Script

```powershell
# deploy-advanced.ps1
param(
    [string[]]$TargetServers,
    [string]$DeploymentPath = "C:\ERPNextFingerprintApp",
    [PSCredential]$Credential
)

foreach ($server in $TargetServers) {
    Write-Host "Deploying to $server..."
    
    # Copy files via network share or WinRM
    $session = New-PSSession -ComputerName $server -Credential $Credential
    
    # Copy deployment package
    Copy-Item -Path "./deploy/standalone/*" -Destination $DeploymentPath -ToSession $session -Recurse -Force
    
    # Run remote installation
    Invoke-Command -Session $session -ScriptBlock {
        param($InstallPath)
        & "$InstallPath\install.ps1" -InstallPath $InstallPath -CreateShortcut -CheckPrerequisites
    } -ArgumentList $DeploymentPath
    
    Remove-PSSession $session
    Write-Host "✅ Deployment to $server completed"
}
```

## 🏢 Enterprise Deployment

### Group Policy Deployment

1. **Create MSI Package** (Future feature):
   ```powershell
   # Will be available in future release
   .\create-msi.ps1 -OutputPath "./deploy/ERPNextFingerprintApp.msi"
   ```

2. **Deploy via SCCM/Intune**:
   - Package the standalone deployment
   - Create detection rules for DigitalPersona SDK
   - Deploy with user context for proper permissions

### Centralized Configuration Management

```powershell
# config-template.json (stored on network share)
{
  "erp_url": "https://company.erpnext.com",
  "log_path": "\\\\fileserver\\logs\\FingerprintApp\\{COMPUTERNAME}.log",
  "fingerprint_cache_enabled": true,
  "auto_save_to_erpnext": true,
  "connection_timeout": 30,
  "max_retry_attempts": 3
}
```

### Monitoring and Logging

```powershell
# Central log collection script
$computers = Get-ADComputer -Filter "Name -like '*-KIOSK-*'"
foreach ($computer in $computers) {
    $logPath = "\\$($computer.Name)\C$\ERPNextFingerprintApp\logs\*.log"
    if (Test-Path $logPath) {
        Copy-Item $logPath "\\fileserver\CentralLogs\FingerprintApp\$($computer.Name)\" -Force
    }
}
```

## 🔍 Troubleshooting

### Common Deployment Issues

#### 1. SDK Not Found
```powershell
# Check SDK installation
$sdkPaths = @(
    "C:\Program Files\DigitalPersona\U.are.U SDK\Windows\Lib\.NET\DPUruNet.dll",
    "C:\Program Files (x86)\DigitalPersona\U.are.U SDK\Windows\Lib\.NET\DPUruNet.dll"
)

foreach ($path in $sdkPaths) {
    if (Test-Path $path) {
        Write-Host "✅ SDK found: $path"
    }
}
```

#### 2. .NET Runtime Missing
```powershell
# Check .NET installation
dotnet --list-runtimes | Where-Object { $_ -like "*Microsoft.WindowsDesktop.App 8.0*" }
```

#### 3. Permission Issues
```powershell
# Fix file permissions
icacls "C:\ERPNextFingerprintApp" /grant "Users:(OI)(CI)F" /T
```

#### 4. Firewall Configuration
```powershell
# Allow application through firewall
New-NetFirewallRule -DisplayName "ERPNext Fingerprint App" -Direction Outbound -Program "C:\ERPNextFingerprintApp\ERPNextFingerprintApp.exe" -Action Allow
```

### Deployment Validation

```powershell
# validate-deployment.ps1
function Test-Deployment {
    param([string]$InstallPath)
    
    $checks = @()
    
    # Check application files
    $requiredFiles = @("ERPNextFingerprintApp.exe", "config.json")
    foreach ($file in $requiredFiles) {
        $filePath = Join-Path $InstallPath $file
        if (Test-Path $filePath) {
            $checks += "✅ $file found"
        } else {
            $checks += "❌ $file missing"
        }
    }
    
    # Check SDK
    if (Test-Path "C:\Program Files\DigitalPersona\U.are.U SDK\Windows\Lib\.NET\DPUruNet.dll") {
        $checks += "✅ DigitalPersona SDK installed"
    } else {
        $checks += "❌ DigitalPersona SDK missing"
    }
    
    # Check configuration
    $configPath = Join-Path $InstallPath "config.json"
    if (Test-Path $configPath) {
        try {
            $config = Get-Content $configPath | ConvertFrom-Json
            if ($config.erp_url -and $config.erp_url -ne "https://your-erpnext-domain.com") {
                $checks += "✅ Configuration appears valid"
            } else {
                $checks += "⚠️ Configuration needs to be updated"
            }
        } catch {
            $checks += "❌ Configuration file is invalid"
        }
    }
    
    return $checks
}

# Run validation
Test-Deployment -InstallPath "C:\ERPNextFingerprintApp"
```

## 📊 Deployment Checklist

### Pre-Deployment
- [ ] System requirements verified
- [ ] DigitalPersona SDK installed
- [ ] .NET Runtime installed (if using framework-dependent)
- [ ] Network connectivity to ERPNext server confirmed
- [ ] Fingerprint scanner connected and tested

### Deployment
- [ ] Application files copied to target location
- [ ] Configuration file created and customized
- [ ] Logs directory created
- [ ] File permissions set correctly
- [ ] Firewall rules configured (if needed)

### Post-Deployment
- [ ] Application launches successfully
- [ ] ERPNext connection established
- [ ] Fingerprint scanner detected
- [ ] Test employee registration
- [ ] Test employee verification
- [ ] Test ticket management (if applicable)
- [ ] Logs are being generated correctly

### Documentation
- [ ] Installation location documented
- [ ] Configuration settings documented
- [ ] User training completed
- [ ] Support contacts provided

## 🔄 Update Deployment

### In-Place Updates
```powershell
# Stop application if running
Stop-Process -Name "ERPNextFingerprintApp" -Force -ErrorAction SilentlyContinue

# Backup current installation
Copy-Item "C:\ERPNextFingerprintApp" "C:\ERPNextFingerprintApp.backup.$(Get-Date -Format 'yyyyMMdd')" -Recurse

# Deploy new version (preserve config.json)
Copy-Item "./deploy/standalone/*" "C:\ERPNextFingerprintApp" -Exclude "config.json" -Force -Recurse

# Restart application
Start-Process "C:\ERPNextFingerprintApp\ERPNextFingerprintApp.exe"
```

### Rollback Procedure
```powershell
# Stop current version
Stop-Process -Name "ERPNextFingerprintApp" -Force -ErrorAction SilentlyContinue

# Restore from backup
Remove-Item "C:\ERPNextFingerprintApp" -Recurse -Force
Rename-Item "C:\ERPNextFingerprintApp.backup.20240101" "C:\ERPNextFingerprintApp"

# Restart application
Start-Process "C:\ERPNextFingerprintApp\ERPNextFingerprintApp.exe"
```

## 📞 Support

For deployment issues:
1. Check application logs in `logs/` directory
2. Verify system requirements
3. Test network connectivity to ERPNext
4. Contact system administrator

---

**Note:** This deployment guide is for ERPNext Fingerprint Application v2.1.0 and later versions.