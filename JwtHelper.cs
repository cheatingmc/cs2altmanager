using System;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CacheLoginToolWPF
{
    public static class JwtHelper
    {
        public static string DecodeSteamId(string token)
        {
            try
            {
                var parts = token.Split('.');
                if (parts.Length != 3)
                    return string.Empty;

                var payload = parts[1];
                var padding = payload.Length % 4;
                if (padding != 0)
                {
                    payload += new string('=', 4 - padding);
                }

                var payloadBytes = Convert.FromBase64String(payload);
                var payloadJson = Encoding.UTF8.GetString(payloadBytes);
                var jsonDoc = JsonDocument.Parse(payloadJson);

                if (jsonDoc.RootElement.TryGetProperty("sub", out var subElement))
                {
                    return subElement.GetString() ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error decoding JWT: {ex.Message}");
            }

            return string.Empty;
        }
    }
}

