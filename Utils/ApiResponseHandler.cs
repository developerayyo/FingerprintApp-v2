using System;
using System.Net;
using System.Net.Http;
using Newtonsoft.Json;
using Serilog;

namespace ERPNextFingerprintApp.Utils
{
    public static class ApiResponseHandler
    {
        public static ApiResult<T> HandleResponse<T>(HttpResponseMessage response, string content) where T : class
        {
            try
            {
                if (response.IsSuccessStatusCode)
                {
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        return ApiResult<T>.Success(default);
                    }

                    var data = JsonHelper.DeserializeObject<T>(content);
                    return ApiResult<T>.Success(data);
                }

                var errorMessage = $"API request failed with status {response.StatusCode}: {response.ReasonPhrase}";
                
                if (!string.IsNullOrWhiteSpace(content))
                {
                    try
                    {
                        var errorResponse = JsonHelper.DeserializeObject<ErrorResponse>(content);
                        if (errorResponse?.Message != null)
                        {
                            errorMessage = errorResponse.Message;
                        }
                    }
                    catch
                    {
                        // If we can't parse the error response, use the original content
                        errorMessage = content;
                    }
                }

                Log.Error("API Error: {StatusCode} - {ErrorMessage}", response.StatusCode, errorMessage);
                return ApiResult<T>.Failure(errorMessage, response.StatusCode);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to handle API response");
                return ApiResult<T>.Failure($"Failed to process response: {ex.Message}");
            }
        }


    }

    public class ApiResult<T>
    {
        public bool IsSuccess { get; private set; }
        public T? Data { get; private set; }
        public string ErrorMessage { get; private set; } = string.Empty;
        public HttpStatusCode? StatusCode { get; private set; }

        private ApiResult() { }

        public static ApiResult<T> Success(T? data)
        {
            return new ApiResult<T>
            {
                IsSuccess = true,
                Data = data
            };
        }

        public static ApiResult<T> Failure(string errorMessage, HttpStatusCode? statusCode = null)
        {
            return new ApiResult<T>
            {
                IsSuccess = false,
                ErrorMessage = errorMessage,
                StatusCode = statusCode
            };
        }
    }

    public class ErrorResponse
    {
        [JsonProperty("message")]
        public string? Message { get; set; }

        [JsonProperty("exc")]
        public string? Exception { get; set; }

        [JsonProperty("exc_type")]
        public string? ExceptionType { get; set; }
    }
}