using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Win32;
using System.Collections.Generic;

namespace CacheLoginToolWPF
{
    public static class ConfigHelper
    {
        public static (string, string) DoLogin(string accountName, string token, bool startSteam = true)
        {
            try
            {
                if (accountName.Contains('@'))
                {
                    accountName = accountName.Split('@')[0];
                }

                var crc32AccountName = ComputeCrc32(accountName) + "1";
                var jsonData = ParseEya(token);
                if (string.IsNullOrEmpty(jsonData))
                    return ("Error", "Invalid token - failed to parse JWT");

                var jsonDoc = JsonDocument.Parse(jsonData);
                if (!jsonDoc.RootElement.TryGetProperty("sub", out var subElement))
                {
                    return ("Error", "Invalid token - missing 'sub' field");
                }

                var steamId = subElement.GetString() ?? string.Empty;
                if (string.IsNullOrEmpty(steamId))
                {
                    return ("Error", "Invalid token - Steam ID is empty");
                }

                KillSteam();
                Thread.Sleep(1000); // Wait for Steam to fully close and release file locks

                var random = new Random();
                var mtbf = new string(Enumerable.Range(0, 9).Select(_ => (char)('0' + random.Next(10))).ToArray());
                var jwt = SteamEncrypt(token, accountName);
                var path = GetSteamInstallPath();

                if (string.IsNullOrEmpty(path))
                {
                    return ("Error", "Steam installation path not found");
                }

                var localVdfPath = GetLocalVdfPath();

                if (File.Exists(localVdfPath))
                {
                    try
                    {
                        File.SetAttributes(localVdfPath, FileAttributes.Normal);
                        File.Delete(localVdfPath);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Warning: Could not delete local VDF: {ex.Message}");

                        Thread.Sleep(500);
                        try
                        {
                            File.SetAttributes(localVdfPath, FileAttributes.Normal);
                            File.Delete(localVdfPath);
                        }
                        catch
                        {
                            return ("Error", $"Cannot delete existing local VDF file. Steam may still be running. Error: {ex.Message}");
                        }
                    }
                }

                Directory.CreateDirectory(Path.Combine(path, "config"));

                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam", true);
                    if (key == null)
                    {
                        return ("Error", "Could not access Steam registry key");
                    }
                    key.SetValue("AutoLoginUser", accountName, RegistryValueKind.String);
                }
                catch (Exception ex)
                {
                    return ("Error", $"Failed to set registry: {ex.Message}");
                }

                var config = BuildConfig(mtbf, steamId, accountName);
                var loginUsers = BuildLoginUsers(steamId, accountName);
                var local = BuildLocal(crc32AccountName, jwt);

                var configPath = Path.Combine(path, "config", "config.vdf");
                var loginUsersPath = Path.Combine(path, "config", "loginusers.vdf");

                try
                {

                    for (int retry = 0; retry < 3; retry++)
                    {
                        try
                        {
                            if (File.Exists(configPath))
                            {
                                File.SetAttributes(configPath, FileAttributes.Normal);
                            }
                            File.WriteAllText(configPath, config, Encoding.UTF8);
                            break; // Success, exit retry loop
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            if (retry == 2) throw; // Last retry failed
                            KillSteam(); // Kill Steam again
                            Thread.Sleep(1000);
                        }
                    }

                    for (int retry = 0; retry < 3; retry++)
                    {
                        try
                        {
                            if (File.Exists(loginUsersPath))
                            {
                                File.SetAttributes(loginUsersPath, FileAttributes.Normal);
                            }
                            File.WriteAllText(loginUsersPath, loginUsers, Encoding.UTF8);
                            break; // Success, exit retry loop
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            if (retry == 2) throw; // Last retry failed
                            KillSteam(); // Kill Steam again
                            Thread.Sleep(1000);
                        }
                    }

                    for (int retry = 0; retry < 3; retry++)
                    {
                        try
                        {
                            if (File.Exists(localVdfPath))
                            {
                                File.SetAttributes(localVdfPath, FileAttributes.Normal);
                                File.Delete(localVdfPath);
                            }
                            File.WriteAllText(localVdfPath, local, Encoding.UTF8);
                            break; // Success, exit retry loop
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            if (retry == 2) throw; // Last retry failed
                            KillSteam(); // Kill Steam again
                            Thread.Sleep(1000);
                        }
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    return ("Error", $"Access denied. Please ensure Steam is closed and try again. If the problem persists, try running as administrator. Error: {ex.Message}");
                }
                catch (Exception ex)
                {
                    return ("Error", $"Failed to write config files: {ex.Message}");
                }

                if (startSteam)
                {
                    try
                    {
                        var steamExe = Path.Combine(path, "steam.exe");
                        if (!File.Exists(steamExe))
                        {
                            return ("Error", $"Steam executable not found at: {steamExe}");
                        }

                        Process.Start(new ProcessStartInfo
                        {
                            FileName = steamExe,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        return ("Error", $"Failed to start Steam: {ex.Message}");
                    }
                }

                return ("Success", "Login configured successfully.");
            }
            catch (Exception ex)
            {
                return ("Error", $"Login failed: {ex.Message}");
            }
        }

        private static string ParseEya(string eya)
        {
            var tokenArr = eya.Split('.');
            if (tokenArr.Length != 3)
                return string.Empty;

            var payload = tokenArr[1];
            var padding = payload.Length % 4;
            if (padding != 0)
            {
                payload += new string('=', 4 - padding);
            }

            try
            {
                var bytes = Convert.FromBase64String(payload);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string GetSteamInstallPath()
        {

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam");
                var installPath = key?.GetValue("SteamPath")?.ToString();
                if (!string.IsNullOrEmpty(installPath) && Directory.Exists(installPath))
                {
                    return installPath;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting Steam path from SteamPath registry: {ex.Message}");
            }

            var steamProcess = Process.GetProcessesByName("steam").FirstOrDefault();
            if (steamProcess != null)
            {
                try
                {
                    var steamPath = steamProcess.MainModule?.FileName ?? string.Empty;
                    if (!string.IsNullOrEmpty(steamPath))
                    {

                        var steamDir = Path.GetDirectoryName(steamPath);
                        if (!string.IsNullOrEmpty(steamDir) && Directory.Exists(steamDir))
                        {
                            return steamDir;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error getting Steam path from process: {ex.Message}");
                }
            }

            try
            {
                using var key = Registry.ClassesRoot.OpenSubKey(@"steam\Shell\Open\Command");
                var command = key?.GetValue("")?.ToString() ?? string.Empty;
                if (!string.IsNullOrEmpty(command))
                {

                    command = command.Trim('"');

                    if (command.Contains(" "))
                    {
                        command = command.Substring(0, command.IndexOf(" "));
                    }
                    var steamDir = Path.GetDirectoryName(command);
                    if (!string.IsNullOrEmpty(steamDir) && Directory.Exists(steamDir))
                    {
                        return steamDir;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting Steam path from registry: {ex.Message}");
            }

            var commonPaths = new[]
            {
                @"C:\Program Files (x86)\Steam",
                @"C:\Program Files\Steam",
                @"D:\Program Files (x86)\Steam",
                @"D:\Program Files\Steam"
            };

            foreach (var path in commonPaths)
            {
                if (Directory.Exists(path) && File.Exists(Path.Combine(path, "steam.exe")))
                {
                    return path;
                }
            }

            return string.Empty;
        }

        private static string GetLocalVdfPath()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(appDataPath, "Steam", "local.vdf");
        }

        private static string ComputeCrc32(string data)
        {
            var bytes = Encoding.UTF8.GetBytes(data);
            var crc32 = Crc32.Compute(bytes);
            return $"{crc32:X8}".TrimStart('0');
        }

        private static string SteamEncrypt(string token, string accountName)
        {

            var data = Encoding.UTF8.GetBytes(token);
            var entropy = Encoding.UTF8.GetBytes(accountName);

            var encrypted = ProtectedData.Protect(data, entropy, DataProtectionScope.CurrentUser);
            return BitConverter.ToString(encrypted).Replace("-", "").ToLower();
        }

        private static string BuildConfig(string mtbf, string steamId, string accountName)
        {

            var config = new Dictionary<string, object>
            {
                ["InstallConfigStore"] = new Dictionary<string, object>
                {
                    ["Software"] = new Dictionary<string, object>
                    {
                        ["Valve"] = new Dictionary<string, object>
                        {
                            ["Steam"] = new Dictionary<string, object>
                            {
                                ["AutoUpdateWindowEnabled"] = "0",
                                ["Accounts"] = new Dictionary<string, object>
                                {
                                    [accountName] = new Dictionary<string, object>
                                    {
                                        ["SteamID"] = steamId
                                    }
                                },
                                ["MTBF"] = mtbf
                            }
                        }
                    }
                }
            };

            return BuildVdf(config);
        }

        private static string BuildLoginUsers(string steamId, string accountName)
        {
            var loginUsers = new Dictionary<string, object>
            {
                ["users"] = new Dictionary<string, object>
                {
                    [steamId] = new Dictionary<string, object>
                    {
                        ["AccountName"] = accountName,
                        ["PersonaName"] = "nicealts",
                        ["RememberPassword"] = "1",
                        ["WantsOfflineMode"] = "0",
                        ["SkipOfflineModeWarning"] = "0",
                        ["AllowAutoLogin"] = "1",
                        ["MostRecent"] = "1",
                        ["Timestamp"] = ((long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds).ToString()
                    }
                }
            };

            return BuildVdf(loginUsers);
        }

        private static string BuildLocal(string crc32, string jwt)
        {
            var local = new Dictionary<string, object>
            {
                ["MachineUserConfigStore"] = new Dictionary<string, object>
                {
                    ["Software"] = new Dictionary<string, object>
                    {
                        ["Valve"] = new Dictionary<string, object>
                        {
                            ["Steam"] = new Dictionary<string, object>
                            {
                                ["ConnectCache"] = new Dictionary<string, object>
                                {
                                    [crc32] = jwt
                                }
                            }
                        }
                    }
                }
            };

            return BuildVdf(local);
        }

        private static string BuildVdf(Dictionary<string, object> data)
        {

            var sb = new StringBuilder();
            BuildVdfRecursive(sb, data, 0);
            return sb.ToString();
        }

        private static void BuildVdfRecursive(StringBuilder sb, Dictionary<string, object> data, int indent)
        {
            foreach (var kvp in data)
            {
                sb.Append('\t', indent);
                sb.Append('"');
                sb.Append(kvp.Key);
                sb.Append('"');

                if (kvp.Value is Dictionary<string, object> nested)
                {
                    sb.AppendLine();
                    sb.Append('\t', indent);
                    sb.AppendLine("{");
                    BuildVdfRecursive(sb, nested, indent + 1);
                    sb.Append('\t', indent);
                    sb.AppendLine("}");
                }
                else
                {
                    sb.Append(' ');
                    sb.Append('"');
                    sb.Append(kvp.Value);
                    sb.Append('"');
                    sb.AppendLine();
                }
            }
        }

        public static void KillSteam()
        {
            try
            {

                var processNames = new[] { "steam", "steamwebhelper", "steamerrorreporter", "steamservice" };
                bool killedAny = false;

                foreach (var processName in processNames)
                {
                    var processes = Process.GetProcessesByName(processName);
                    foreach (var process in processes)
                    {
                        try
                        {
                            process.Kill();
                            killedAny = true;
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error killing {processName}: {ex.Message}");
                        }
                    }
                }

                if (killedAny)
                {
                    Thread.Sleep(2000); // Wait for processes to fully terminate
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error killing Steam: {ex.Message}");
            }
        }

        public static void ResetSteam()
        {
            try
            {
                var path = GetSteamInstallPath();
                var directories = new[] { Path.Combine(path, "userdata"), Path.Combine(path, "config") };

                foreach (var directory in directories)
                {
                    if (Directory.Exists(directory))
                    {
                        Directory.Delete(directory, true);
                    }
                }

                var localVdfPath = GetLocalVdfPath();
                if (File.Exists(localVdfPath))
                {
                    File.Delete(localVdfPath);
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = Path.Combine(path, "steam.exe"),
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error resetting Steam: {ex.Message}");
            }
        }

        public static void StartSteam()
        {
            try
            {
                var path = GetSteamInstallPath();
                if (string.IsNullOrEmpty(path))
                {
                    throw new InvalidOperationException("Steam installation path not found. Please ensure Steam is installed.");
                }

                var steamExe = Path.Combine(path, "steam.exe");
                if (!File.Exists(steamExe))
                {
                    throw new FileNotFoundException($"Steam executable not found at: {steamExe}");
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = steamExe,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting Steam: {ex.Message}");
                throw; // Re-throw so caller can handle it
            }
        }

        public static string? GetCurrentAccount()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam");
                return key?.GetValue("AutoLoginUser")?.ToString();
            }
            catch
            {
                return null;
            }
        }

        public static void SaveCurrentAccounts()
        {
            try
            {
                var username = GetCurrentAccount();
                var vdfPath = GetLocalVdfPath();
                var path = GetSteamInstallPath();
                var savePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "cacheLoginToolData", "config");

                Directory.CreateDirectory(savePath);

                var configPath = Path.Combine(path, "config", "config.vdf");
                var loginUsersPath = Path.Combine(path, "config", "loginusers.vdf");

                if (File.Exists(configPath))
                    File.Copy(configPath, Path.Combine(savePath, "config.vdf"), true);
                if (File.Exists(loginUsersPath))
                    File.Copy(loginUsersPath, Path.Combine(savePath, "loginusers.vdf"), true);
                if (File.Exists(vdfPath))
                    File.Copy(vdfPath, Path.Combine(savePath, "local.vdf"), true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving accounts: {ex.Message}");
            }
        }
    }

    public static class Crc32
    {
        private static readonly uint[] Table = new uint[256];

        static Crc32()
        {
            for (uint i = 0; i < 256; i++)
            {
                uint crc = i;
                for (int j = 0; j < 8; j++)
                {
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
                }
                Table[i] = crc;
            }
        }

        public static uint Compute(byte[] bytes)
        {
            uint crc = 0xFFFFFFFF;
            foreach (var b in bytes)
            {
                crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            }
            return ~crc;
        }
    }
}

