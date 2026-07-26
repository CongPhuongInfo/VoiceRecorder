# VoiceRecorder

Tool thu âm dataset để train TTS (Piper) bằng chính giọng của bạn.
VB.NET, .NET 9, WinForms, dùng NAudio để ghi âm và convert audio.

## Cách build

Yêu cầu: đã cài **.NET 9 SDK** (không cần Visual Studio).

```
build.bat
```

File chạy sẽ nằm ở: `bin\Release\net9.0-windows\VoiceRecorder.exe`

Lần build đầu tiên cần internet để NuGet tải gói `NAudio`.

## Cách dùng

1. Mở `VoiceRecorder.exe`.
2. Chọn microphone ở góc trên bên phải (nếu có nhiều thiết bị).
3. Đọc câu hiển thị giữa màn hình, bấm **Space** để bắt đầu ghi, bấm **Space** lần nữa để dừng.
4. File `.wav` sẽ tự lưu vào `dataset\wavs\`, và dòng text tương ứng tự ghi vào `dataset\metadata.csv`.
5. Bấm **P** để nghe lại câu vừa thu. Nếu đọc sai/vấp, bấm **R** để thu lại (ghi đè lên câu cũ).
6. Bấm **Enter** hoặc mũi tên phải để sang câu tiếp theo. Mũi tên trái để quay lại câu trước.
7. Có thể tắt app giữa chừng, mở lại sẽ tự nhảy tới câu đầu tiên chưa thu (tiến độ được lưu qua `metadata.csv`).

## Format output (chuẩn Piper)

- `dataset/wavs/0001.wav`, `0002.wav`, ... — mono, 16-bit, 22050Hz.
- `dataset/metadata.csv` — mỗi dòng dạng `id|text`, ví dụ:
  ```
  0001|Xin chào, hôm nay trời rất đẹp.
  0002|Tôi đang học cách lập trình bằng ngôn ngữ Visual Basic.
  ```

Thư mục `dataset/` này đưa thẳng vào bước train Piper sau này.

## Tùy chỉnh câu mẫu

Sửa file `script.txt` (mỗi dòng 1 câu). File này hiện có sẵn ~120 câu tiếng Việt
đa dạng chủ đề/thanh điệu để bạn bắt đầu — nên bổ sung thêm nếu muốn dataset lớn hơn
(khuyến nghị tối thiểu 30 phút audio, càng nhiều càng tốt).

## Lưu ý thu âm

- Phòng yên tĩnh, hạn chế tiếng vang/echo.
- Giữ khoảng cách mic ổn định, tốc độ đọc tự nhiên, không quá nhanh.
- Theo dõi thanh volume meter: tránh để mức quá thấp (giọng nhỏ) hoặc kịch kim liên tục (dễ vỡ tiếng/clip).
- Nếu thu nhiều buổi/nhiều ngày, cố giữ **âm lượng và khoảng cách mic đều nhau** giữa các buổi để dataset đồng nhất.
- Mệt hoặc khàn giọng giữa chừng thì nên nghỉ, thu tiếp lúc khác — giọng gằn vì mệt sẽ ảnh hưởng chất lượng model.
- Không cần đợi thu hết mới train thử: thu khoảng 100–150 câu đầu có thể train thử ngay để phát hiện sớm vấn đề
  (mic rè, sai format...), tránh tốn công thu hết cả trăm câu rồi mới phát hiện lỗi.

## Bước tiếp theo: train giọng bằng Google Colab

Sau khi thu xong (hoặc thu thử một phần để test), làm theo thứ tự:

1. **Nén dataset:** nén thư mục `dataset` (chứa `wavs/` và `metadata.csv`) thành file `dataset.zip`.
2. **Upload lên Google Drive:** đưa `dataset.zip` lên Drive, tốt nhất để ngay trong `MyDrive` gốc
   (nếu để trong thư mục con thì nhớ sửa đường dẫn `DATASET_ZIP` ở bước 3 trong notebook).
3. **Mở Google Colab:** truy cập [colab.research.google.com](https://colab.research.google.com/).
4. **Tải notebook lên:** bấm **"Tải sổ tay lên"** → chọn tab **"Tải lên"** (Upload) trong hộp thoại hiện ra → chọn trực tiếp file
   `Train_Piper_FineTune.ipynb` từ bộ nhớ máy/điện thoại (không cần đưa file này lên Drive trước, chỉ `dataset.zip` mới cần).
5. **Bật GPU:** vào **Runtime → Change runtime type → GPU (T4)** trước khi chạy.
6. **Chạy từng ô theo thứ tự** (bấm nút ▶ ở từng ô, từ trên xuống dưới). Notebook dùng repo **piper1-gpl**
   (bản Piper mới, tương thích Python 3.12 trên Colab hiện tại — bản cũ dễ lỗi cài đặt do ghim phiên bản `torch` quá cũ). Notebook sẽ tự:
   - Kết nối Google Drive và giải nén dataset.
   - Cài piper1-gpl + tải sẵn checkpoint tiếng Việt **vais1000-medium** để fine-tune (không train từ đầu).
   - Convert `metadata.csv` sang đúng format piper1-gpl cần.
   - Fine-tune không giới hạn epoch — bạn tự dừng (**Runtime → Interrupt execution**) khi thấy ổn, rồi export thử nghe.
   - Export ra `.onnx` + `.onnx.json`, cho nghe thử ngay trong Colab, và lưu file cuối cùng vào Google Drive.

**Lưu ý:** Colab bản miễn phí giới hạn thời gian chạy mỗi phiên và có thể ngắt bất chợt. Nếu bị ngắt giữa chừng lúc train,
mở lại notebook, chạy lại từ đầu tới bước cài đặt, rồi ở bước training thay `--resume_from_checkpoint` bằng checkpoint
mới nhất trong `training_dir` (hoặc bản đã sao lưu ở Drive từ bước 7b trong notebook) để train tiếp, không cần train lại từ đầu.

File `.onnx` + `.onnx.json` cuối cùng chính là thứ app .NET 9 chính (bước sau) sẽ load để chạy TTS bằng giọng bạn.
