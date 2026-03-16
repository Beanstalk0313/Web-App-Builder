using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Composition.SystemBackdrops;
using WebAppBuilder.Views;

namespace WebAppBuilder
{
    public partial class App : Application
    {
        public static Window? MainWindow { get; private set; }

        public App()
        {
            this.InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            MainWindow = new Window();
            MainWindow.Title = "Web App Builder";

            // Enable Acrylic/Mica Backdrop with correct support checks
            if (DesktopAcrylicController.IsSupported())
            {
                MainWindow.SystemBackdrop = new DesktopAcrylicBackdrop();
            }
            else if (MicaController.IsSupported())
            {
                MainWindow.SystemBackdrop = new MicaBackdrop();
            }

            if (MainWindow.Content is not Frame rootFrame)
            {
                rootFrame = new Frame();
                MainWindow.Content = rootFrame;
            }

            _ = rootFrame.Navigate(typeof(MainPage));
            MainWindow.Activate();
        }
    }
}
