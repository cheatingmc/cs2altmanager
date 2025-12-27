using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CacheLoginToolWPF
{
    public static class NiceAltsApiHelper
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private const string ApiKeyFilePath = "nicealts_api_key.txt";
        private const string ApiBaseUrl = "https://api.nicealts.com";

        public static string? GetApiKey()
        {
            try
            {
                var configDir = AppDataHelper.GetConfigPath();
                Directory.CreateDirectory(configDir); // Ensure it exists
                var keyPath = Path.Combine(configDir, ApiKeyFilePath);

                if (File.Exists(keyPath))
                {
                    return File.ReadAllText(keyPath).Trim();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading API key: {ex.Message}");
            }
            return null;
        }

        public static void SaveApiKey(string apiKey)
        {
            try
            {
                var configDir = AppDataHelper.GetConfigPath();
                Directory.CreateDirectory(configDir); // Ensure it exists
                var keyPath = Path.Combine(configDir, ApiKeyFilePath);
                File.WriteAllText(keyPath, apiKey.Trim());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving API key: {ex.Message}");
                throw;
            }
        }

        public static async Task<(bool Success, string? AccountData, string? Error)> GenerateAccountAsync(
            string accountType = "banned", 
            int amount = 1, 
            string precheck = "full")
        {
            try
            {
                var apiKey = GetApiKey();
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    return (false, null, "API key not set. Please set your API key first.");
                }

                var queryParams = $"key={Uri.EscapeDataString(apiKey)}&type={Uri.EscapeDataString(accountType)}&amount={amount}&precheck={Uri.EscapeDataString(precheck)}";
                var url = $"{ApiBaseUrl}/v1/generate?{queryParams}";
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("Accept", "application/json");
                request.Headers.Add("User-Agent", "CS2AltManager/1.0");

                var response = await _httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    string errorMessage = $"HTTP {response.StatusCode}";
                    try
                    {
                        var errorJson = JsonDocument.Parse(responseContent);
                        if (errorJson.RootElement.TryGetProperty("detail", out var detail))
                        {
                            errorMessage = detail.GetString() ?? errorMessage;
                        }
                        else if (errorJson.RootElement.TryGetProperty("error", out var error))
                        {
                            errorMessage = error.GetString() ?? errorMessage;
                        }
                    }
                    catch { }

                    return (false, null, errorMessage);
                }

                var accountData = responseContent.Trim();
                if (string.IsNullOrEmpty(accountData))
                {
                    return (false, null, "Empty response from API");
                }

                return (true, accountData, null);
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        public static string? ParseSteamAccountFromResponse(string accountData)
        {
            try
            {

                var lines = accountData.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {

                    var steamTokenPattern = @"(ey[A-Za-z0-9\-_]+\.ey[A-Za-z0-9\-_]+\.ey[A-Za-z0-9\-_]+)";
                    var tokenMatch = Regex.Match(line, steamTokenPattern);

                    if (tokenMatch.Success)
                    {
                        var token = tokenMatch.Groups[1].Value;

                        var emailMatch = Regex.Match(line, @"([a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,})");
                        if (emailMatch.Success)
                        {
                            var email = emailMatch.Groups[1].Value;
                            var username = email.Split('@')[0];
                            return $"{username}----{token}";
                        }

                        var usernameMatch = Regex.Match(line, @"([a-zA-Z0-9_]+)[:\s]+.*?(?=mctoken|token|Accesstoken)", RegexOptions.IgnoreCase);
                        if (usernameMatch.Success)
                        {
                            var username = usernameMatch.Groups[1].Value;
                            return $"{username}----{token}";
                        }
                    }

                    if (line.Contains("----"))
                    {
                        var parts = line.Split(new[] { "----" }, StringSplitOptions.None);
                        if (parts.Length >= 2)
                        {
                            var token = parts[1].Trim();

                            if (token.StartsWith("ey") && token.Split('.').Length == 3)
                            {
                                return line.Trim();
                            }
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing account data: {ex.Message}");
                return null;
            }
        }
    }
}

