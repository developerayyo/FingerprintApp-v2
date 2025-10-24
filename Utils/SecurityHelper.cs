using System;
using System.Security.Cryptography;
using System.Text;

namespace ERPNextFingerprintApp.Utils
{
    public static class SecurityHelper
    {
        /// <summary>
        /// Computes SHA256 hash of the input string
        /// </summary>
        public static string ComputeSHA256Hash(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            using var sha256 = SHA256.Create();
            var inputBytes = Encoding.UTF8.GetBytes(input);
            var hashBytes = sha256.ComputeHash(inputBytes);
            return Convert.ToHexString(hashBytes);
        }
    }
}