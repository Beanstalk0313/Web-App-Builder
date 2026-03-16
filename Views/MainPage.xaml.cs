using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using WinRT.Interop;
using Windows.UI.Notifications;
using Windows.Data.Xml.Dom;

namespace WebAppBuilder.Views
{
    public sealed partial class MainPage : Page
    {
        private string? _selectedIconPath;

        public MainPage()
        {
            this.InitializeComponent();
        }

        private void Log(string message)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                LogTextBox.Text += $"{DateTime.Now:HH:mm:ss} - {message}\r\n";
                LogScrollViewer.ChangeView(null, LogScrollViewer.ExtentHeight, null);
                Console.WriteLine(message);
            });
        }

        private async void BrowseIcon_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".ico");
            PreparePickerWithWindow(picker);
            var file = await picker.PickSingleFileAsync();
            if (file != null) {
                _selectedIconPath = file.Path;
                IconPathBox.Text = file.Name;
            }
        }

        private void PreparePickerWithWindow(object obj)
        {
            if (App.MainWindow != null) {
                IntPtr windowHandle = WindowNative.GetWindowHandle(App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(obj, windowHandle);
            }
        }

        private async void BuildButton_Click(object sender, RoutedEventArgs e)
        {
            string appName = AppNameBox.Text.Trim();
            string appUrl = AppUrlBox.Text.Trim();
            string format = (FormatComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "win-nsis";

            if (string.IsNullOrEmpty(appName) || string.IsNullOrEmpty(appUrl)) {
                ShowError("Please provide both App Name and Web URL.");
                return;
            }

            LogTextBox.Text = "";
            SetLoading(true);
            try {
                await BuildWebAppAsync(appName, appUrl, format);
                ShowSuccess("Build completed! Files moved to 'Completed Apps'.");
                ShowNotification(appName, "Your app is ready!");
            } catch (Exception ex) {
                Log($"ERROR: {ex.Message}");
                ShowError(ex.Message);
            } finally {
                SetLoading(false);
            }
        }

        private void ShowNotification(string title, string body)
        {
            try {
                string xml = $@"<toast><visual><binding template='ToastGeneric'><text>{title}</text><text>{body}</text></binding></visual></toast>";
                var doc = new XmlDocument();
                doc.LoadXml(xml);
                var toast = new ToastNotification(doc);
                ToastNotificationManager.CreateToastNotifier().Show(toast);
            } catch {}
        }

        private void SetLoading(bool isLoading)
        {
            BuildButton.IsEnabled = !isLoading;
            BuildProgressBar.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        }

        private async Task BuildWebAppAsync(string name, string url, string format)
        {
            string currentDir = Environment.CurrentDirectory;
            string templatePath = Path.Combine(currentDir, "Template");
            
            if (!Directory.Exists(templatePath)) {
                string baseDir = AppContext.BaseDirectory;
                var dir = new DirectoryInfo(baseDir);
                while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Template"))) dir = dir.Parent;
                if (dir != null) templatePath = Path.Combine(dir.FullName, "Template");
            }

            string safeName = name.Replace(" ", "_");
            foreach (char c in Path.GetInvalidFileNameChars()) safeName = safeName.Replace(c, '_');

            string tempBuildDir = Path.Combine(currentDir, "TempBuild");
            string targetDir = Path.Combine(tempBuildDir, safeName);
            if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
            
            Log("Copying template...");
            CopyDirectory(templatePath, targetDir);

            // Icon
            if (!string.IsNullOrEmpty(_selectedIconPath)) {
                File.Copy(_selectedIconPath, Path.Combine(targetDir, "icon.png"), true);
            } else {
                Log("Attempting to fetch high-res favicon...");
                try {
                    string domain = new Uri(url).Host;
                    using var client = new HttpClient();
                    var iconData = await client.GetByteArrayAsync($"https://logo.clearbit.com/{domain}?size=256");
                    if (iconData.Length > 5000) await File.WriteAllBytesAsync(Path.Combine(targetDir, "icon.png"), iconData);
                } catch { Log("Using default icon."); }
            }

            // CSS Injection
            string userCss = CustomCssBox.Text;
            if (BlockAdsCheck.IsChecked == true) userCss += "\n[class*='ad-'], [id*='ad-'] { display: none !important; }";
            if (ForceDarkCheck.IsChecked == true) userCss += "\nhtml, body { filter: invert(0.9) hue-rotate(180deg) !important; background: #fff !important; }";
            if (!string.IsNullOrWhiteSpace(userCss)) await File.WriteAllTextAsync(Path.Combine(targetDir, "user.css"), userCss);

            // Config.json
            string splashTemplate = (SplashTemplateCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "modern";
            var config = new { appName = name, url = url, splashColor = SplashColorBox.Text, splashTemplate = splashTemplate, splashTextColor = TextColorBox.Text };
            await File.WriteAllTextAsync(Path.Combine(targetDir, "config.json"), JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));

            // Package.json
            string pkgPath = Path.Combine(targetDir, "package.json");
            var pkgObj = JsonSerializer.Deserialize<Dictionary<string, object>>(await File.ReadAllTextAsync(pkgPath));
            if (pkgObj != null) {
                pkgObj["name"] = safeName.ToLower();
                var build = JsonSerializer.Deserialize<Dictionary<string, object>>(pkgObj["build"].ToString()!);
                build!["productName"] = name;
                pkgObj["build"] = build;
                await File.WriteAllTextAsync(pkgPath, JsonSerializer.Serialize(pkgObj, new JsonSerializerOptions { WriteIndented = true }));
            }

            Log("Installing dependencies...");
            await RunCommandAsync("npm install", targetDir);

            Log($"Packaging for {format}...");
            string buildCmd = "";
            if (format == "win-nsis") buildCmd = "npx electron-builder --win nsis";
            else if (format == "win-portable") buildCmd = "npx electron-builder --win portable";
            else if (format == "win-both") buildCmd = "npx electron-builder --win nsis portable";

            await RunCommandAsync(buildCmd, targetDir);

            Log("Finalizing...");
            string outDir = Path.Combine(tempBuildDir, "Builds");
            string finalDir = Path.Combine(currentDir, "Completed Apps");
            if (!Directory.Exists(finalDir)) Directory.CreateDirectory(finalDir);

            if (Directory.Exists(outDir)) {
                foreach (var file in Directory.GetFiles(outDir)) {
                    string fileName = Path.GetFileName(file);
                    if (fileName.EndsWith(".exe") || fileName.EndsWith(".msi")) {
                        File.Copy(file, Path.Combine(finalDir, fileName), true);
                        Log($"Moved to Completed Apps: {fileName}");
                    }
                }
            }

            Log("Wiping TempBuild...");
            try { Directory.Delete(tempBuildDir, true); } catch { Log("Warning: Could not delete TempBuild folder completely."); }
        }

        private async Task RunCommandAsync(string command, string workingDir)
        {
            var process = new Process {
                StartInfo = new ProcessStartInfo {
                    FileName = "cmd.exe", Arguments = $"/c {command}", WorkingDirectory = workingDir,
                    RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
                }
            };
            process.OutputDataReceived += (s, e) => { if (e.Data != null) Log($"[OUT] {e.Data}"); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) Log($"[ERR] {e.Data}"); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) throw new Exception($"Command failed: {command}");
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);
            foreach (var file in Directory.GetFiles(sourceDir)) File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)));
            foreach (var subDir in Directory.GetDirectories(sourceDir)) {
                if (Path.GetFileName(subDir).Equals("node_modules", StringComparison.OrdinalIgnoreCase)) continue;
                CopyDirectory(subDir, Path.Combine(destinationDir, Path.GetFileName(subDir)));
            }
        }

        private void ShowSuccess(string msg) { StatusInfoBar.Message = msg; StatusInfoBar.Severity = InfoBarSeverity.Success; StatusInfoBar.IsOpen = true; }
        private void ShowError(string msg) { StatusInfoBar.Message = msg; StatusInfoBar.Severity = InfoBarSeverity.Error; StatusInfoBar.IsOpen = true; }
    }
}
