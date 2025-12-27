using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using CacheLoginToolWPF.Models;
using System.Text.Json;
using System.Text;
using System.Windows.Controls;

namespace CacheLoginToolWPF
{
    public partial class MainWindow : Window
    {
        private DatabaseHelper? _dbHelper;
        private List<Account> _accounts = new List<Account>();
        private SplashScreen? _splashScreen;

        public MainWindow()
        {
            InitializeComponent();
        }

        public async Task InitializeAsync(SplashScreen splashScreen)
        {
            _splashScreen = splashScreen;

            try
            {

                _splashScreen?.UpdateStatus("Initializing...");
                var isFirstRun = AppDataHelper.InitializeAppData();

                if (isFirstRun)
                {
                    _splashScreen?.UpdateStatus("Setting up application data...");
                    await Task.Delay(500);
                }

                _splashScreen?.UpdateStatus("Loading database...");
                _dbHelper = new DatabaseHelper();
                await Task.Delay(200);

                _splashScreen?.UpdateStatus("Loading accounts...");
                LoadAccounts();
                await Task.Delay(200);

                _splashScreen?.UpdateStatus("Loading...");
                UpdateStatus();
                await Task.Delay(200);

                _splashScreen?.UpdateStatus("Ready!");
                await Task.Delay(300);

                AllowDrop = true;
                Drop += MainWindow_Drop;
                DragOver += MainWindow_DragOver;

                KeyDown += MainWindow_KeyDown;
            }
            catch (Exception ex)
            {
                _splashScreen?.UpdateStatus($"Error: {ex.Message}");
                await Task.Delay(1000);
                MessageBox.Show($"Error during initialization: {ex.Message}\n\nStack trace: {ex.StackTrace}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadAccounts()
        {
            if (_dbHelper == null) return;
            _accounts = _dbHelper.GetAllAccounts();
            AccountsDataGrid.ItemsSource = null; // Clear first
            AccountsDataGrid.ItemsSource = _accounts;
            AccountsDataGrid.Items.Refresh(); // Force refresh
        }

        private void UpdateStatus()
        {
            var currentAccount = ConfigHelper.GetCurrentAccount();
            StatusLabel.Text = currentAccount != null 
                ? $"Logged in as {currentAccount}" 
                : "Not Logged in.";
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {

            var currentAccount = ConfigHelper.GetCurrentAccount();
            if (string.IsNullOrEmpty(currentAccount))
            {
                MessageBox.Show("Please login to an account first.", "Not Logged In", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StartButton.IsEnabled = false;
            StartButton.Content = "Starting...";

            try
            {

                ConfigHelper.StartSteam();

                await Task.Run(async () =>
                {
                    int attempts = 0;
                    while (attempts < 30) // Wait up to 30 seconds
                    {
                        var steamProcesses = System.Diagnostics.Process.GetProcessesByName("steam");
                        if (steamProcesses.Length > 0)
                        {

                            await Task.Delay(1000); // Give it a moment to fully start
                            break;
                        }
                        await Task.Delay(500);
                        attempts++;
                    }
                });

                UpdateStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start Steam: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {

                StartButton.IsEnabled = true;
                StartButton.Content = "Start";
            }
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {

            LoginToAccount();
        }

        private void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Import Accounts",
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                LoadFromFile(dialog.FileName);
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            if (_accounts.Count == 0)
            {
                MessageBox.Show("No accounts to clear.", "No Accounts", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Are you sure you want to permanently delete all {_accounts.Count} account(s) from the database?\n\nThis action cannot be undone.",
                "Delete All Accounts",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    if (_dbHelper == null)
                    {
                        MessageBox.Show("Database not initialized.", "Error", 
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    var deletedCount = 0;
                    foreach (var account in _accounts.ToList()) // ToList() to avoid modification during iteration
                    {
                        if (_dbHelper.DeleteAccount(account.Username))
                        {
                            deletedCount++;
                        }
                    }

                    _accounts.Clear();
                    AccountsDataGrid.ItemsSource = null;
                    AccountsDataGrid.ItemsSource = _accounts;
                    AccountsDataGrid.Items.Refresh();

                    MessageBox.Show($"Successfully deleted {deletedCount} account(s) from the database.", 
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error deleting accounts: {ex.Message}", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void KillSteamButton_Click(object sender, RoutedEventArgs e)
        {
            ConfigHelper.KillSteam();
            UpdateStatus();
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_dbHelper == null)
            {
                MessageBox.Show("Database not initialized.", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var accountsToExport = _accounts.ToList();
            if (accountsToExport.Count == 0)
            {
                MessageBox.Show("No accounts to export.", "No Accounts", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                Title = "Export Accounts",
                FileName = "accounts.txt"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var exportedCount = 0;
                    using var writer = new StreamWriter(dialog.FileName, false, Encoding.UTF8);
                    foreach (var account in accountsToExport)
                    {
                        if (string.IsNullOrWhiteSpace(account.Username))
                            continue;

                        var token = account.Token;

                        if (string.IsNullOrWhiteSpace(token))
                        {
                            token = _dbHelper.GetToken(account.Username);
                        }

                        if (string.IsNullOrWhiteSpace(token))
                        {
                            System.Diagnostics.Debug.WriteLine($"Token not found for account: {account.Username}");
                            continue;
                        }

                        writer.WriteLine($"{account.Username}----{token}");
                        exportedCount++;
                    }

                    writer.Flush();

                    if (exportedCount > 0)
                    {
                        MessageBox.Show($"Exported {exportedCount} account(s) to {dialog.FileName}.", 
                            "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("No accounts were exported. Please check that accounts have valid tokens.", 
                            "Export Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error exporting accounts: {ex.Message}\n\n{ex.StackTrace}", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void SetApiKeyButton_Click(object sender, RoutedEventArgs e)
        {

            var inputDialog = new Window
            {
                Title = "Set Nice Alts API Key",
                Width = 400,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 0, 0))
            };

            var stackPanel = new System.Windows.Controls.StackPanel
            {
                Margin = new Thickness(20)
            };

            var label = new System.Windows.Controls.Label
            {
                Content = "Enter API Key:",
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255)),
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI")
            };

            var textBox = new System.Windows.Controls.TextBox
            {
                Height = 25,
                Margin = new Thickness(0, 5, 0, 15),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(26, 26, 26)),
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 255)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(26, 26, 26))
            };

            var buttonPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var okButton = new System.Windows.Controls.Button
            {
                Content = "OK",
                Width = 75,
                Height = 30,
                Margin = new Thickness(5, 0, 0, 0),
                Style = (Style)FindResource("PrimaryButtonStyle")
            };

            var cancelButton = new System.Windows.Controls.Button
            {
                Content = "Cancel",
                Width = 75,
                Height = 30,
                Margin = new Thickness(5, 0, 0, 0),
                Style = (Style)FindResource("SecondaryButtonStyle")
            };

            okButton.Click += (s, args) =>
            {
                var apiKey = textBox.Text.Trim();
                if (!string.IsNullOrEmpty(apiKey))
                {
                    try
                    {
                        NiceAltsApiHelper.SaveApiKey(apiKey);
                        MessageBox.Show("API key saved successfully.", "Success", 
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        inputDialog.DialogResult = true;
                        inputDialog.Close();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error saving API key: {ex.Message}", "Error", 
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    inputDialog.DialogResult = false;
                    inputDialog.Close();
                }
            };

            cancelButton.Click += (s, args) =>
            {
                inputDialog.DialogResult = false;
                inputDialog.Close();
            };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);

            stackPanel.Children.Add(label);
            stackPanel.Children.Add(textBox);
            stackPanel.Children.Add(buttonPanel);

            inputDialog.Content = stackPanel;
            inputDialog.ShowDialog();
        }

        private async void GenerateAccountButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                GenerateAccountButton.IsEnabled = false;
                GenerateAccountButton.Content = "Generating...";

                var (success, accountData, error) = await NiceAltsApiHelper.GenerateAccountAsync(
                    "cs2_prime", 1, "full");

                if (!success)
                {
                    MessageBox.Show($"Error: {error}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    GenerateAccountButton.IsEnabled = true;
                    GenerateAccountButton.Content = "Generate";
                    return;
                }

                if (accountData == null)
                {
                    MessageBox.Show("No account data received.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    GenerateAccountButton.IsEnabled = true;
                    GenerateAccountButton.Content = "Generate";
                    return;
                }

                var parsedAccount = NiceAltsApiHelper.ParseSteamAccountFromResponse(accountData);

                if (parsedAccount == null)
                {

                    var lines = accountData.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    parsedAccount = lines.FirstOrDefault(l => l.Contains("----"));
                }

                if (parsedAccount != null)
                {
                    var parts = parsedAccount.Split(new[] { "----" }, StringSplitOptions.None);
                    if (parts.Length >= 2)
                    {
                        var username = parts[0].Trim();
                        var token = parts[1].Trim();

                        if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(token))
                        {
                            if (_dbHelper == null)
                            {
                                MessageBox.Show("Database not initialized.", "Error",
                                    MessageBoxButton.OK, MessageBoxImage.Error);
                                return;
                            }

                            var steamId = JwtHelper.DecodeSteamId(token);
                            if (!string.IsNullOrEmpty(steamId))
                            {
                                if (_dbHelper.AddAccount(username, token, steamId))
                                {
                                    LoadAccounts();
                                    MessageBox.Show($"Account generated and added: {username}", "Success",
                                        MessageBoxButton.OK, MessageBoxImage.Information);
                                }
                                else
                                {
                                    MessageBox.Show("Account generated but failed to add to database.", "Warning",
                                        MessageBoxButton.OK, MessageBoxImage.Warning);
                                }
                            }
                            else
                            {
                                MessageBox.Show("Account generated but invalid token format.", "Warning",
                                    MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                        }
                    }
                }
                else
                {
                    MessageBox.Show($"Could not parse account from response. Raw data saved to clipboard.\n\nResponse: {accountData.Substring(0, Math.Min(200, accountData.Length))}...", 
                        "Parse Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    Clipboard.SetText(accountData);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error generating account: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                GenerateAccountButton.IsEnabled = true;
                GenerateAccountButton.Content = "Generate";
            }
        }

        private void LoginMenuItem_Click(object sender, RoutedEventArgs e)
        {
            StartWithAccount();
        }

        private void ViewBrowserMenuItem_Click(object sender, RoutedEventArgs e)
        {
            ViewInBrowser();
        }

        private void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            DeleteSelectedRows();
        }

        private void AccountsDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (AccountsDataGrid.SelectedItem is Account selectedAccount)
            {

                LoginToAccount(selectedAccount, startSteam: false);
            }
        }

        private bool LoginToAccount(Account? account = null, bool startSteam = false)
        {
            if (account == null)
            {
                var selectedItems = AccountsDataGrid.SelectedItems.Cast<Account>().ToList();
                if (selectedItems.Count == 0)
                {
                    MessageBox.Show("Please select an account to login with.", "No Selection", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                if (selectedItems.Count > 1)
                {
                    MessageBox.Show("Please select only one account.", "Multiple Selection", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                account = selectedItems[0];
            }

            if (account == null) return false;

            try
            {
                if (_dbHelper == null)
                {
                    MessageBox.Show("Database helper not initialized.", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                var token = _dbHelper.GetToken(account.Username);
                if (token == null)
                {
                    MessageBox.Show($"Token not found for account: {account.Username}", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                var result = ConfigHelper.DoLogin(account.Username, token, startSteam);
                if (result.Item1 == "Success")
                {
                    UpdateStatus();
                    return true; // Login successful
                }
                else
                {
                    MessageBox.Show($"Failed to login with {account.Username}.\n\nError: {result.Item2}", "Login Failed", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error logging into account {account.Username}: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void StartWithAccount(Account? account = null)
        {

            if (account == null)
            {
                var selectedItems = AccountsDataGrid.SelectedItems.Cast<Account>().ToList();
                if (selectedItems.Count == 0)
                {
                    MessageBox.Show("Please select an account to login with.", "No Selection", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (selectedItems.Count > 1)
                {
                    MessageBox.Show("Please select only one account.", "Multiple Selection", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                account = selectedItems[0];
            }

            if (account == null) return;

            if (LoginToAccount(account))
            {

                ConfigHelper.StartSteam();
                UpdateStatus();
            }
        }

        private void ViewInBrowser()
        {
            var selectedItems = AccountsDataGrid.SelectedItems.Cast<Account>().ToList();
            if (selectedItems.Count == 0)
            {
                MessageBox.Show("Please select an account to view.", "No Selection", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            foreach (var account in selectedItems)
            {
                if (!string.IsNullOrEmpty(account.SteamId))
                {
                    var url = $"https://steamcommunity.com/profiles/{account.SteamId}";
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
            }
        }

        private void DeleteSelectedRows()
        {
            var selectedItems = AccountsDataGrid.SelectedItems.Cast<Account>().ToList();
            if (selectedItems.Count == 0)
            {
                MessageBox.Show("Please select accounts to delete.", "No Selection", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"Are you sure you want to delete {selectedItems.Count} account(s)?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

            if (result == MessageBoxResult.Yes)
            {
                var deletedCount = 0;
                foreach (var account in selectedItems)
                {
                    if (_dbHelper == null) continue;
                    if (_dbHelper.DeleteAccount(account.Username))
                    {
                        deletedCount++;
                    }
                }
                LoadAccounts();
                if (deletedCount > 0)
                {
                    MessageBox.Show($"Deleted {deletedCount} account(s).", "Success", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (AccountsDataGrid.IsFocused)
                {
                    HandlePaste();
                    e.Handled = true;
                }
            }
        }

        private void HandlePaste()
        {
            try
            {
                var clipboardText = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(clipboardText))
                    return;

                var addedCount = 0;
                var lines = clipboardText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

                foreach (var line in lines)
                {
                    var parts = line.Split(new[] { "----" }, StringSplitOptions.None);
                    if (parts.Length >= 2)
                    {
                        var username = parts[0].Trim();
                        var token = parts[1].Trim();

                        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(token))
                            continue;

                        try
                        {
                            var steamId = JwtHelper.DecodeSteamId(token);
                            if (!string.IsNullOrEmpty(steamId))
                            {
                                if (_dbHelper != null && _dbHelper.AddAccount(username, token, steamId))
                                {
                                    addedCount++;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error processing account {username}: {ex.Message}");
                        }
                    }
                }

                if (addedCount > 0)
                {
                    LoadAccounts();
                    MessageBox.Show($"Added {addedCount} account(s) from clipboard.", "Success", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading accounts from clipboard: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MainWindow_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Text))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void MainWindow_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (var file in files)
                {
                    if (file.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    {
                        LoadFromFile(file);
                    }
                }
            }
            else if (e.Data.GetDataPresent(DataFormats.Text))
            {
                var text = e.Data.GetData(DataFormats.Text) as string;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    ImportAccountsFromText(text);
                }
            }
        }

        private void AccountsDataGrid_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetDataPresent(DataFormats.Text))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void AccountsDataGrid_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (var file in files)
                {
                    if (file.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                    {
                        LoadFromFile(file);
                    }
                }
                e.Handled = true;
            }
            else if (e.Data.GetDataPresent(DataFormats.Text))
            {
                var text = e.Data.GetData(DataFormats.Text) as string;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    ImportAccountsFromText(text);
                }
                e.Handled = true;
            }
        }

        private void ImportAccountsFromText(string text)
        {
            try
            {
                var addedCount = 0;
                var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

                foreach (var line in lines)
                {
                    var parts = line.Split(new[] { "----" }, StringSplitOptions.None);
                    if (parts.Length >= 2)
                    {
                        var username = parts[0].Trim();
                        var token = parts[1].Trim();

                        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(token))
                            continue;

                        try
                        {
                            if (_dbHelper == null) continue;
                            var steamId = JwtHelper.DecodeSteamId(token);
                            if (!string.IsNullOrEmpty(steamId))
                            {
                                if (_dbHelper.AddAccount(username, token, steamId))
                                {
                                    addedCount++;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error processing account {username}: {ex.Message}");
                        }
                    }
                }

                if (addedCount > 0)
                {
                    LoadAccounts();
                    MessageBox.Show($"Added {addedCount} account(s) from drag and drop.", "Success", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("No valid accounts found in the dropped text.", "Info", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error importing accounts: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadFromFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    MessageBox.Show($"File not found: {filePath}", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var addedCount = 0;
                var lines = File.ReadAllLines(filePath, Encoding.UTF8);

                foreach (var line in lines)
                {
                    var parts = line.Split(new[] { "----" }, StringSplitOptions.None);
                    if (parts.Length >= 2)
                    {
                        var username = parts[0].Trim();
                        var token = parts[1].Trim();

                        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(token))
                            continue;

                        try
                        {
                            var steamId = JwtHelper.DecodeSteamId(token);
                            if (!string.IsNullOrEmpty(steamId))
                            {
                                if (_dbHelper != null && _dbHelper.AddAccount(username, token, steamId))
                                {
                                    addedCount++;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error processing account {username}: {ex.Message}");
                        }
                    }
                }

                if (addedCount > 0)
                {
                    LoadAccounts();
                    MessageBox.Show($"Successfully imported {addedCount} account(s) from file.", "Import Success", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("No valid accounts found in the file.\n\nMake sure the file contains accounts in the format:\nusername----token\n\nEach account should be on a separate line.", "No Accounts Found", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading file {filePath}: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _dbHelper?.Close();
            base.OnClosed(e);
        }
    }
}

