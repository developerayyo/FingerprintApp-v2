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
                BaseAddress = new Uri(_config.ErpUrl),
                Timeout = TimeSpan.FromSeconds(_config.ConnectionTimeout)
            };
            
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "ERPNext-Fingerprint-App/1.0");
        }

        public async Task<bool> LoginAsync(string username, string password)
        {
            const string endpoint = "/api/method/login";

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

                var response = await _httpClient.PostAsync(endpoint, formContent);
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

            try
            {
                Log.Information("Logging out current session");

                var response = await _httpClient.PostAsync(endpoint, null);
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
            var url = $"{endpoint}?fields={Uri.EscapeDataString(fields)}&filters={Uri.EscapeDataString(filters)}&limit_page_length=0";

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

            try
            {
                Log.Information("Fetching employee {EmployeeId} from ERPNext", employeeId);
                
                // Ensure session authentication is set
                EnsureSessionAuthentication();
                
                var response = await _httpClient.GetAsync(endpoint);
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

                var response = await _httpClient.PutAsync(endpoint, content);
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

                var response = await _httpClient.PostAsync(endpoint, content);
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

                var response = await _httpClient.PostAsync(endpoint, content);
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

            try
            {
                Log.Information("Testing ERPNext connection");
                
                // Ensure session authentication is set
                EnsureSessionAuthentication();
                
                var response = await _httpClient.GetAsync(endpoint);
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

        /// <summary>
        /// Fetches unused Ticket for a specific employee from ERPNext
        /// </summary>
        /// <param name="employeeId">The employee ID to fetch Ticket for</param>
        /// <returns>List of unused Ticket for the employee</returns>
        public async Task<ApiResult<List<Ticket>>> GetUnusedTicketsAsync(string employeeId)
        {
            try
            {
                Log.Information("Fetching unused Ticket for employee: {EmployeeId}", employeeId);

                var fields = "[\"name\",\"employee\",\"employee_name\",\"amount\",\"ticket_type\",\"status\"]";
                var filters = $"[[\"employee\",\"=\",\"{employeeId}\"],[\"status\",\"=\",\"Unused\"]]";
                var url = $"/api/resource/Ticket?fields={Uri.EscapeDataString(fields)}&filters={Uri.EscapeDataString(filters)}";

                var response = await _httpClient.GetAsync(url);
                var content = await response.Content.ReadAsStringAsync();

                Log.Information("Ticket API response status: {StatusCode}", response.StatusCode);
                Log.Debug("Ticket API response content: {Content}", content);

                if (response.IsSuccessStatusCode)
                {
                    var apiResponse = JsonConvert.DeserializeObject<ERPNextListResponse<Ticket>>(content);
                    var tickets = apiResponse?.Data ?? new List<Ticket>();
                    
                    Log.Information("Successfully fetched {Count} unused tickets for employee {EmployeeId}", 
                        tickets.Count, employeeId);
                    
                    return ApiResult<List<Ticket>>.Success(tickets);
                }
                else
                {
                    var errorMessage = $"Failed to fetch Ticket. Status: {response.StatusCode}, Content: {content}";
                    Log.Error("Failed to fetch Ticket for employee {EmployeeId}: {Error}", employeeId, errorMessage);
                    return ApiResult<List<Ticket>>.Failure(errorMessage);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error fetching Ticket for employee {EmployeeId}", employeeId);
                return ApiResult<List<Ticket>>.Failure($"Error fetching Ticket: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates a single ticket status to "Used" in ERPNext
        /// </summary>
        /// <param name="ticketId">The ticket ID to update</param>
        /// <param name="usedBy">The user who used the ticket</param>
        /// <returns>Success or failure result</returns>
        public async Task<ApiResult<string>> UseTicketAsync(string ticketId, string usedBy)
        {
            try
            {
                Log.Information("Updating ticket {TicketId} as used by {UsedBy}", ticketId, usedBy);

                var updateData = new
                {
                    status = "Used",
                    custom_used_inn = usedBy
                };

                var json = JsonConvert.SerializeObject(updateData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var url = $"/api/resource/Ticket/{ticketId}";
                var response = await _httpClient.PutAsync(url, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                Log.Information("Update ticket API response status: {StatusCode}", response.StatusCode);
                Log.Debug("Update ticket API response content: {Content}", responseContent);

                if (response.IsSuccessStatusCode)
                {
                    Log.Information("Successfully updated ticket {TicketId} as used by {UsedBy}", ticketId, usedBy);
                    return ApiResult<string>.Success($"Ticket {ticketId} used successfully");
                }
                else
                {
                    var errorMessage = $"Failed to update ticket. Status: {response.StatusCode}, Content: {responseContent}";
                    Log.Error("Failed to update ticket {TicketId}: {Error}", ticketId, errorMessage);
                    return ApiResult<string>.Failure(errorMessage);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error updating ticket {TicketId}", ticketId);
                return ApiResult<string>.Failure($"Error updating ticket: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates multiple tickets status to "Used" in ERPNext
        /// </summary>
        /// <param name="tickets">List of tickets to update</param>
        /// <param name="usedBy">The user who used the tickets</param>
        /// <returns>Success or failure result with summary</returns>
        public async Task<ApiResult<string>> UseAllTicketsAsync(List<Ticket> tickets, string usedBy)
        {
            try
            {
                Log.Information("Updating {Count} tickets as used by {UsedBy}", tickets.Count, usedBy);

                var successCount = 0;
                var failedTickets = new List<string>();
                var totalAmount = tickets.Sum(t => t.Amount);

                foreach (var ticket in tickets)
                {
                    var result = await UseTicketAsync(ticket.Name, usedBy);
                    if (result.IsSuccess)
                    {
                        successCount++;
                    }
                    else
                    {
                        failedTickets.Add(ticket.Name);
                        Log.Warning("Failed to update ticket {TicketId}: {Error}", ticket.Name, result.ErrorMessage);
                    }
                }

                if (failedTickets.Any())
                {
                    var errorMessage = $"Updated {successCount}/{tickets.Count} tickets. Failed tickets: {string.Join(", ", failedTickets)}";
                    Log.Warning("Partial success updating tickets: {Message}", errorMessage);
                    return ApiResult<string>.Failure(errorMessage);
                }
                else
                {
                    var successMessage = $"All {successCount} tickets used successfully (₦{totalAmount:N2} total)";
                    Log.Information("Successfully updated all tickets: {Message}", successMessage);
                    return ApiResult<string>.Success(successMessage);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error updating multiple tickets");
                return ApiResult<string>.Failure($"Error updating tickets: {ex.Message}");
            }
        }

        public async Task<ApiResult<string>> GetCurrentUserAsync()
        {
            const string endpoint = "/api/method/frappe.auth.get_logged_user";

            try
            {
                Log.Information("Getting current logged user from ERPNext");
                
                // Ensure session authentication is set
                EnsureSessionAuthentication();
                
                var response = await _httpClient.GetAsync(endpoint);
                var content = await response.Content.ReadAsStringAsync();

                LoggerService.LogApiCall(endpoint, "GET", response.IsSuccessStatusCode, 
                    response.IsSuccessStatusCode ? null : content);

                // Handle session expiration
                if (await HandleSessionExpiration(response.StatusCode))
                {
                    return ApiResult<string>.Failure("Session expired. Please login again.");
                }

                if (response.IsSuccessStatusCode)
                {
                    var jsonResponse = JsonConvert.DeserializeObject<dynamic>(content);
                    string? userId = jsonResponse?.message;
                    
                    if (!string.IsNullOrEmpty(userId))
                    {
                        Log.Information("Successfully retrieved current user: {UserId}", userId);
                        return ApiResult<string>.Success(userId);
                    }
                    else
                    {
                        Log.Warning("No user ID returned from ERPNext");
                        return ApiResult<string>.Failure("No user ID returned from ERPNext");
                    }
                }
                else
                {
                    Log.Error("Failed to get current user. Status: {StatusCode}, Content: {Content}", 
                        response.StatusCode, content);
                    return ApiResult<string>.Failure($"Failed to get current user: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error getting current user from ERPNext");
                return ApiResult<string>.Failure($"Error getting current user: {ex.Message}");
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