const { app, BrowserWindow, Tray, Menu, ipcMain, clipboard, dialog } = require('electron');
const path = require('path');
const Store = require('electron-store');
const { SerialPort } = require('serialport');
const fs = require('fs');
const { StringDecoder } = require('string_decoder');

// Global variables
let mainWindow;
let tray = null;
let serialPort = null;
let reconnectTimer = null;
let currentStatus = { connected: false, message: 'Đang khởi động...' };
let robot = null;

// Logger
function logError(errorMsg) {
  try {
    const logPath = path.join(app.getPath('userData'), 'error.log');
    const time = new Date().toISOString();
    fs.appendFileSync(logPath, `[${time}] ${errorMsg}\n`);
    console.error(errorMsg);
  } catch (e) {
    console.error('Cannot write log:', e);
  }
}

// Load robotjs at top level
try {
  robot = require('robotjs');
} catch (e) {
  logError('Robotjs load error: ' + e.message + '. Tính năng tự động dán sẽ không hoạt động.');
}

// Check for admin privileges (Windows specific)
function isElevated() {
  try {
    // Attempt to write to a protected directory or check process environment
    // On Windows, checking if we can write to System32 or using a shell command is common.
    // A simpler way in Electron on Windows is to check if we are running with admin rights.
    // For now, we'll use a simple flag that can be checked later.
    return process.platform === 'win32' && require('child_process').spawnSync('net', ['session']).status === 0;
  } catch (e) {
    return false;
  }
}

const isAdmin = isElevated();
logError(`Application started. Admin Mode: ${isAdmin}`);

// Auto-relaunch as admin if not elevated
if (process.platform === 'win32' && !isAdmin && !process.argv.includes('--no-admin')) {
  const { spawn } = require('child_process');
  const exePath = app.getPath('exe');
  const args = process.argv.slice(1);
  
  // Use PowerShell to start the process with 'runAs' verb (Admin)
  const psCommand = `Start-Process -FilePath "${exePath}" -ArgumentList "${args.join(' ')}" -Verb RunAs`;
  
  spawn('powershell.exe', ['-Command', psCommand], {
    detached: true,
    stdio: 'ignore'
  }).unref();
  
  app.quit();
  process.exit(0);
}

const store = new Store({
  defaults: {
    port: '',
    baudRate: 9600,
    dataBits: 8,
    parity: 'none',
    stopBits: 1,
    encoding: 'utf8',
    startWithWindows: true,
    autoReconnect: true,
    reconnectIntervalSeconds: 10
  }
});

function broadcastStatus(status) {
  currentStatus = { ...currentStatus, ...status };
  if (mainWindow && !mainWindow.isDestroyed()) {
    mainWindow.webContents.send('status-update', currentStatus);
  }
}

function setupAutoLaunch() {
  const enabled = store.get('startWithWindows');
  try {
    app.setLoginItemSettings({
      openAtLogin: enabled,
      path: app.getPath('exe'),
      args: ['--hidden']
    });
  } catch (e) {
    logError('Error setting login item: ' + e.message);
  }
}

function parseCCCDData(rawString) {
  return rawString.trim();
}

