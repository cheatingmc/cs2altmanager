using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CacheLoginToolWPF
{
    public partial class SplashScreen : Window
    {
        public SplashScreen()
        {
            InitializeComponent();
        }

        public void UpdateStatus(string status)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = status;
            });
        }

        public void SetProgress(double value)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Value = value;
            });
        }

        public async Task FadeOut()
        {
            var fadeOut = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = TimeSpan.FromSeconds(0.3)
            };

            BeginAnimation(UIElement.OpacityProperty, fadeOut);
            await Task.Delay(300);
        }

        private void Image_DecodeFailed(object sender, ExceptionEventArgs e)
        {

            System.Diagnostics.Debug.WriteLine($"Failed to load logo image: {e.ErrorException?.Message}");
        }
    }
}

