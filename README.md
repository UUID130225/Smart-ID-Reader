# Tài liệu Phần mềm Quét CCCD qua Cổng COM

**Tên phần mềm:** CCCD Scanner  
**Phiên bản:** 1.0  
**Ngày soạn:** 18/05/2026  

---

## 1. Tổng quan

Phần mềm hỗ trợ đọc dữ liệu từ mã QR trên Căn cước công dân (CCCD) thông qua máy quét kết nối cổng COM (serial). Ứng dụng hoạt động ở chế độ nền tương tự UniKey — hiển thị hộp thoại nhỏ gọn, có thể thu vào khay hệ thống (system tray), và tự động xử lý dữ liệu quét được.

---

## 2. Giao diện người dùng

### 2.1 Hộp thoại chính

- Giao diện dạng hộp thoại nhỏ gọn, **tương tự UniKey**.
- Hiển thị tự động khi khởi động.
- Có nút **Đóng** (ẩn xuống system tray, không thoát hẳn).
- Có nút **Thoát** (đóng hẳn ứng dụng).
- Có nút **Mở rộng** để hiển thị phần cấu hình nâng cao.

### 2.2 Hai cột thông tin chính

| Cột | Nội dung |
|-----|----------|
| Cổng COM | Tên cổng đang được chọn/kết nối |
| Định dạng ngôn ngữ | Mặc định: UTF-8 |

### 2.3 Tùy chọn khởi động

- **Khởi chạy ngầm mặc định:**
  - Phần mềm được cấu hình mặc định tự động khởi chạy cùng Windows ở chế độ ngầm (`--hidden`) thông qua API gốc `app.setLoginItemSettings()` của Electron.
  - Không hiển thị hộp thoại gây cản trở khi bật máy, ứng dụng nằm gọn trong System Tray.

---

## 3. Quản lý cổng COM

### 3.1 Tự động quét và cập nhật cổng

- Khi khởi động hoặc khi hoạt động, ứng dụng tự động quét toàn bộ driver cổng COM.
- **Cơ chế cập nhật danh sách trực tiếp (Live Refresh):**
  - Quét ngầm định kỳ mỗi `3 giây`.
  - Tự động nạp lại danh sách cổng khi người dùng rê chuột (`mouseenter`) vào ô chọn cổng.
  - Hiển thị đầy đủ tên thiết bị dưới thanh trạng thái và danh sách (ví dụ: `COM4 (Prolific USB-to-Serial Comm Port)`).

### 3.2 Lưu và nhận diện định danh phần cứng (Hardware ID)

- Khi người dùng chọn cổng và bấm **Lưu cấu hình**, hệ thống lưu trữ:
  - Tên cổng COM hiện tại (ví dụ: `COM3`).
  - Tên thiết bị (`deviceName`, ví dụ: `Prolific USB-to-Serial Comm Port`).
  - Mã định danh phần cứng (`vendorId` & `productId`, ví dụ: `067B:2303`).
- **Các lần khởi động sau:** Ưu tiên quét tìm thiết bị theo `vendorId`/`productId` (chính xác 100%), sau đó dự phòng tìm theo `deviceName`. Đảm bảo nhận diện đúng máy quét ngay cả khi người dùng cắm sang cổng USB khác làm thay đổi số COM.

### 3.3 Tự động kết nối lại khi mất cổng hoặc đổi cổng

- Nếu cổng COM bị ngắt kết nối (rút USB hoặc lỏng cáp), ứng dụng:
  - Phát hiện mất kết nối ngay lập tức.
  - Vòng lặp ngầm tự động quét tìm lại thiết bị mỗi `10 giây`.
  - Khi cắm lại (kể cả cắm sang cổng USB khác thành `COM4`, `COM5`), hệ thống tự động nhận diện theo mã phần cứng và **tự động kết nối lại** mà không cần thao tác thủ công.

---

## 4. Cấu hình kết nối

### 4.1 Thông số mặc định

| Thông số | Giá trị mặc định | Các tùy chọn |
|----------|------------------|--------------|
| Baud Rate | `9600` | 1200, 2400, 4800, **9600**, 19200, 38400, 57600, 115200 |
| Data Bits | `8` | 5, 6, 7, **8** |
| Parity | `None` | **None**, Even, Odd, Mark, Space |
| Stop Bits | `1` | **1**, 1.5, 2 |
| Encoding | `UTF-8` | **UTF-8**, ASCII |

### 4.2 Lưu cấu hình

- Toàn bộ thông số trên được **lưu vào file cấu hình** (ví dụ: `%APPDATA%\CCCDScanner\config.json`).
- Các lần chạy tiếp theo tự động nạp cấu hình đã lưu, không cần cài đặt lại.

