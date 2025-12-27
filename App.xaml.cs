using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace CacheLoginToolWPF
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {

                LoadPoppinsFont();

                var splashScreen = new SplashScreen();
                splashScreen.Show();

                var mainWindow = new MainWindow();
                await mainWindow.InitializeAsync(splashScreen);

                await splashScreen.FadeOut();
                splashScreen.Close();

                mainWindow.Show();
                mainWindow.Activate();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start application: {ex.Message}\n\n{ex.StackTrace}", 
                    "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        private void LoadPoppinsFont()
        {
            try
            {

                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var currentDir = Environment.CurrentDirectory;
                var appContextBaseDir = AppContext.BaseDirectory;

                var paths = new List<string>();

                if (!string.IsNullOrEmpty(baseDir))
                    paths.Add(Path.Combine(baseDir, "Fonts", "Poppins-Regular.ttf"));

                if (!string.IsNullOrEmpty(currentDir))
                    paths.Add(Path.Combine(currentDir, "Fonts", "Poppins-Regular.ttf"));

                if (!string.IsNullOrEmpty(appContextBaseDir))
                    paths.Add(Path.Combine(appContextBaseDir, "Fonts", "Poppins-Regular.ttf"));

                foreach (var fontPath in paths)
                {
                    if (!string.IsNullOrEmpty(fontPath) && File.Exists(fontPath))
                    {
                        var fontUri = new Uri(fontPath);
                        var fontFamily = new FontFamily(fontUri, "Poppins");

                        Resources["PoppinsFont"] = fontFamily;
                        System.Diagnostics.Debug.WriteLine($"Poppins font loaded from: {fontPath}");
                        return;
                    }
                }

                System.Diagnostics.Debug.WriteLine("Poppins font not found, using fallback font");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Could not load Poppins font: {ex.Message}");
            }
        }
    }
}
