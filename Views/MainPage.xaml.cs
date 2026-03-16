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

        private void UpdateProgress(double value, string status)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                BuildProgressBar.Value = value;
                BuildStatusText.Text = $"Status: {status}";
            });
        }

        private async void BrowseIcon_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".ico");
            PreparePickerWithWindow(picker);
            var file = await picker.PickSingleFileAsync();
            if (file != null) { _selectedIconPath = file.Path; IconPathBox.Text = file.Name; }
        }

        private void PreparePickerWithWindow(object obj)
        {
            if (App.MainWindow != null) {
                IntPtr windowHandle = WindowNative.GetWindowHandle(App.MainWindow);
                WinRT.Interop.InitializeWithWindow.Initialize(obj, windowHandle);
            }
        }

        private async void PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            string appName = AppNameBox.Text.Trim();
            string appUrl = AppUrlBox.Text.Trim();
            if (string.IsNullOrEmpty(appName) || string.IsNullOrEmpty(appUrl)) { ShowError("Need Name and URL to preview."); return; }

            SetLoading(true);
            try {
                Log("Preparing instant preview...");
                string targetDir = await PrepareTempBuildAsync(appName, appUrl);
                Log("Launching app preview (Electron)...");
                await RunCommandAsync("npm start", targetDir, true);
            } catch (Exception ex) { ShowError(ex.Message); }
            finally { SetLoading(false); }
        }

        private async void BuildButton_Click(object sender, RoutedEventArgs e)
        {
            string appName = AppNameBox.Text.Trim();
            string appUrl = AppUrlBox.Text.Trim();
            string format = (FormatComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "win-nsis";

            if (string.IsNullOrEmpty(appName) || string.IsNullOrEmpty(appUrl)) { ShowError("Missing information."); return; }

            LogTextBox.Text = "";
            SetLoading(true);
            try {
                UpdateProgress(10, "Preparing workspace...");
                string targetDir = await PrepareTempBuildAsync(appName, appUrl);
                
                UpdateProgress(30, "Installing dependencies...");
                await RunCommandAsync("npm install", targetDir);

                UpdateProgress(60, "Packaging application...");
                string buildCmd = format switch {
                    "win-nsis" => "npx electron-builder --win nsis",
                    "win-portable" => "npx electron-builder --win portable",
                    "win-both" => "npx electron-builder --win nsis portable",
                    _ => "npx electron-builder --win nsis"
                };
                await RunCommandAsync(buildCmd, targetDir);

                UpdateProgress(90, "Moving to Completed Apps...");
                await FinalizeBuildAsync(targetDir, appName);
                
                UpdateProgress(100, "Ready");
                ShowSuccess("Build completed successfully!");
                ShowNotification(appName, "Your app is ready!");
            } catch (Exception ex) { Log($"ERROR: {ex.Message}"); ShowError(ex.Message); UpdateProgress(0, "Build Failed"); }
            finally { SetLoading(false); }
        }

        private async Task<string> PrepareTempBuildAsync(string name, string url)
        {
            string currentDir = Environment.CurrentDirectory;
            string templatePath = GetTemplatePath();
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
                try {
                    string domain = new Uri(url).Host;
                    using var client = new HttpClient();
                    var iconData = await client.GetByteArrayAsync($"https://logo.clearbit.com/{domain}?size=256");
                    if (iconData.Length > 5000) await File.WriteAllBytesAsync(Path.Combine(targetDir, "icon.png"), iconData);
                } catch { }
            }

            // Colors
            var bg = SplashColorPicker.Color;
            var tx = TextColorPicker.Color;
            string splashBg = $"#{bg.R:X2}{bg.G:X2}{bg.B:X2}";
            string splashTx = $"#{tx.R:X2}{tx.G:X2}{tx.B:X2}";
            string template = (SplashTemplateCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "modern";

            var config = new { appName = name, url = url, splashColor = splashBg, splashTextColor = splashTx, splashTemplate = template };
            await File.WriteAllTextAsync(Path.Combine(targetDir, "config.json"), JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));

            // Advanced Features (CSS Injection)
            string userCss = "";
            if (BlockAdsCheck.IsChecked == true) userCss += "\n[class*='ad-'], [id*='ad-'] { display: none !important; }";
            if (ForceDarkCheck.IsChecked == true) userCss += "\nhtml, body { filter: invert(0.9) hue-rotate(180deg) !important; background: #fff !important; }";
            if (!string.IsNullOrWhiteSpace(CustomCssBox.Text)) userCss += "\n" + CustomCssBox.Text;
            if (!string.IsNullOrWhiteSpace(userCss)) await File.WriteAllTextAsync(Path.Combine(targetDir, "user.css"), userCss);

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

            return targetDir;
        }

        private async Task FinalizeBuildAsync(string targetDir, string name)
        {
            await Task.Run(() => {
                string currentDir = Environment.CurrentDirectory;
                string? parentDir = Path.GetDirectoryName(targetDir);
                if (parentDir == null) return;

                string outDir = Path.Combine(parentDir, "Builds");
                string finalDir = Path.Combine(currentDir, "Completed Apps");
                if (!Directory.Exists(finalDir)) Directory.CreateDirectory(finalDir);

                if (Directory.Exists(outDir)) {
                    foreach (var file in Directory.GetFiles(outDir)) {
                        string fileName = Path.GetFileName(file);
                        if (fileName.EndsWith(".exe") || fileName.EndsWith(".msi")) {
                            File.Copy(file, Path.Combine(finalDir, fileName), true);
                        }
                    }
                }
                try { Directory.Delete(parentDir, true); } catch { }
            });
        }

        private string GetTemplatePath()
        {
            string path = Path.Combine(Environment.CurrentDirectory, "Template");
            if (!Directory.Exists(path)) {
                path = Path.Combine(AppContext.BaseDirectory, "Template");
                if (!Directory.Exists(path)) {
                    var dir = new DirectoryInfo(AppContext.BaseDirectory);
                    while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Template"))) dir = dir.Parent;
                    if (dir != null) path = Path.Combine(dir.FullName, "Template");
                }
            }
            return path;
        }

        private async Task RunCommandAsync(string command, string workingDir, bool isBackground = false)
        {
            var process = new Process {
                StartInfo = new ProcessStartInfo {
                    FileName = "cmd.exe", Arguments = $"/c {command}", WorkingDirectory = workingDir,
                    RedirectStandardOutput = !isBackground, RedirectStandardError = !isBackground, UseShellExecute = false, CreateNoWindow = true
                }
            };
            if (!isBackground) {
                process.OutputDataReceived += (s, e) => { if (e.Data != null) Log($"[OUT] {e.Data}"); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) Log($"[ERR] {e.Data}"); };
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync();
                if (process.ExitCode != 0) throw new Exception($"Command failed: {command}");
            } else {
                process.Start();
            }
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);
            foreach (var file in Directory.GetFiles(sourceDir)) File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), true);
            foreach (var subDir in Directory.GetDirectories(sourceDir)) {
                if (Path.GetFileName(subDir).Equals("node_modules", StringComparison.OrdinalIgnoreCase)) continue;
                CopyDirectory(subDir, Path.Combine(destinationDir, Path.GetFileName(subDir)));
            }
        }

        private void SetLoading(bool isLoading) { BuildButton.IsEnabled = !isLoading; PreviewButton.IsEnabled = !isLoading; }
        private void ShowSuccess(string msg) { StatusInfoBar.Message = msg; StatusInfoBar.Severity = InfoBarSeverity.Success; StatusInfoBar.IsOpen = true; }
        private void ShowError(string msg) { StatusInfoBar.Message = msg; StatusInfoBar.Severity = InfoBarSeverity.Error; StatusInfoBar.IsOpen = true; }
        private void ShowNotification(string title, string body) {
            try {
                var xml = $"<toast><visual><binding template='ToastGeneric'><text>{title}</text><text>{body}</text></binding></visual></toast>";
                var doc = new XmlDocument(); doc.LoadXml(xml);
                ToastNotificationManager.CreateToastNotifier().Show(new ToastNotification(doc));
            } catch { }
        }
    }
}