### 4.3 Giao diện cấu hình nâng cao

- Hiển thị khi nhấn nút **Mở rộng**.
- Cho phép chỉnh sửa tất cả thông số ở mục 4.1.
- Có nút **Lưu cấu hình** và **Đặt lại mặc định**.

---

## 5. Xử lý dữ liệu quét

### 5.1 Đọc dữ liệu từ cổng serial

- Lắng nghe dữ liệu từ cổng COM theo cấu hình đã thiết lập.
- Dữ liệu từ mã QR trên CCCD gửi về dạng chuỗi ký tự, **mã hóa UTF-8**.
- Ứng dụng tự động chuyển đổi sang UTF-8 nếu cần.

### 5.2 Phân tích dữ liệu CCCD

Dữ liệu QR trên CCCD theo chuẩn Bộ Công an, các trường phân cách bằng ký tự `|`:

```
<Số CCCD>|<Số CMND cũ>|<Họ và tên>|<Ngày sinh>|<Giới tính>|<Địa chỉ>|<Ngày cấp>
```

**Ví dụ:**
```
079084012345|123456789|NGUYEN VAN A|01011990|Nam|Ha Noi|15052021
```

### 5.3 Đầu ra

- Sau khi phân tích thành công, dữ liệu được:
  - Hiển thị trong log/console của ứng dụng.
  - Chuyển tiếp đến ứng dụng đang focus (giống cách UniKey gõ chữ) hoặc ghi vào clipboard.
  - *(Tùy yêu cầu mở rộng sau)*: gọi API, ghi vào database.

---

## 6. Luồng hoạt động

```
Khởi động Windows
    │
    ▼
Khởi động CCCD Scanner (nếu đã tích "Khởi chạy cùng máy")
    │
    ▼
Nạp cấu hình đã lưu (cổng, baud rate, encoding,...)
    │
    ▼
Quét driver cổng COM → Tìm cổng đã lưu tên → Kết nối tự động
    │
    ├──[Tìm thấy]──→ Kết nối → Lắng nghe dữ liệu QR
    │
    └──[Không tìm thấy]──→ Quét lại mỗi 10 giây
                               │
                               └──[Xuất hiện]──→ Kết nối tự động
```

---

## 7. Yêu cầu phi chức năng

| Yêu cầu | Chi tiết |
|---------|----------|
| Hệ điều hành | Windows 10/11 (64-bit) |
| Hiệu năng | Phản hồi quét < 200ms sau khi nhận đủ dữ liệu |
| Khởi động | Đăng ký registry: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` |
| Lưu cấu hình | File JSON tại `%APPDATA%\CCCDScanner\config.json` |
| Driver | Hỗ trợ Prolific USB-to-Serial, CH340, FTDI và các driver COM phổ biến |
| Encoding | Mặc định UTF-8, hỗ trợ chuyển đổi tự động |
| Tray icon | Hiển thị icon trong system tray, click để mở lại hộp thoại |

---

## 8. Công nghệ đề xuất triển khai

### Phương án 1: Electron + Node.js 

- **Ưu điểm:** Cross-platform, giao diện HTML/CSS đẹp, hệ sinh thái npm phong phú.
- **Thư viện chính:**
  - `serialport` — đọc/ghi cổng COM
  - `node-auto-launch` — khởi động cùng Windows
  - `electron-store` — lưu cấu hình
  - `electron-tray` — system tray


## 9. Cấu trúc file cấu hình (config.json)

```json
{
  "port": "COM3",
  "baudRate": 9600,
  "dataBits": 8,
  "parity": "None",
  "stopBits": 1,
  "encoding": "UTF-8",
  "startWithWindows": true,
  "autoReconnect": true,
  "reconnectIntervalSeconds": 10
}
```

---

## 10. Các trường hợp đặc biệt cần xử lý

| Tình huống | Xử lý |
|-----------|-------|
| Không tìm thấy cổng khi khởi động | Hiển thị trạng thái "Đang chờ thiết bị", quét lại mỗi 10 giây |
| Mất kết nối trong khi đang quét | Hiển thị cảnh báo, tự động reconnect khi cổng xuất hiện lại |
| Dữ liệu QR không đúng định dạng | Bỏ qua, ghi log lỗi, tiếp tục lắng nghe |
| Nhiều cổng cùng tên (VD: COM3 bị đổi) | Ưu tiên cổng có tên khớp đúng, nếu không tìm thấy thì chờ |
| Người dùng bỏ tích "Khởi chạy cùng máy" | Xóa registry key tương ứng ngay lập tức |

---

*Tài liệu này mô tả yêu cầu ban đầu và có thể được bổ sung trong quá trình phát triển.*
