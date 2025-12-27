using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.Sqlite;
using System.IO;
using System.Linq;
using CacheLoginToolWPF.Models;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CacheLoginToolWPF
{
    public class DatabaseHelper
    {
        private string _dbPath;
        private SqliteConnection? _connection;

        public DatabaseHelper()
        {

            var dbDir = AppDataHelper.GetDatabasePath();

            try
            {
                Directory.CreateDirectory(dbDir);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to create database directory: {dbDir}, Error: {ex.Message}", ex);
            }

            try
            {
                _dbPath = Path.Combine(dbDir, "current.db");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to combine database path. dbDir: '{dbDir}', Error: {ex.Message}", ex);
            }

            if (string.IsNullOrWhiteSpace(_dbPath))
            {
                throw new InvalidOperationException($"Database path is null or empty after combination. dbDir: '{dbDir}'");
            }

            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            if (string.IsNullOrEmpty(_dbPath))
            {
                throw new InvalidOperationException("Database path is null or empty");
            }

            string absolutePath;
            try
            {

                if (Path.IsPathRooted(_dbPath))
                {
                    absolutePath = _dbPath;
                }
                else
                {

                    absolutePath = Path.GetFullPath(_dbPath);
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to get absolute path for database. Path: '{_dbPath}', Error: {ex.Message}", ex);
            }

            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                throw new InvalidOperationException($"Absolute path is null or empty. Original path: '{_dbPath}'");
            }

            absolutePath = absolutePath.Replace('/', '\\');

            if (absolutePath.Length > 3 && absolutePath.EndsWith("\\"))
            {
                absolutePath = absolutePath.TrimEnd('\\');
            }

            if (!absolutePath.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Database path must end with .db. Path: '{absolutePath}'");
            }

            var dbDirectory = Path.GetDirectoryName(absolutePath);
            if (string.IsNullOrWhiteSpace(dbDirectory))
            {
                throw new InvalidOperationException($"Could not determine directory for database path: '{absolutePath}'");
            }

            try
            {
                Directory.CreateDirectory(dbDirectory);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to create database directory: '{dbDirectory}', Error: {ex.Message}", ex);
            }

            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                throw new InvalidOperationException("Absolute path is null or empty after validation");
            }

            var invalidChars = Path.GetInvalidPathChars();
            if (absolutePath.IndexOfAny(invalidChars) >= 0)
            {
                throw new InvalidOperationException($"Path contains invalid characters: '{absolutePath}'");
            }

            string connectionString;

            try
            {

                var builder = new SqliteConnectionStringBuilder();

                builder.DataSource = absolutePath;

                builder.Mode = SqliteOpenMode.ReadWriteCreate; // Create if doesn't exist
                builder.ForeignKeys = true;

                connectionString = builder.ConnectionString;

                if (string.IsNullOrWhiteSpace(builder.DataSource))
                {
                    throw new InvalidOperationException($"SqliteConnectionStringBuilder.DataSource is null after setting. Path was: '{absolutePath}'");
                }

                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    throw new InvalidOperationException("Connection string is null or empty after building");
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to build connection string. Path: '{absolutePath}', Error: {ex.Message}", ex);
            }

            try
            {

                var pathInfo = new FileInfo(absolutePath);
                var pathDir = pathInfo.Directory;

                if (pathDir == null)
                {
                    throw new InvalidOperationException($"Could not get directory info for path: '{absolutePath}'");
                }

                if (!pathDir.Exists)
                {
                    pathDir.Create();
                }

                _connection = new SqliteConnection(connectionString);

                _connection.Open();

                if (_connection == null || _connection.State != System.Data.ConnectionState.Open)
                {
                    _connection?.Dispose();
                    _connection = null;
                    throw new InvalidOperationException($"Database connection failed to open. Connection is null: {_connection == null}");
                }
            }
            catch (ArgumentNullException ex)
            {
                _connection?.Dispose();
                _connection = null;
                throw new InvalidOperationException($"SQLite connection failed: null argument. Path: '{absolutePath}', Directory: '{dbDirectory}', Connection String: '{connectionString}', Error: {ex.Message}", ex);
            }
            catch (ArgumentException ex)
            {
                _connection?.Dispose();
                _connection = null;
                throw new InvalidOperationException($"SQLite connection failed: invalid argument. Path: '{absolutePath}', Directory: '{dbDirectory}', Error: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                _connection?.Dispose();
                _connection = null;
                throw new InvalidOperationException($"Failed to open database connection. Path: '{absolutePath}', Directory: '{dbDirectory}', Connection String: '{connectionString}', Error: {ex.Message}", ex);
            }

            using var command = _connection!.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS accounts (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    username TEXT NOT NULL UNIQUE,
                    steamid TEXT NOT NULL,
                    token TEXT NOT NULL,
                    is_prime INTEGER NOT NULL DEFAULT 0,
                    is_used INTEGER NOT NULL DEFAULT 0,
                    last_login INTEGER NOT NULL DEFAULT 0,
                    in_config INTEGER NOT NULL DEFAULT 0
                )";
            command.ExecuteNonQuery();

            try
            {
                command.CommandText = "ALTER TABLE accounts ADD COLUMN is_prime INTEGER NOT NULL DEFAULT 0";
                command.ExecuteNonQuery();
            }
            catch
            {

            }
        }

        public List<Account> GetAllAccounts()
        {
            var accounts = new List<Account>();
            if (_connection == null) return accounts;

            string query = "SELECT username, steamid, token, is_prime FROM accounts";
            try
            {
                using var command = _connection.CreateCommand();
                command.CommandText = query;
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var account = new Account
                    {
                        Username = reader.GetString(0),
                        SteamId = reader.GetString(1),
                        Token = reader.GetString(2)
                    };

                    if (reader.FieldCount > 3)
                    {
                        account.IsPrime = reader.GetInt32(3) != 0;
                    }
                    else
                    {

                        account.IsPrime = CheckPrimeStatus(account.SteamId);
                    }

                    account.ProfilePictureUrl = GetSteamAvatarUrl(account.SteamId);

                    accounts.Add(account);
                }
            }
            catch
            {

                using var command = _connection.CreateCommand();
                command.CommandText = "SELECT username, steamid, token FROM accounts";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var account = new Account
                    {
                        Username = reader.GetString(0),
                        SteamId = reader.GetString(1),
                        Token = reader.GetString(2),
                        IsPrime = CheckPrimeStatus(reader.GetString(1))
                    };
                    account.ProfilePictureUrl = GetSteamAvatarUrl(account.SteamId);
                    accounts.Add(account);
                }
            }

            return accounts;
        }

        private bool CheckPrimeStatus(string steamId)
        {
            try
            {

                using var httpClient = new System.Net.Http.HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(10);
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                var url = $"https://steamcommunity.com/profiles/{steamId}/games/?tab=all&xml=1";

                try
                {
                    var response = httpClient.GetStringAsync(url).GetAwaiter().GetResult();

                    if (response.Contains("appID>730<") || 
                        response.Contains("\"730\"") || 
                        response.Contains("appid=\"730\"") ||
                        response.Contains("appid=730") ||
                        response.Contains("730") && (response.Contains("Counter-Strike") || response.Contains("CS2")))
                    {
                        return true;
                    }
                }
                catch
                {

                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private string GetSteamAvatarUrl(string steamId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(steamId))
                    return string.Empty;

                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(5);
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                var profileUrl = $"https://steamcommunity.com/profiles/{steamId}";
                var response = httpClient.GetStringAsync(profileUrl).GetAwaiter().GetResult();

                var avatarMatch = Regex.Match(
                    response, 
                    @"https://avatars\.steamstatic\.com/[^""\s]+\.jpg",
                    RegexOptions.IgnoreCase);

                if (avatarMatch.Success)
                {
                    return avatarMatch.Value;
                }

                avatarMatch = Regex.Match(
                    response,
                    @"profile_header_avatar[^>]+src=""([^""]+)""",
                    RegexOptions.IgnoreCase);

                if (avatarMatch.Success && avatarMatch.Groups.Count > 1)
                {
                    var avatarUrl = avatarMatch.Groups[1].Value;
                    if (avatarUrl.StartsWith("http"))
                    {
                        return avatarUrl;
                    }
                }

                avatarMatch = Regex.Match(
                    response,
                    @"playerAvatar[^>]+src=""([^""]+)""",
                    RegexOptions.IgnoreCase);

                if (avatarMatch.Success && avatarMatch.Groups.Count > 1)
                {
                    var avatarUrl = avatarMatch.Groups[1].Value;
                    if (avatarUrl.StartsWith("http"))
                    {
                        return avatarUrl;
                    }
                }

                return string.Empty;
            }
            catch
            {

                return string.Empty;
            }
        }

        public bool AddAccount(string username, string token, string steamId)
        {
            try
            {
                if (_connection == null) return false;
                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(token))
                    return false;

                if (username.Length > 255 || token.Length > 2000)
                    return false;

                var isPrime = CheckPrimeStatus(steamId);

                try
                {
                    using var command = _connection.CreateCommand();
                    command.CommandText = "INSERT OR IGNORE INTO accounts (username, token, steamid, is_prime) VALUES (@username, @token, @steamid, @is_prime)";
                    command.Parameters.AddWithValue("@username", username);
                    command.Parameters.AddWithValue("@token", token);
                    command.Parameters.AddWithValue("@steamid", steamId);
                    command.Parameters.AddWithValue("@is_prime", isPrime ? 1 : 0);
                    command.ExecuteNonQuery();
                    return true;
                }
                catch
                {

                    using var command = _connection.CreateCommand();
                    command.CommandText = "INSERT OR IGNORE INTO accounts (username, token, steamid) VALUES (@username, @token, @steamid)";
                    command.Parameters.AddWithValue("@username", username);
                    command.Parameters.AddWithValue("@token", token);
                    command.Parameters.AddWithValue("@steamid", steamId);
                    command.ExecuteNonQuery();
                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adding account: {ex.Message}");
                return false;
            }
        }

        public string? GetToken(string username)
        {
            if (_connection == null) return null;
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT token FROM accounts WHERE username = @username";
            command.Parameters.AddWithValue("@username", username);
            var result = command.ExecuteScalar();
            return result?.ToString();
        }

        public bool DeleteAccount(string username)
        {
            try
            {
                if (_connection == null) return false;
                using var command = _connection.CreateCommand();
                command.CommandText = "DELETE FROM accounts WHERE username = @username";
                command.Parameters.AddWithValue("@username", username);
                return command.ExecuteNonQuery() > 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error deleting account: {ex.Message}");
                return false;
            }
        }

        public void Close()
        {
            _connection?.Close();
            _connection?.Dispose();
        }
    }
}

