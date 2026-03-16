const { app, BaseWindow, WebContentsView, shell, dialog, ipcMain, BrowserWindow, Tray, Menu, nativeTheme } = require('electron');
const path = require('path');
const fs = require('fs');

const userDataPath = app.getPath('userData');
const settingsPath = path.join(userDataPath, 'settings.json');
const windowStatePath = path.join(userDataPath, 'window-state.json');

function loadSettings() {
  try {
    if (fs.existsSync(settingsPath)) {
      return JSON.parse(fs.readFileSync(settingsPath, 'utf8'));
    }
  } catch (e) {}
  return { 
    defaultDownloadsFolder: app.getPath('downloads'),
    theme: 'system',
    minimizeToTray: false,
    closeToTray: false,
    homePage: ""
  };
}

function saveSettings(settings) {
  fs.writeFileSync(settingsPath, JSON.stringify(settings, null, 2));
  applyTheme(settings.theme);
}

function applyTheme(theme) {
  nativeTheme.themeSource = theme || 'system';
}

function loadWindowState() {
  try {
    if (fs.existsSync(windowStatePath)) {
      return JSON.parse(fs.readFileSync(windowStatePath, 'utf8'));
    }
  } catch (e) {}
  return { width: 1200, height: 800 };
}

function saveWindowState(state) {
  fs.writeFileSync(windowStatePath, JSON.stringify(state, null, 2));
}

let config;
try {
  config = JSON.parse(fs.readFileSync(path.join(__dirname, 'config.json'), 'utf8'));
} catch (e) {
  config = { appName: "Web App", url: "https://example.com" };
}

let mainWindow;
let splashWindow;
let webContentsView;
let tray;
let isQuitting = false;
let settingsWindow;

Menu.setApplicationMenu(null);

function createSplash() {
  splashWindow = new BrowserWindow({
    width: 500, height: 350, frame: false, transparent: true,
    alwaysOnTop: true, center: true,
    webPreferences: { nodeIntegration: true, contextIsolation: false }
  });
  splashWindow.loadFile('splash.html');
}

function createWindow() {
  const windowState = loadWindowState();
  const settings = loadSettings();
  applyTheme(settings.theme);

  mainWindow = new BaseWindow({
    width: windowState.width, height: windowState.height,
    x: windowState.x, y: windowState.y,
    title: config.appName,
    show: false, icon: path.join(__dirname, 'icon.png')
  });

  if (windowState.isMaximized) mainWindow.maximize();

  webContentsView = new WebContentsView();
  mainWindow.contentView.addChildView(webContentsView);

  const resize = () => {
    const { width, height } = mainWindow.getContentBounds();
    webContentsView.setBounds({ x: 0, y: 0, width, height });
  };
  mainWindow.on('resize', resize);
  resize();

  webContentsView.webContents.on('did-finish-load', () => {
    const cssPath = path.join(__dirname, 'user.css');
    if (fs.existsSync(cssPath)) {
      webContentsView.webContents.insertCSS(fs.readFileSync(cssPath, 'utf8'));
    }
  });

  webContentsView.webContents.on('before-input-event', (event, input) => {
    if (input.control && input.key === ',') { openSettings(); event.preventDefault(); }
    if (input.key === 'F5') { webContentsView.webContents.reload(); }
    if (input.key === 'F12') { webContentsView.webContents.openDevTools(); }
  });

  webContentsView.webContents.loadURL(settings.homePage || config.url);

  // Direct native save dialog for downloads
  webContentsView.webContents.session.on('will-download', async (event, item) => {
    const settings = loadSettings();
    const result = await dialog.showSaveDialog(mainWindow, {
      defaultPath: path.join(settings.defaultDownloadsFolder, item.getFilename())
    });

    if (!result.canceled && result.filePath) {
      item.setSavePath(result.filePath);
    } else {
      item.cancel();
    }
  });

  mainWindow.on('close', (event) => {
    if (loadSettings().closeToTray && !isQuitting) {
      event.preventDefault();
      mainWindow.hide();
    } else {
      saveWindowState({ ...mainWindow.getBounds(), isMaximized: (mainWindow.isMaximized ? mainWindow.isMaximized() : false) });
    }
  });

  if (splashWindow) {
    setTimeout(() => {
      splashWindow.close();
      splashWindow = null;
      mainWindow.show();
    }, 1500);
  }
}

function openSettings() {
  if (settingsWindow) { settingsWindow.focus(); return; }
  settingsWindow = new BrowserWindow({
    width: 550, height: 600, parent: mainWindow, modal: true, title: "App Settings",
    autoHideMenuBar: true,
    webPreferences: { nodeIntegration: true, contextIsolation: false }
  });
  settingsWindow.loadFile('settings.html');
  settingsWindow.on('closed', () => { settingsWindow = null; });
}

function createTray() {
  tray = new Tray(path.join(__dirname, 'icon.png'));
  tray.setToolTip(config.appName);
  tray.setContextMenu(Menu.buildFromTemplate([
    { label: 'Show ' + config.appName, click: () => mainWindow.show() },
    { type: 'separator' },
    { label: 'Settings', click: openSettings },
    { type: 'separator' },
    { label: 'Quit', click: () => { isQuitting = true; app.quit(); } }
  ]));
  tray.on('click', () => mainWindow.show());
}

app.whenReady().then(() => {
  createSplash();
  createWindow();
  createTray();
});

ipcMain.on('get-settings', (event) => event.returnValue = loadSettings());
ipcMain.on('save-settings', (event, s) => saveSettings(s));
ipcMain.on('get-config', (event) => event.returnValue = config);
ipcMain.on('open-settings', () => openSettings());
ipcMain.on('browse-folder', async (event) => {
  const result = await dialog.showOpenDialog(settingsWindow, { properties: ['openDirectory'] });
  if (!result.canceled) event.reply('folder-selected', result.filePaths[0]);
});
