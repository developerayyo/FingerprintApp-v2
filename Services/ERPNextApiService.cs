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

        public ERPNextApiService(Config config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            
            _httpClient = new HttpClient()
            {
                Timeout = TimeSpan.FromSeconds(_config.ConnectionTimeout)
            };

            _httpClient.DefaultRequestHeaders.Add("Authorization", _config.AuthorizationHeader);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "ERPNext-Fingerprint-App/1.0");
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
                
                var response = await _httpClient.GetAsync(url);
                var content = await response.Content.ReadAsStringAsync();

                LoggerService.LogApiCall(endpoint, "GET", response.IsSuccessStatusCode, 
                    response.IsSuccessStatusCode ? null : content);

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
                
                var response = await _httpClient.GetAsync(url);
                var content = await response.Content.ReadAsStringAsync();

                LoggerService.LogApiCall(endpoint, "GET", response.IsSuccessStatusCode, 
                    response.IsSuccessStatusCode ? null : content);

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

        private string ProcessFingerprintForStorage(string fingerprintTemplate)
        {
            try
            {
                if (string.IsNullOrEmpty(fingerprintTemplate))
                    return fingerprintTemplate;

                Log.Debug("Processing fingerprint template for storage. Original length: {Length}", fingerprintTemplate.Length);

                // First, try compression
                var compressed = CompressFingerprintTemplate(fingerprintTemplate);
                Log.Debug("After compression: {Length} characters", compressed?.Length ?? 0);

                // If still too long for ERPNext custom field, truncate with hash
                const int maxFieldLength = 140; // Conservative limit for ERPNext custom fields
                
                if (compressed.Length <= maxFieldLength)
                {
                    Log.Debug("Compressed template fits in field limit");
                    return compressed;
                }

                // If compression isn't enough, create a truncated version with hash for verification
                var truncated = compressed.Substring(0, maxFieldLength - 32); // Reserve 32 chars for hash
                var hash = SecurityHelper.ComputeSHA256Hash(fingerprintTemplate).Substring(0, 32);
                var result = truncated + hash;

                Log.Warning("Fingerprint template too large ({Length} chars), truncated to {TruncatedLength} chars with hash", 
                    compressed.Length, result.Length);

                return result;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to process fingerprint template, using truncated original");
                // Fallback: just truncate the original if all else fails
                const int fallbackLength = 140;
                return fingerprintTemplate.Length > fallbackLength 
                    ? fingerprintTemplate.Substring(0, fallbackLength)
                    : fingerprintTemplate;
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

        public async Task<ApiResult<bool>> CreateDeductionRecordAsync(DeductionRecord deduction)
        { 
            const string endpoint = "/api/resource/Employee Deduction";
            var url = $"{_config.ErpUrl}{endpoint}";

            try
            {
                Log.Information("Creating deduction record for employee {Employee}", deduction.Employee);

                var deductionData = new
                {
                    employee = deduction.Employee,
                    deduction_type = deduction.DeductionType.ToString().ToUpper(),
                    amount = deduction.Amount,
                    description = deduction.Description,
                    transaction_date = deduction.Timestamp.ToString("yyyy-MM-dd"),
                    transaction_time = deduction.Timestamp.ToString("HH:mm:ss")
                };

                var json = JsonHelper.SerializeObject(deductionData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                LoggerService.LogApiCall(endpoint, "POST", response.IsSuccessStatusCode, 
                    response.IsSuccessStatusCode ? null : responseContent);

                if (response.IsSuccessStatusCode)
                {
                    LoggerService.LogDeductionProcessing(deduction, true);
                    return ApiResult<bool>.Success(true);
                }

                LoggerService.LogDeductionProcessing(deduction, false, responseContent);
                return ApiResult<bool>.Failure($"Failed to create deduction: {responseContent}", response.StatusCode);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to create deduction record for employee {Employee}", deduction.Employee);
                LoggerService.LogDeductionProcessing(deduction, false, ex.Message);
                return ApiResult<bool>.Failure($"Network error: {ex.Message}");
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
                
                var response = await _httpClient.GetAsync(url);
                var content = await response.Content.ReadAsStringAsync();

                LoggerService.LogApiCall(endpoint, "GET", response.IsSuccessStatusCode, 
                    response.IsSuccessStatusCode ? null : content);

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