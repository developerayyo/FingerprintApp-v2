using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Serilog;
using ERPNextFingerprintApp.Models;
using ERPNextFingerprintApp.Utils;

namespace ERPNextFingerprintApp.Services
{
    public class ERPNextApiService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly Config _config;
        private bool _disposed = false;
        private string? _sessionId;
        private bool _isSessionAuthenticated = false;

        public bool IsAuthenticated => _isSessionAuthenticated && !string.IsNullOrEmpty(_sessionId);

        public ERPNextApiService(Config config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            
            _httpClient = new HttpClient()
            {
                Timeout = TimeSpan.FromSeconds(_config.ConnectionTimeout)
            };

            // Only set API key authorization if not using session authentication
            if (!_isSessionAuthenticated && !string.IsNullOrEmpty(_config.AuthorizationHeader))
            {
                _httpClient.DefaultRequestHeaders.Add("Authorization", _config.AuthorizationHeader);
            }
            
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "ERPNext-Fingerprint-App/1.0");
        }

        public async Task<bool> LoginAsync(string username, string password)
        {
            const string endpoint = "/api/method/login";
            var url = $"{_config.ErpUrl}{endpoint}";

            try
            {
                Log.Information("Attempting login for user: {Username}", username);

                // Clear any existing session
                ClearSession();

                // Prepare login data
                var loginData = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("usr", username),
                    new KeyValuePair<string, string>("pwd", password)
                };

                var formContent = new FormUrlEncodedContent(loginData);

                var response = await _httpClient.PostAsync(url, formContent);
                var responseContent = await response.Content.ReadAsStringAsync();

                LoggerService.LogApiCall(endpoint, "POST", response.IsSuccessStatusCode, 
                    response.IsSuccessStatusCode ? null : responseContent);

                // Handle session expiration
                if (await HandleSessionExpiration(response.StatusCode))
                {
                    return false;
                }

                if (response.IsSuccessStatusCode)
                {
                    // Extract session ID from Set-Cookie header
                    if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
                    {
                        foreach (var cookie in cookies)
                        {
                            if (cookie.StartsWith("sid="))
                            {
                                var sidValue = cookie.Split(';')[0].Substring(4); // Remove "sid=" prefix
                                _sessionId = sidValue;
                                _isSessionAuthenticated = true;
                                
                                // Remove API key authorization and set up session authentication
                                _httpClient.DefaultRequestHeaders.Remove("Authorization");
                                
                                Log.Information("Login successful for user: {Username}, Session ID: {SessionId}", username, _sessionId?.Substring(0, 8) + "...");
                                return true;
                            }
                        }
                    }

                    Log.Warning("Login response was successful but no session ID found in cookies");
                    return false;
                }

                Log.Warning("Login failed for user: {Username}, Status: {StatusCode}, Response: {Response}", 
                    username, response.StatusCode, responseContent);
                return false;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during login for user: {Username}", username);
                return false;
            }
        }

        public async Task<bool> LogoutAsync()
        {
            if (!_isSessionAuthenticated || string.IsNullOrEmpty(_sessionId))
            {
                Log.Information("No active session to logout");
                return true;
            }

            const string endpoint = "/api/method/logout";
            var url = $"{_config.ErpUrl}{endpoint}";

            try
            {
                Log.Information("Logging out current session");

                var response = await _httpClient.PostAsync(url, null);
                var responseContent = await response.Content.ReadAsStringAsync();

                LoggerService.LogApiCall(endpoint, "POST", response.IsSuccessStatusCode, 
                    response.IsSuccessStatusCode ? null : responseContent);

                // Clear session regardless of response status
                ClearSession();

                if (response.IsSuccessStatusCode)
                {
                    Log.Information("Logout successful");
                    return true;
                }

                Log.Warning("Logout request failed but session cleared locally: {Response}", responseContent);
                return true; // Return true since we cleared the session locally
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during logout");
                ClearSession(); // Clear session even if logout request failed
                return true;
            }
        }

        private void ClearSession()
        {
            _sessionId = null;
            _isSessionAuthenticated = false;
            
            // Remove any existing cookie headers
            _httpClient.DefaultRequestHeaders.Remove("Cookie");
            
            Log.Debug("Session cleared");
        }

        private void EnsureSessionAuthentication()
        {
            if (_isSessionAuthenticated && !string.IsNullOrEmpty(_sessionId))
            {
                // Remove existing cookie header if present
                _httpClient.DefaultRequestHeaders.Remove("Cookie");
                
                // Add session cookie
                _httpClient.DefaultRequestHeaders.Add("Cookie", $"sid={_sessionId}");
            }
        }

        private async Task<bool> HandleSessionExpiration(System.Net.HttpStatusCode statusCode)
        {
            if (statusCode == System.Net.HttpStatusCode.Unauthorized || statusCode == System.Net.HttpStatusCode.Forbidden)
            {
                Log.Warning("Session appears to be expired (Status: {StatusCode}), clearing session", statusCode);
                ClearSession();
                return true; // Indicates session was expired
            }
            return false;
        }

        public async Task<ApiResult<List<Employee>>> GetEmployeesAsync()
        {
            const string endpoint = "/api/resource/Employee";
            const string fields = "[\"name\",\"employee_name\",\"department\",\"designation\",\"custom_fingerprint_template\"]";
            const string filters = "[[\"status\",\"=\",\"Active\"]]";
            var url = $"{_config.ErpUrl}{endpoint}?fields={Uri.EscapeDataString(fields)}&filters={Uri.EscapeDataString(filters)}&limit_page_length=0";

            try
            {
                Log.Information("Fetching employees from ERPNext: {Url}", url);
                
                // Ensure session authentication is set
                EnsureSessionAuthentication();
                
                var response = await _httpClient.GetAsync(url);
                var content = await response.Content.ReadAsStringAsync();

                LoggerService.LogApiCall(endpoint, "GET", response.IsSuccessStatusCode, 
                    response.IsSuccessStatusCode ? null : content);

                // Handle session expiration
                if (await HandleSessionExpiration(response.StatusCode))
                {
                    return ApiResult<List<Employee>>.Failure("Session expired. Please login again.");
                }

                if (response.IsSuccessStatusCode)
                {
                    var apiResponse = JsonHelper.DeserializeObject<ERPNextListResponse<Employee>>(content);
                    if (apiResponse?.Data != null)
                    {
                        Log.Information("Successfully fetched {Count} employees", apiResponse.Data.Count);
                        return ApiResult<List<Employee>>.Success(apiResponse.Data);
                    }
                }

                return ApiResponseHandler.HandleResponse<List<Employee>>(response, content);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to fetch employees from ERPNext");
                return ApiResult<List<Employee>>.Failure($"Network error: {ex.Message}");
            }
        }

        public async Task<ApiResult<Employee>> GetEmployeeAsync(string employeeId)
        {
            var endpoint = $"/api/resource/Employee/{employeeId}";
            var url = $"{_config.ErpUrl}{endpoint}";

            try
            {
                Log.Information("Fetching employee {EmployeeId} from ERPNext", employeeId);
                
                // Ensure session authentication is set
                EnsureSessionAuthentication();
                
                var response = await _httpClient.GetAsync(url);
                var content = await response.Content.ReadAsStringAsync();

                LoggerService.LogApiCall(endpoint, "GET", response.IsSuccessStatusCode, 
                    response.IsSuccessStatusCode ? null : content);

                // Handle session expiration
                if (await HandleSessionExpiration(response.StatusCode))
                {
                    return ApiResult<Employee>.Failure("Session expired. Please login again.");
                }

                if (response.IsSuccessStatusCode)
                {
                    var apiResponse = JsonHelper.DeserializeObject<ERPNextSingleResponse<Employee>>(content);
                    if (apiResponse?.Data != null)
                    {
                        Log.Information("Successfully fetched employee {EmployeeId}", employeeId);
                        return ApiResult<Employee>.Success(apiResponse.Data);
                    }
                }

                return ApiResponseHandler.HandleResponse<Employee>(response, content);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to fetch employee {EmployeeId} from ERPNext", employeeId);
                return ApiResult<Employee>.Failure($"Network error: {ex.Message}");
            }
        }

        public async Task<ApiResult<bool>> UpdateEmployeeFingerprintAsync(string employeeId, string fingerprintTemplate)
        {
            // Use standard ERPNext Employee doctype endpoint
            var endpoint = $"/api/resource/Employee/{employeeId}";
            var url = $"{_config.ErpUrl}{endpoint}";

            try
            {
                Log.Information("Updating fingerprint template for employee {EmployeeId} using standard ERPNext API", employeeId);
                Log.Debug("Original fingerprint template length: {Length} characters", fingerprintTemplate?.Length ?? 0);

                // Ensure session authentication is set
                EnsureSessionAuthentication();

                // Store the full fingerprint template without processing
                Log.Debug("Storing full fingerprint template length: {Length} characters", fingerprintTemplate?.Length ?? 0);

                var updateData = new Dictionary<string, object>
                {
                    ["custom_fingerprint_template"] = fingerprintTemplate
                };

                var json = JsonHelper.SerializeObject(updateData);
                Log.Debug("Request JSON: {Json}", json);
                
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PutAsync(url, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                Log.Debug("Response Status: {StatusCode}, Content: {Content}", response.StatusCode, responseContent);

                LoggerService.LogApiCall(endpoint, "PUT", response.IsSuccessStatusCode, 
                    response.IsSuccessStatusCode ? null : responseContent);

                // Handle session expiration
                if (await HandleSessionExpiration(response.StatusCode))
                {
                    return ApiResult<bool>.Failure("Session expired. Please login again.");
                }

                if (response.IsSuccessStatusCode)
                {
                    Log.Information("Successfully updated fingerprint template for employee {EmployeeId}", employeeId);
                    return ApiResult<bool>.Success(true);
                }

                return ApiResult<bool>.Failure($"Failed to update fingerprint: {responseContent}", response.StatusCode);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to update fingerprint for employee {EmployeeId}", employeeId);
                return ApiResult<bool>.Failure($"Network error: {ex.Message}");
            }
        }



        private string CompressFingerprintTemplate(string fingerprintTemplate)
        {
            try
            {
                if (string.IsNullOrEmpty(fingerprintTemplate))
                    return fingerprintTemplate;

                // Convert base64 to bytes
                var originalBytes = Convert.FromBase64String(fingerprintTemplate);
                
                // Compress using GZip
                using (var memoryStream = new MemoryStream())
                {
                    using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Compress))
                    {
                        gzipStream.Write(originalBytes, 0, originalBytes.Length);
                    }
                    
                    var compressedBytes = memoryStream.ToArray();
                    var compressedBase64 = Convert.ToBase64String(compressedBytes);
                    
                    Log.Debug("Compression: {Original} -> {Compressed} chars ({Ratio:P1})", 
                        fingerprintTemplate.Length, compressedBase64.Length, 
                        (double)compressedBase64.Length / fingerprintTemplate.Length);
                    
                    return compressedBase64;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to compress fingerprint template, using original");
                return fingerprintTemplate;
            }
        }

        public async Task<ApiResult<VerificationResult>> VerifyFingerprintAndCreateDeductionAsync(string fingerprintTemplate, string deductionType, decimal amount)
        {
            const string endpoint = "/api/method/demoapp.api.fingerprint.verify";
            var url = $"{_config.ErpUrl}{endpoint}";

            try
            {
                Log.Information("Verifying fingerprint and creating deduction - Type: {DeductionType}, Amount: {Amount}", deductionType, amount);
                Log.Debug("Fingerprint template being sent - Length: {Length}, First 100 chars: {Template}", 
                    fingerprintTemplate?.Length ?? 0, 
                    fingerprintTemplate?.Length > 100 ? fingerprintTemplate.Substring(0, 100) + "..." : fingerprintTemplate);

                // Ensure session authentication is set
                EnsureSessionAuthentication();

                var verificationData = new
                {
                    fingerprint_template = fingerprintTemplate,
                    deduction_type = deductionType,
                    amount = amount
                };

                var json = JsonHelper.SerializeObject(verificationData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                LoggerService.LogApiCall(endpoint, "POST", response.IsSuccessStatusCode, 
                    response.IsSuccessStatusCode ? null : responseContent);

                if (response.IsSuccessStatusCode)
                {
                    Log.Debug("API Response Content: {Content}", responseContent);
                    
                    // Try to parse as nested message structure first: {"message": {"success": false, "message": "..."}}
                    try
                    {
                        var nestedResponse = JsonHelper.DeserializeObject<dynamic>(responseContent);
                        if (nestedResponse?.message != null)
                        {
                            var messageContent = nestedResponse.message.ToString();
                            Log.Debug("Extracted message content: {MessageContent}", messageContent);
                            var verificationResult = JsonHelper.DeserializeObject<VerificationResult>(messageContent);
                            if (verificationResult != null)
                            {
                                if (verificationResult.Success)
                                {
                                    Log.Information("Fingerprint verification and deduction creation successful - Employee: {Employee}, Deduction ID: {DeductionId}", 
                                        verificationResult.Employee, verificationResult.DeductionId);
                                }
                                else
                                {
                                    Log.Warning("Fingerprint verification failed: {Message}", verificationResult.Message);
                                }
                                return ApiResult<VerificationResult>.Success(verificationResult);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("Failed to parse as nested message response: {Error}", ex.Message);
                    }
                    
                    // Try to parse as ERPNext wrapped response
                    try
                    {
                        var apiResponse = JsonHelper.DeserializeObject<ERPNextSingleResponse<VerificationResult>>(responseContent);
                        if (apiResponse?.Data != null)
                        {
                            Log.Information("Fingerprint verification and deduction creation successful - Employee: {Employee}, Deduction ID: {DeductionId}", 
                                apiResponse.Data.Employee, apiResponse.Data.DeductionId);
                            return ApiResult<VerificationResult>.Success(apiResponse.Data);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("Failed to parse as wrapped response: {Error}", ex.Message);
                    }
                    
                    // Handle direct response format (not wrapped in ERPNext response structure)
                    try
                    {
                        var directResponse = JsonHelper.DeserializeObject<VerificationResult>(responseContent);
                        if (directResponse != null)
                        {
                            Log.Information("Fingerprint verification and deduction creation successful - Employee: {Employee}, Deduction ID: {DeductionId}", 
                                directResponse.Employee, directResponse.DeductionId);
                            return ApiResult<VerificationResult>.Success(directResponse);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("Failed to parse as direct response: {Error}", ex.Message);
                    }
                }

                Log.Error("Fingerprint verification failed with status {StatusCode}: {Content}", response.StatusCode, responseContent);
                return ApiResult<VerificationResult>.Failure($"Verification failed: {responseContent}", response.StatusCode);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to verify fingerprint and create deduction");
                return ApiResult<VerificationResult>.Failure($"Network error: {ex.Message}");
            }
        }

        public async Task<ApiResult<string>> CreateDeductionAsync(string employeeId, string deductionType, decimal amount, string description)
        {
            const string endpoint = "/api/resource/Deductions";
            var url = $"{_config.ErpUrl}{endpoint}";

            try
            {
                Log.Information("Creating deduction for employee {EmployeeId} - Type: {DeductionType}, Amount: {Amount}", 
                    employeeId, deductionType, amount);

                // Ensure session authentication is set
                EnsureSessionAuthentication();

                var deductionData = new
                {
                    employee = employeeId,
                    deduction_type = deductionType,
                    amount = amount
                };

                var json = JsonConvert.SerializeObject(deductionData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                LoggerService.LogApiCall(endpoint, "POST", response.IsSuccessStatusCode, 
                    response.IsSuccessStatusCode ? null : responseContent);

                // Handle session expiration
                if (await HandleSessionExpiration(response.StatusCode))
                {
                    return ApiResult<string>.Failure("Session expired. Please login again.");
                }

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonConvert.DeserializeObject<ERPNextSingleResponse<dynamic>>(responseContent);
                    var deductionId = result?.Data?.name?.ToString() ?? "Unknown";
                    
                    Log.Information("Deduction created successfully for employee {EmployeeId} with ID: {DeductionId}", 
                        employeeId, deductionId);
                    
                    return ApiResult<string>.Success(deductionId);
                }

                Log.Error("Failed to create deduction for employee {EmployeeId}: {Response}", employeeId, responseContent);
                return ApiResult<string>.Failure($"Failed to create deduction: {responseContent}", response.StatusCode);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error creating deduction for employee {EmployeeId}", employeeId);
                return ApiResult<string>.Failure($"Error creating deduction: {ex.Message}");
            }
        }

        public async Task<ApiResult<bool>> TestConnectionAsync()
        {
            const string endpoint = "/api/method/frappe.auth.get_logged_user";
            var url = $"{_config.ErpUrl}{endpoint}";

            try
            {
                Log.Information("Testing ERPNext connection");
                
                // Ensure session authentication is set
                EnsureSessionAuthentication();
                
                var response = await _httpClient.GetAsync(url);
                var content = await response.Content.ReadAsStringAsync();

                LoggerService.LogApiCall(endpoint, "GET", response.IsSuccessStatusCode, 
                    response.IsSuccessStatusCode ? null : content);

                // Handle session expiration
                if (await HandleSessionExpiration(response.StatusCode))
                {
                    return ApiResult<bool>.Failure("Session expired. Please login again.");
                }

                if (response.IsSuccessStatusCode)
                {
                    Log.Information("ERPNext connection test successful");
                    return ApiResult<bool>.Success(true);
                }

                return ApiResult<bool>.Failure($"Connection test failed: {content}", response.StatusCode);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ERPNext connection test failed");
                return ApiResult<bool>.Failure($"Connection error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _httpClient?.Dispose();
                _disposed = true;
            }
        }
    }

    public class ERPNextListResponse<T>
    {
        [JsonProperty("data")]
        public List<T>? Data { get; set; }
    }

    public class ERPNextSingleResponse<T>
    {
        [JsonProperty("data")]
        public T? Data { get; set; }
    }
}