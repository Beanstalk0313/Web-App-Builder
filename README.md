# Web-App-Builder
A C# application that makes Electron web apps
=======
# Web App Builder 🚀

A powerful, standalone Windows application built with **C# and WinUI 3** that allows you to transform any website into a professional, feature-rich desktop application using **Electron**.

![WinUI 3](https://img.shields.io/badge/UI-WinUI%203-blue)
![.NET 8](https://img.shields.io/badge/.NET-8.0-purple)
![Electron](https://img.shields.io/badge/Engine-Electron-47848F)

## ✨ Features

### 🛠 The Builder (WinUI 3)
*   **Modern Native UI:** Designed with a beautiful Acrylic/Mica translucent background that fits perfectly into Windows 11.
*   **One-Click Packaging:** Automatically handles `npm install` and `electron-builder` to produce final installers or portable executables.
*   **Smart Icon Fetching:** Automatically retrieves high-resolution logos from websites or allows for custom `.ico`/`.png` uploads.
*   **Splash Screen Customization:** Choose from 4 animated templates:
    *   **Modern Minimal:** Clean fade and slide.
    *   **Glassmorphism:** Elegant blurred background.
    *   **Gradient Wave:** Dynamic, colorful backgrounds.
    *   **Elegant Reveal:** Sophisticated reveal animations.
*   **Advanced Injection:** Optional experimental ad-blocking and forced dark mode via CSS injection.
*   **System Notifications:** Get notified immediately when your build is finished and moved to the `Completed Apps` folder.

### 🌐 The Generated Apps (Electron)
*   **Native Feel:** No browser tabs or address bars—just your web app in a dedicated window.
*   **Built-in Settings Menu:** Every built app includes its own settings panel (`Ctrl + ,`) independent of the website.
*   **Window Intelligence:** Automatically remembers its last size and position on the screen.
*   **Tray Integration:** Minimize or close the app to the System Tray to keep it running in the background.
*   **Theme Control:** Force Light/Dark mode or follow the system theme, allowing websites with native dark mode support to react accordingly.
*   **Direct Downloads:** Uses the native Windows file picker immediately for all downloads.
*   **Domain Lockdown:** Navigation is restricted to the app's domain; external links open safely in your default web browser.

## 🚀 Getting Started

### Prerequisites
To build applications, the machine running the **Web App Builder** must have:
*   [Node.js & NPM](https://nodejs.org/) installed.

### Installation
1.  Download the latest release from the [Releases](https://github.com/YOUR_USERNAME/web-app-builder/releases) page.
2.  Extract the ZIP file.
3.  Run `WebAppBuilder.exe`.

### Creating your first App
1.  Enter the **App Name** (e.g., "GitHub").
2.  Enter the **Website URL** (e.g., `https://github.com`).
3.  (Optional) Upload a custom icon or customize the splash screen colors.
4.  Select your **Output Format** (Installer, Portable, or Both).
5.  Click **Create & Package App**.
6.  Once the notification appears, find your ready-to-use app in the `Completed Apps` folder!

## 🛠 Technical Details
*   **Frontend:** WinUI 3 (Windows App SDK)
*   **Backend Logic:** C# / .NET 8
*   **Web Engine:** Electron with `WebContentsView`
*   **Build Tooling:** `electron-builder` & `npm`

## 🤝 Contributing
Contributions are welcome! If you have a feature request or found a bug, please open an issue or submit a pull request.

## 📄 License
This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
