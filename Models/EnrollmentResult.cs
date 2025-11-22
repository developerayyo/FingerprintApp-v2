using System;
using System.Collections.Generic;
using System.Linq;
using DPUruNet;

namespace ERPNextFingerprintApp.Models
{
    /// <summary>
    /// Result of fingerprint enrollment operation using DigitalPersona EnrollmentControl
    /// </summary>
    public class EnrollmentResult
    {
        public bool IsSuccess { get; set; }
        public string ErrorMessage { get; set; }
        public List<Fmd> Templates { get; set; } = new List<Fmd>();
        public string Template { get; set; } = string.Empty;
        public int CapturedScans { get; set; }
        public Constants.ResultCode ResultCode { get; set; }

        public static EnrollmentResult Success(List<Fmd> templates, int capturedScans)
        {
            var templateString = string.Empty;
            
            // Convert Fmd templates to Base64 string
            if (templates != null && templates.Count > 0)
            {
                try
                {
                    // Create enrollment template from multiple Fmd templates
                    var enrollmentResult = Enrollment.CreateEnrollmentFmd(Constants.Formats.Fmd.ANSI, templates);
                    if (enrollmentResult.ResultCode == Constants.ResultCode.DP_SUCCESS && enrollmentResult.Data != null)
                    {
                        // Convert the enrollment Fmd to Base64 string
                        templateString = Convert.ToBase64String(enrollmentResult.Data.Bytes);
                    }
                    else
                    {
                        // Fallback: use the first template if enrollment creation fails
                        templateString = Convert.ToBase64String(templates.First().Bytes);
                    }
                }
                catch (Exception)
                {
                    // Fallback: use the first template if any error occurs
                    if (templates.Count > 0)
                    {
                        templateString = Convert.ToBase64String(templates.First().Bytes);
                    }
                }
            }

            return new EnrollmentResult
            {
                IsSuccess = true,
                Templates = templates ?? new List<Fmd>(),
                Template = templateString,
                CapturedScans = capturedScans,
                ResultCode = Constants.ResultCode.DP_SUCCESS
            };
        }

        public static EnrollmentResult Success(string template, int capturedScans)
        {
            return new EnrollmentResult
            {
                IsSuccess = true,
                Templates = new List<Fmd>(),
                Template = template,
                CapturedScans = capturedScans,
                ResultCode = Constants.ResultCode.DP_SUCCESS
            };
        }

        public static EnrollmentResult Failure(string errorMessage, Constants.ResultCode resultCode = Constants.ResultCode.DP_FAILURE)
        {
            return new EnrollmentResult
            {
                IsSuccess = false,
                ErrorMessage = errorMessage,
                Templates = new List<Fmd>(),
                CapturedScans = 0,
                ResultCode = resultCode
            };
        }
    }
}