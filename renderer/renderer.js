// DOM Elements
const comPortSelect = document.getElementById('com-port');
const btnHide = document.getElementById('btn-hide');
const btnExpand = document.getElementById('btn-expand');
const btnExit = document.getElementById('btn-exit');
const btnMinimize = document.getElementById('btn-minimize');
const btnClose = document.getElementById('btn-close');

const panelAdvanced = document.getElementById('panel-advanced');
const baudRateSelect = document.getElementById('baud-rate');
const dataBitsSelect = document.getElementById('data-bits');
const paritySelect = document.getElementById('parity');
const stopBitsSelect = document.getElementById('stop-bits');
const encodingSelect = document.getElementById('encoding');
const btnSaveAdvanced = document.getElementById('btn-save-advanced');
const btnResetAdvanced = document.getElementById('btn-reset-advanced');

const statusDot = document.getElementById('status-dot');
const statusMsg = document.getElementById('status-msg');

let isExpanded = false;

// Load & Populate Ports
async function loadPorts(selectedPort = '') {
  comPortSelect.innerHTML = '<option value="">Chọn cổng COM...</option>';
  const ports = await window.api.listPorts();
  
  ports.forEach(port => {
    const option = document.createElement('option');
    option.value = port.path;
    // Hiển thị tên đầy đủ nếu có, ví dụ: COM3 (Prolific USB-to-Serial)
    option.textContent = port.friendlyName ? `${port.path} (${port.friendlyName})` : port.path;
    if (port.path === selectedPort) {
      option.selected = true;
    }
    comPortSelect.appendChild(option);
  });
}

// Load Saved Config
async function loadConfig() {
  const config = await window.api.getConfig();
  
  await loadPorts(config.port);
  
  if (config.baudRate) baudRateSelect.value = config.baudRate;
  if (config.dataBits) dataBitsSelect.value = config.dataBits;
  if (config.parity) paritySelect.value = config.parity.toLowerCase();
  if (config.stopBits) stopBitsSelect.value = config.stopBits;
  if (config.encoding) encodingSelect.value = config.encoding;
}

// Save Config Handler
async function saveCurrentConfig() {
  const ports = await window.api.listPorts();
  const selectedPort = comPortSelect.value;
  const foundPort = ports.find(p => p.path === selectedPort);
  
  let deviceName = '';
  let vendorId = '';
  let productId = '';
  
  if (foundPort) {
    if (foundPort.friendlyName) {
      deviceName = foundPort.friendlyName.replace(/\s*\([^)]*\)$/, '').trim();
    }
    vendorId = foundPort.vendorId || '';
    productId = foundPort.productId || '';
  }

  const config = {
    port: selectedPort,
    deviceName: deviceName,
    vendorId: vendorId,
    productId: productId,
    baudRate: parseInt(baudRateSelect.value),
    dataBits: parseInt(dataBitsSelect.value),
    parity: paritySelect.value,
    stopBits: parseFloat(stopBitsSelect.value),
    encoding: encodingSelect.value,
    startWithWindows: true
  };
  await window.api.saveConfig(config);
}

// Status Updates
window.api.onStatusUpdate((status) => {
  statusMsg.textContent = status.message;
  if (status.connected) {
    statusDot.classList.add('connected');
    statusDot.classList.remove('disconnected');
  } else {
    statusDot.classList.remove('connected');
    statusDot.classList.add('disconnected');
  }
});

// Scan Result Handler
window.api.onScanResult((result) => {
  // Giao diện đã bỏ log-box, giờ chỉ nháy đèn hoặc giữ nguyên tùy ý
  // Dữ liệu đã được lưu ra file error.log nếu lỗi, quét thành công được dán qua robotjs
});

// Tự động tải lại danh sách cổng khi người dùng rê chuột vào dropdown (tránh làm phiền nếu đang mở chọn)
comPortSelect.addEventListener('mouseenter', async () => {
  if (document.activeElement !== comPortSelect) {
    await loadPorts(comPortSelect.value);
  }
});

// Tự động quét ngầm mỗi 3 giây để phát hiện thiết bị mới cắm vào hoặc rút ra
setInterval(async () => {
  if (document.activeElement !== comPortSelect) {
    await loadPorts(comPortSelect.value);
  }
}, 3000);


comPortSelect.addEventListener('change', saveCurrentConfig);

btnHide.addEventListener('click', () => window.api.hideWindow());
if (btnMinimize) btnMinimize.addEventListener('click', () => window.api.hideWindow());
if (btnClose) btnClose.addEventListener('click', () => window.api.hideWindow());

btnExit.addEventListener('click', () => window.api.exitApp());

const expandText = document.getElementById('expand-text');
const expandIcon = btnExpand.querySelector('.icon');
btnExpand.addEventListener('click', () => {
  isExpanded = !isExpanded;
  if (isExpanded) {
    panelAdvanced.style.display = 'flex';
    if (expandText) expandText.textContent = 'Thu gọn';
    if (expandIcon) expandIcon.textContent = '▲';
    window.api.resizeWindow(480, 420); // Mở rộng chiều cao cửa sổ an toàn qua IPC
  } else {
    panelAdvanced.style.display = 'none';
    if (expandText) expandText.textContent = 'Mở rộng';
    if (expandIcon) expandIcon.textContent = '▼';
    window.api.resizeWindow(480, 175); // Thu gọn chiều cao an toàn qua IPC
  }
});

btnSaveAdvanced.addEventListener('click', async () => {
  await saveCurrentConfig();
  alert('Đã lưu cấu hình kết nối!');
});

btnResetAdvanced.addEventListener('click', async () => {
  baudRateSelect.value = '9600';
  dataBitsSelect.value = '8';
  paritySelect.value = 'none';
  stopBitsSelect.value = '1';
  encodingSelect.value = 'utf8';
  await saveCurrentConfig();
});

// Khởi chạy ban đầu
document.addEventListener('DOMContentLoaded', loadConfig);
