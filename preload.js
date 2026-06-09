const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('api', {
  listPorts: () => ipcRenderer.invoke('list-ports'),
  connect: (cfg) => ipcRenderer.invoke('connect', cfg),
  disconnect: () => ipcRenderer.invoke('disconnect'),
  saveConfig: (cfg) => ipcRenderer.invoke('save-config', cfg),
  getConfig: () => ipcRenderer.invoke('get-config'),
  hideWindow: () => ipcRenderer.invoke('hide-window'),
  exitApp: () => ipcRenderer.invoke('exit-app'),
  resizeWindow: (w, h) => ipcRenderer.invoke('resize-window', w, h),
  onScanResult: (callback) => ipcRenderer.on('scan-result', (event, data) => callback(data)),
  onStatusUpdate: (callback) => ipcRenderer.on('status-update', (event, data) => callback(data)),
});