function connectSerialPort(config) {
  if (serialPort && serialPort.isOpen) {
    serialPort.close();
  }

  if (reconnectTimer) {
    clearInterval(reconnectTimer);
    reconnectTimer = null;
  }

  if (!config.port) {
    broadcastStatus({ connected: false, message: 'Chưa chọn cổng COM' });
    startReconnectLoop();
    return;
  }

  const displayName = config.deviceName ? `${config.port} (${config.deviceName})` : config.port;
  broadcastStatus({ connected: false, message: `Đang kết nối ${displayName}...` });

  try {
    serialPort = new SerialPort({
      path: config.port,
      baudRate: parseInt(config.baudRate) || 9600,
      dataBits: parseInt(config.dataBits) || 8,
      parity: config.parity.toLowerCase(),
      stopBits: parseFloat(config.stopBits) || 1,
      autoOpen: false
    });

    serialPort.open((err) => {
      if (err) {
        logError('Error opening port ' + config.port + ': ' + err.message);
        broadcastStatus({ connected: false, message: `Lỗi kết nối: ${err.message}` });
        startReconnectLoop();
        return;
      }
      broadcastStatus({ connected: true, message: `Đã kết nối ${displayName}` });
    });

    let buffer = '';
    let scanTimeout = null;
    let decoder = new StringDecoder(config.encoding === 'ascii' ? 'ascii' : 'utf8');

    function processBuffer() {
      if (!buffer) return;
      const completeData = buffer.trim();
      buffer = '';
      decoder = new StringDecoder(config.encoding === 'ascii' ? 'ascii' : 'utf8');

      if (completeData) {
        logError(`Scanned data: ${completeData}`);
        const formatted = parseCCCDData(completeData);
        clipboard.writeText(formatted);
        
        if (robot) {
          setTimeout(() => {
            try {
              robot.keyTap('v', ['control']);
              setTimeout(() => { 
                robot.keyTap('enter');
              }, 250);
            } catch (err) {
              logError('Robotjs execution error: ' + err.message);
            }
          }, 150);
        } else {
          logError('Robotjs not available, skipping auto-paste.');
        }

        if (mainWindow && !mainWindow.isDestroyed()) {
          mainWindow.webContents.send('scan-result', { raw: completeData, formatted, time: new Date().toLocaleTimeString() });
        }
      }
    }

    serialPort.on('data', (data) => {
      const text = decoder.write(data);
      buffer += text;
      
      if (scanTimeout) clearTimeout(scanTimeout);

      if (buffer.includes('\n') || buffer.includes('\r')) {
        processBuffer();
      } else {
        scanTimeout = setTimeout(processBuffer, 50);
      }
    });

    serialPort.on('close', () => {
      broadcastStatus({ connected: false, message: `Mất kết nối ${config.port}. Đang thử lại...` });
      startReconnectLoop();
    });

    serialPort.on('error', (err) => {
      logError('Serial port error: ' + err.message);
      broadcastStatus({ connected: false, message: `Lỗi cổng: ${err.message}` });
      startReconnectLoop();
    });

  } catch (err) {
    logError('Connection initialization error: ' + err.message);
    broadcastStatus({ connected: false, message: `Lỗi khởi tạo: ${err.message}` });
    startReconnectLoop();
  }
}

function startReconnectLoop() {
  const config = store.store;
  if (!config.autoReconnect) return;

  if (!reconnectTimer) {
    reconnectTimer = setInterval(async () => {
      if (serialPort && serialPort.isOpen) {
        clearInterval(reconnectTimer);
        reconnectTimer = null;
        return;
      }

      try {
        const ports = await SerialPort.list();
        let found = ports.find(p => p.path === config.port);
        
        if (!found && config.vendorId && config.productId) {
          found = ports.find(p => p.vendorId === config.vendorId && p.productId === config.productId);
        }

        if (!found && config.deviceName) {
          found = ports.find(p => p.friendlyName && p.friendlyName.includes(config.deviceName));
        }

        if (found) {
          if (found.path !== config.port) {
            config.port = found.path;
            store.set('port', found.path);
          }
          connectSerialPort(config);
        }
      } catch (e) {
        logError('Error listing ports in loop: ' + e.message);
      }
    }, (config.reconnectIntervalSeconds || 10) * 1000);
  }
}

function createWindow() {
  const isHiddenStartup = process.argv.includes('--hidden');

  mainWindow = new BrowserWindow({
    width: 480,
    height: 175,
    show: !isHiddenStartup,
    resizable: false,
    frame: false,
    transparent: true,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      nodeIntegration: false,
      contextIsolation: true
    },
    icon: path.join(__dirname, 'assets', 'icon.png')
  });

  mainWindow.loadFile('renderer/index.html');

  mainWindow.webContents.on('did-finish-load', () => {
    broadcastStatus(currentStatus);
    // Notify if not admin
    if (!isAdmin) {
      mainWindow.webContents.executeJavaScript(`
        const statusMsg = document.getElementById('status-msg');
        if (statusMsg) {
          statusMsg.title = 'Phần mềm đang chạy ở quyền người dùng thường. Một số phần mềm đích có thể yêu cầu quyền Administrator để tự động dán dữ liệu.';
        }
      `);
    }
  });
}

function createTray() {
  const iconPath = path.join(__dirname, 'assets', 'icon.png');
  tray = new Tray(iconPath);
  tray.setToolTip('CCCD Scanner' + (isAdmin ? ' (Administrator)' : ''));

  const contextMenu = Menu.buildFromTemplate([
    { label: 'Mở bảng điều khiển', click: () => mainWindow.show() },
    { label: isAdmin ? 'Đang chạy quyền Admin ✓' : 'Khuyên dùng: Chạy quyền Admin', enabled: false },
    { type: 'separator' },
    { label: 'Thoát', click: () => { app.isQuiting = true; app.quit(); } }
  ]);

  tray.setContextMenu(contextMenu);
  tray.on('click', () => {
    mainWindow.show();
  });
}

const gotTheLock = app.requestSingleInstanceLock();

if (!gotTheLock) {
  // If we couldn't get the lock, it means another instance is already running.
  // If this instance is Admin, we should tell the user to close the other one.
  logError('Another instance is already running. Exiting...');
  app.quit();
} else {
  app.on('second-instance', (event, commandLine, workingDirectory) => {
    if (mainWindow) {
      if (!mainWindow.isVisible()) mainWindow.show();
      if (mainWindow.isMinimized()) mainWindow.restore();
      mainWindow.focus();
      
      // If the second instance tried to run as admin, notify the first instance
      if (commandLine.includes('--admin-attempt')) {
        dialog.showMessageBox(mainWindow, {
          type: 'info',
          title: 'Thông báo',
          message: 'Bạn đang cố gắng chạy phần mềm với quyền Administrator, nhưng một phiên bản khác đang chạy ngầm. Vui lòng thoát hẳn phần mềm ở khay hệ thống (Tray Icon) trước khi chạy lại với quyền Administrator.'
        });
      }
    }
  });

  app.whenReady().then(async () => {
    setupAutoLaunch();
    createWindow();
    createTray();

    const config = store.store;
    try {
      const ports = await SerialPort.list();
      let found = ports.find(p => p.path === config.port);
      if (!found && config.vendorId && config.productId) {
        found = ports.find(p => p.vendorId === config.vendorId && p.productId === config.productId);
      }
      if (!found && config.deviceName) {
        found = ports.find(p => p.friendlyName && p.friendlyName.includes(config.deviceName));
      }

      if (found) {
        if (found.path !== config.port) {
          config.port = found.path;
          store.set('port', found.path);
        }
        connectSerialPort(config);
      } else {
        broadcastStatus({ connected: false, message: `Không tìm thấy cổng ${config.port || '(Chưa chọn)'}` });
        startReconnectLoop();
      }
    } catch (e) {
      logError('Startup port listing error: ' + e.message);
    }

    app.on('activate', () => {
      if (BrowserWindow.getAllWindows().length === 0) createWindow();
    });
  });
}

// IPC Handlers
ipcMain.handle('list-ports', async () => {
  try {
    return await SerialPort.list();
  } catch (e) {
    return [];
  }
});

ipcMain.handle('connect', (event, cfg) => {
  store.set(cfg);
  connectSerialPort(cfg);
  return currentStatus;
});

ipcMain.handle('disconnect', () => {
  if (serialPort && serialPort.isOpen) {
    serialPort.close();
  }
  if (reconnectTimer) {
    clearInterval(reconnectTimer);
    reconnectTimer = null;
  }
  broadcastStatus({ connected: false, message: 'Đã ngắt kết nối' });
  return currentStatus;
});

ipcMain.handle('save-config', (event, cfg) => {
  store.set(cfg);
  connectSerialPort(cfg);
  return store.store;
});

ipcMain.handle('get-config', () => {
  return store.store;
});

ipcMain.handle('hide-window', () => {
  if (mainWindow) mainWindow.hide();
});

ipcMain.handle('exit-app', () => {
  app.isQuiting = true;
  app.quit();
});

ipcMain.handle('resize-window', (event, width, height) => {
  if (mainWindow && !mainWindow.isDestroyed()) {
    mainWindow.setResizable(true);
    mainWindow.setSize(width, height);
    mainWindow.setResizable(false);
  }
});
