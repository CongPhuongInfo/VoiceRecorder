Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Threading.Tasks
Imports System.Drawing
Imports System.Windows.Forms
Imports NAudio.Wave

Public Class MainForm
    Inherits Form

    ' ===== Cau hinh output cho Piper TTS =====
    Private Const TARGET_SAMPLE_RATE As Integer = 22050
    Private Const TARGET_BITS As Integer = 16
    Private Const TARGET_CHANNELS As Integer = 1

    Private ReadOnly scriptsFolder As String = Path.Combine(Application.StartupPath, "scripts")
    Private ReadOnly outputRoot As String = Path.Combine(Application.StartupPath, "dataset")
    Private wavsFolder As String
    Private metadataPath As String
    Private lastPositionPath As String
    Private currentScriptName As String = ""

    Private sentences As New List(Of String)
    Private recordedIndices As New HashSet(Of Integer)
    Private currentIndex As Integer = 0

    ' ===== Nguong kiem tra chat luong ghi am =====
    Private Const MIN_DURATION_SECONDS As Double = 0.4
    Private Const MIN_PEAK_LEVEL As Single = 0.03
    Private Const MAX_CLIP_RATIO As Double = 0.02   ' 2% so mau bi vo tieng
    Private Const CLIP_SAMPLE_THRESHOLD As Integer = 32000 ' gan max cua Int16 (32767)

    ' ===== Xu ly nang cao ban ghi: cat khoang lang + chuan hoa am luong =====
    Private Const TRIM_SILENCE_THRESHOLD As Single = 0.02F  ' bien do duoi nguong nay coi la khoang lang
    Private Const TRIM_PADDING_MS As Integer = 80           ' giu lai it dem 2 dau de khong cat cut tu
    Private Const TARGET_PEAK As Single = 0.9F              ' muc bien do dinh muon dat toi sau chuan hoa
    Private Const MAX_NORMALIZE_GAIN As Single = 6.0F       ' gioi han khuech dai, tranh khuech dai qua on nen

    ' ===== Dem nguoc truoc khi ghi that + canh bao on nen =====
    Private Const PRE_RECORD_COUNTDOWN_SECONDS As Integer = 2
    Private Const AMBIENT_NOISE_WARN_THRESHOLD As Single = 0.05F

    ' ===== NAudio =====
    Private waveIn As WaveInEvent
    Private tempWriter As WaveFileWriter
    Private tempFilePath As String
    Private isRecording As Boolean = False
    Private currentVolume As Single = 0

    ' thong ke chat luong cho lan ghi hien tai (chi tinh phan SAU khi dem nguoc xong)
    Private recSampleCount As Long = 0
    Private recClippedCount As Long = 0
    Private recPeakLevel As Single = 0
    Private recSampleRate As Integer = 44100

    ' ===== Dem nguoc & huy ghi =====
    Private isCountingDown As Boolean = False
    Private countdownRemaining As Integer = 0
    Private ambientPeak As Single = 0
    Private cancelPending As Boolean = False
    Private countdownTimer As Timer
    Private autoNextTimer As Timer

    ' ===== Waveform live khi dang ghi =====
    Private Const LIVE_WAVEFORM_CAPACITY As Integer = 400
    Private liveWaveformPeaks As New List(Of Single)

    ' ===== UI controls =====
    Private lblProgress As Label
    Private lblSentence As Label
    Private lblStatus As Label
    Private cmbDevice As ComboBox
    Private lblScript As Label
    Private cmbScript As ComboBox
    Private progressOverall As ProgressBar
    Private meterVolume As ProgressBar
    Private waveformPanel As WaveformPanel
    Private btnRecord As Button
    Private btnPlay As Button
    Private btnPrev As Button
    Private btnNext As Button
    Private txtJumpTo As TextBox
    Private btnJumpTo As Button
    Private lblOverallProgress As Label
    Private lblTotalDuration As Label
    Private chkAutoNext As CheckBox
    Private btnExportZip As Button
    Private meterTimer As Timer
    Private isLoadingScript As Boolean = False

    Public Sub New()
        Directory.CreateDirectory(scriptsFolder)
        Directory.CreateDirectory(outputRoot)
        EnsureDefaultScripts()

        InitUI()
        PopulateScriptList()

        If cmbScript.Items.Count = 0 Then
            MessageBox.Show("Khong tim thay bo cau nao trong thu muc 'scripts'. Vui long them file .txt vao do roi mo lai app.",
                             "Thieu du lieu", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            sentences.Add("Chua co bo cau nao. Them file .txt vao thu muc scripts roi mo lai app.")
        End If

        PopulateDevices()
        ShowSentence()
    End Sub

    ' ===================== UI =====================

    Private Sub InitUI()
        Me.Text = "Voice Recorder - Thu am dataset TTS"
        Me.ClientSize = New Size(780, 596)
        Me.StartPosition = FormStartPosition.CenterScreen
        Me.KeyPreview = True
        Me.Font = New Font("Segoe UI", 10)
        Me.MaximizeBox = False
        Me.FormBorderStyle = FormBorderStyle.FixedSingle

        lblProgress = New Label() With {
            .Location = New Point(20, 15), .Width = 400, .Text = "Cau 0 / 0"
        }
        Me.Controls.Add(lblProgress)

        cmbDevice = New ComboBox() With {
            .Location = New Point(430, 12), .Width = 320, .DropDownStyle = ComboBoxStyle.DropDownList
        }
        Me.Controls.Add(cmbDevice)

        lblScript = New Label() With {
            .Location = New Point(20, 48), .Width = 90, .Text = "Kich ban:"
        }
        Me.Controls.Add(lblScript)

        cmbScript = New ComboBox() With {
            .Location = New Point(115, 45), .Width = 635, .DropDownStyle = ComboBoxStyle.DropDownList
        }
        AddHandler cmbScript.SelectedIndexChanged, AddressOf CmbScript_SelectedIndexChanged
        Me.Controls.Add(cmbScript)

        lblSentence = New Label() With {
            .Location = New Point(20, 85), .Width = 720, .Height = 140,
            .Font = New Font("Segoe UI", 18, FontStyle.Bold),
            .TextAlign = ContentAlignment.MiddleCenter,
            .BorderStyle = BorderStyle.FixedSingle
        }
        Me.Controls.Add(lblSentence)

        waveformPanel = New WaveformPanel() With {
            .Location = New Point(20, 232), .Width = 720, .Height = 70
        }
        Me.Controls.Add(waveformPanel)

        meterVolume = New ProgressBar() With {
            .Location = New Point(20, 306), .Width = 720, .Height = 14, .Maximum = 100
        }
        Me.Controls.Add(meterVolume)

        lblStatus = New Label() With {
            .Location = New Point(20, 324), .Width = 720, .Text = "San sang."
        }
        Me.Controls.Add(lblStatus)

        Dim lblJump As New Label() With {
            .Location = New Point(20, 352), .Width = 90, .Text = "Di toi cau:"
        }
        Me.Controls.Add(lblJump)

        txtJumpTo = New TextBox() With {
            .Location = New Point(115, 349), .Width = 60
        }
        Me.Controls.Add(txtJumpTo)

        btnJumpTo = New Button() With {
            .Location = New Point(182, 347), .Width = 75, .Height = 28, .Text = "Di toi"
        }
        AddHandler btnJumpTo.Click, AddressOf BtnJumpTo_Click
        Me.Controls.Add(btnJumpTo)

        lblOverallProgress = New Label() With {
            .Location = New Point(270, 352), .Width = 470, .Height = 20,
            .TextAlign = ContentAlignment.MiddleRight,
            .ForeColor = Color.FromArgb(0, 90, 160),
            .Text = "Tong dataset: 0 / 0 cau (0%)"
        }
        Me.Controls.Add(lblOverallProgress)

        lblTotalDuration = New Label() With {
            .Location = New Point(270, 376), .Width = 470, .Height = 20,
            .TextAlign = ContentAlignment.MiddleRight,
            .ForeColor = Color.Gray,
            .Text = "Tong thoi luong da thu: 0 phut 0 giay"
        }
        Me.Controls.Add(lblTotalDuration)

        btnPrev = New Button() With {.Location = New Point(20, 406), .Width = 120, .Height = 42, .Text = "<< Cau truoc"}
        AddHandler btnPrev.Click, AddressOf BtnPrev_Click
        Me.Controls.Add(btnPrev)

        btnRecord = New Button() With {.Location = New Point(155, 406), .Width = 210, .Height = 42, .Text = "Ghi am (Space)"}
        AddHandler btnRecord.Click, AddressOf BtnRecord_Click
        Me.Controls.Add(btnRecord)

        btnPlay = New Button() With {.Location = New Point(380, 406), .Width = 150, .Height = 42, .Text = "Nghe lai (P)"}
        AddHandler btnPlay.Click, AddressOf BtnPlay_Click
        Me.Controls.Add(btnPlay)

        btnNext = New Button() With {.Location = New Point(545, 406), .Width = 195, .Height = 42, .Text = "Cau tiep theo (Enter) >>"}
        AddHandler btnNext.Click, AddressOf BtnNext_Click
        Me.Controls.Add(btnNext)

        chkAutoNext = New CheckBox() With {
            .Location = New Point(20, 456), .Width = 300, .Height = 24,
            .Text = "Tu dong sang cau tiep sau khi luu (auto-next)"
        }
        Me.Controls.Add(chkAutoNext)

        btnExportZip = New Button() With {
            .Location = New Point(545, 452), .Width = 195, .Height = 30, .Text = "Xuat dataset (.zip)"
        }
        AddHandler btnExportZip.Click, AddressOf BtnExportZip_Click
        Me.Controls.Add(btnExportZip)

        progressOverall = New ProgressBar() With {
            .Location = New Point(20, 490), .Width = 720, .Height = 22
        }
        Me.Controls.Add(progressOverall)

        Dim lblHint As New Label() With {
            .Location = New Point(20, 518), .Width = 720, .Height = 60,
            .ForeColor = Color.Gray,
            .Text = "Phim tat: Space = Bat dau/Dung ghi am (bam lai Space trong luc dem nguoc de huy) | R = Thu lai | P = Nghe lai | Enter hoac mui ten phai = Cau tiep theo | Mui ten trai = Cau truoc | Go so vao o 'Di toi cau' roi Enter de nhay nhanh"
        }
        Me.Controls.Add(lblHint)

        AddHandler Me.KeyDown, AddressOf MainForm_KeyDown

        meterTimer = New Timer() With {.Interval = 50}
        AddHandler meterTimer.Tick, AddressOf MeterTimer_Tick
        meterTimer.Start()

        countdownTimer = New Timer() With {.Interval = 1000}
        AddHandler countdownTimer.Tick, AddressOf CountdownTimer_Tick

        autoNextTimer = New Timer() With {.Interval = 800}
        AddHandler autoNextTimer.Tick, AddressOf AutoNextTimer_Tick
    End Sub

    ' ===================== Load du lieu =====================

    ' Neu thu muc scripts chua co bo cau nao: thu chuyen script.txt cu (ban truoc) vao do.
    ' Ngoai ra, neu phat hien du lieu da thu cua ban cu (chi co 1 bo cau, luu thang trong
    ' dataset\wavs) thi tu dong chuyen sang dataset\01_co_ban\ de khong mat tien do da lam.
    Private Sub EnsureDefaultScripts()
        Dim existingScripts = Directory.GetFiles(scriptsFolder, "*.txt")
        If existingScripts.Length = 0 Then
            Dim legacyScriptPath = Path.Combine(Application.StartupPath, "script.txt")
            If File.Exists(legacyScriptPath) Then
                Try
                    File.Copy(legacyScriptPath, Path.Combine(scriptsFolder, "01_co_ban.txt"))
                Catch
                    ' bo qua neu khong copy duoc, PopulateScriptList se bao khong co bo cau
                End Try
            End If
        End If

        Dim legacyWavsFolder = Path.Combine(outputRoot, "wavs")
        Dim migratedTarget = Path.Combine(outputRoot, "01_co_ban")
        If Directory.Exists(legacyWavsFolder) AndAlso Not Directory.Exists(migratedTarget) Then
            Try
                Directory.CreateDirectory(migratedTarget)
                Directory.Move(legacyWavsFolder, Path.Combine(migratedTarget, "wavs"))

                Dim legacyMeta = Path.Combine(outputRoot, "metadata.csv")
                If File.Exists(legacyMeta) Then File.Move(legacyMeta, Path.Combine(migratedTarget, "metadata.csv"))

                Dim legacyPos = Path.Combine(outputRoot, "last_position.txt")
                If File.Exists(legacyPos) Then File.Move(legacyPos, Path.Combine(migratedTarget, "last_position.txt"))
            Catch
                ' neu di chuyen loi thi bo qua, khong lam crash app; du lieu cu van con nguyen o dataset\wavs
            End Try
        End If
    End Sub

    ' Do danh sach tat ca bo cau (.txt) co trong thu muc scripts vao combo box
    Private Sub PopulateScriptList()
        cmbScript.Items.Clear()

        Dim files = Directory.GetFiles(scriptsFolder, "*.txt")
        Array.Sort(files)
        For Each f In files
            cmbScript.Items.Add(Path.GetFileNameWithoutExtension(f))
        Next

        If cmbScript.Items.Count > 0 Then
            isLoadingScript = True
            cmbScript.SelectedIndex = 0
            isLoadingScript = False
            LoadSelectedScript()
        End If
    End Sub

    Private Sub CmbScript_SelectedIndexChanged(sender As Object, e As EventArgs)
        If isLoadingScript Then Return
        If isCountingDown Then CancelCountdown()
        If isRecording Then StopRecording()
        LoadSelectedScript()
        ShowSentence()
    End Sub

    ' Nap bo cau dang duoc chon trong combo box: doc file cau, tro toi thu muc
    ' dataset\<ten_bo_cau>\ rieng cho bo do, roi nap lai tien do/vi tri da lam dang do.
    Private Sub LoadSelectedScript()
        If cmbScript.SelectedItem Is Nothing Then Return

        currentScriptName = cmbScript.SelectedItem.ToString()
        Dim scriptFilePath = Path.Combine(scriptsFolder, currentScriptName & ".txt")

        sentences.Clear()
        recordedIndices.Clear()
        currentIndex = 0

        LoadScriptFile(scriptFilePath)

        Dim scriptOutputRoot = Path.Combine(outputRoot, currentScriptName)
        wavsFolder = Path.Combine(scriptOutputRoot, "wavs")
        metadataPath = Path.Combine(scriptOutputRoot, "metadata.csv")
        lastPositionPath = Path.Combine(scriptOutputRoot, "last_position.txt")
        Directory.CreateDirectory(wavsFolder)

        LoadExistingProgress()
        LoadLastPosition()

        Me.Text = $"Voice Recorder - Thu am dataset TTS  [{currentScriptName}]"
    End Sub

    Private Sub LoadScriptFile(scriptFilePath As String)
        If Not File.Exists(scriptFilePath) Then
            MessageBox.Show($"Khong tim thay file: {Path.GetFileName(scriptFilePath)}",
                             "Loi", MessageBoxButtons.OK, MessageBoxIcon.Error)
            sentences.Add("Khong co cau mau nao duoc tai.")
            Return
        End If

        For Each line In File.ReadAllLines(scriptFilePath)
            Dim trimmed = line.Trim()
            If trimmed.Length > 0 Then
                sentences.Add(trimmed)
            End If
        Next
    End Sub

    Private Sub LoadExistingProgress()
        If File.Exists(metadataPath) Then
            For Each line In File.ReadAllLines(metadataPath)
                Dim parts = line.Split("|"c)
                If parts.Length >= 1 Then
                    Dim idx As Integer
                    If Integer.TryParse(parts(0).Trim(), idx) Then
                        recordedIndices.Add(idx - 1) ' file dat ten tu 1, index mang tu 0
                    End If
                End If
            Next
        End If

        ' nhay toi cau dau tien chua thu de tiep tuc cong viec
        For i = 0 To sentences.Count - 1
            If Not recordedIndices.Contains(i) Then
                currentIndex = i
                Exit For
            End If
        Next
    End Sub

    ' Nap lai chinh xac cau dang lam do tu lan truoc (neu co), de tiep tuc dung cho
    ' du ban da luot qua vai cau chua thu roi moi tat app, khong chi nhay ve cau
    ' dau tien chua thu nhu LoadExistingProgress.
    Private Sub LoadLastPosition()
        If Not File.Exists(lastPositionPath) Then Return

        Try
            Dim content = File.ReadAllText(lastPositionPath).Trim()
            Dim savedIndex As Integer
            If Integer.TryParse(content, savedIndex) Then
                If savedIndex >= 0 AndAlso savedIndex < sentences.Count Then
                    currentIndex = savedIndex
                End If
            End If
        Catch
            ' file loi/hong thi bo qua, giu nguyen vi tri tinh boi LoadExistingProgress
        End Try
    End Sub

    Private Sub SaveLastPosition()
        Try
            File.WriteAllText(lastPositionPath, currentIndex.ToString())
        Catch
            ' khong the luu thi thoi, khong lam gian doan viec ghi am
        End Try
    End Sub

    Private Sub PopulateDevices()
        cmbDevice.Items.Clear()
        For i = 0 To WaveInEvent.DeviceCount - 1
            Dim cap = WaveInEvent.GetCapabilities(i)
            cmbDevice.Items.Add(cap.ProductName)
        Next
        If cmbDevice.Items.Count > 0 Then cmbDevice.SelectedIndex = 0
    End Sub

    Private Sub ShowSentence()
        If sentences.Count = 0 Then Return
        lblProgress.Text = $"Cau {currentIndex + 1} / {sentences.Count}    (Da thu: {recordedIndices.Count})"
        lblSentence.Text = sentences(currentIndex)
        progressOverall.Maximum = Math.Max(sentences.Count, 1)
        progressOverall.Value = recordedIndices.Count

        Dim hasRecording = recordedIndices.Contains(currentIndex)
        btnPlay.Enabled = hasRecording
        lblSentence.BackColor = If(hasRecording, Color.FromArgb(220, 255, 220), Color.White)

        ' Khong dong vao waveform panel neu dang ghi/dem nguoc (de khong lam gian doan hien thi live)
        If Not isRecording AndAlso Not isCountingDown Then
            If hasRecording Then
                LoadWaveformPreview(GetFileId(currentIndex))
            Else
                waveformPanel.Clear()
            End If
        End If

        SaveLastPosition()
        UpdateOverallProgressLabel()
    End Sub

    ' Tinh tong so cau va tong so cau da thu tren TAT CA cac kich ban (goi khi can hien thi
    ' tong tien do toan bo dataset, khong chi rieng kich ban dang chon).
    Private Function ComputeOverallProgress() As (total As Integer, recorded As Integer)
        Dim total As Integer = 0
        Dim recorded As Integer = 0

        Try
            For Each scriptFile In Directory.GetFiles(scriptsFolder, "*.txt")
                Dim name = Path.GetFileNameWithoutExtension(scriptFile)
                Dim lineCount = File.ReadAllLines(scriptFile).Count(Function(l) l.Trim().Length > 0)
                total += lineCount

                Dim metaPath = Path.Combine(outputRoot, name, "metadata.csv")
                If File.Exists(metaPath) Then
                    recorded += File.ReadAllLines(metaPath).Count(Function(l) l.Trim().Length > 0)
                End If
            Next
        Catch
            ' neu doc file loi giua chung thi tra ve gia tri da tinh duoc, khong lam crash app
        End Try

        Return (total, recorded)
    End Function

    ' Quet toan bo file .wav da luu (tat ca kich ban) de tinh tong thoi luong audio.
    ' Dung truc tiep kich thuoc file (PCM 16-bit, header 44 byte chuan) de tinh nhanh,
    ' khong can mo tung file bang thu vien audio.
    Private Function ComputeTotalDurationSeconds() As Double
        Dim total As Double = 0
        Try
            For Each scriptFile In Directory.GetFiles(scriptsFolder, "*.txt")
                Dim name = Path.GetFileNameWithoutExtension(scriptFile)
                Dim wFolder = Path.Combine(outputRoot, name, "wavs")
                If Directory.Exists(wFolder) Then
                    For Each wavFile In Directory.GetFiles(wFolder, "*.wav")
                        Try
                            Dim info As New FileInfo(wavFile)
                            Dim dataBytes = Math.Max(0L, info.Length - 44L)
                            total += dataBytes / (TARGET_SAMPLE_RATE * 2.0)
                        Catch
                            ' bo qua file loi, khong lam sai lech qua nhieu tong so
                        End Try
                    Next
                End If
            Next
        Catch
            ' neu doc thu muc loi thi tra ve gia tri da cong duoc
        End Try
        Return total
    End Function

    Private Sub UpdateOverallProgressLabel()
        Dim result = ComputeOverallProgress()
        Dim pct As Integer = If(result.total > 0, CInt(Math.Round(result.recorded / result.total * 100)), 0)
        lblOverallProgress.Text = $"Tong dataset: {result.recorded} / {result.total} cau ({pct}%)"

        Dim totalSeconds = ComputeTotalDurationSeconds()
        Dim ts = TimeSpan.FromSeconds(totalSeconds)
        lblTotalDuration.Text = $"Tong thoi luong da thu: {CInt(ts.TotalMinutes)} phut {ts.Seconds} giay"
    End Sub

    ' ===================== Nhay nhanh toi cau =====================

    Private Sub BtnJumpTo_Click(sender As Object, e As EventArgs)
        JumpToSentence()
    End Sub

    Private Sub JumpToSentence()
        If sentences.Count = 0 Then Return

        Dim num As Integer
        If Not Integer.TryParse(txtJumpTo.Text.Trim(), num) Then
            MessageBox.Show("Vui long nhap mot so hop le.", "Sai dinh dang", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        If num < 1 OrElse num > sentences.Count Then
            MessageBox.Show($"Vui long nhap so tu 1 den {sentences.Count}.", "So cau khong hop le", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        If isCountingDown Then CancelCountdown()
        If isRecording Then StopRecording()
        currentIndex = num - 1
        ShowSentence()
    End Sub

    Private Function GetFileId(index As Integer) As String
        Return (index + 1).ToString("D4")
    End Function

    ' ===================== Ghi am =====================

    Private Sub BtnRecord_Click(sender As Object, e As EventArgs)
        ToggleRecording()
    End Sub

    Private Sub ToggleRecording()
        If isCountingDown Then
            CancelCountdown()
        ElseIf isRecording Then
            StopRecording()
        Else
            StartRecording()
        End If
    End Sub

    ' Bat dau: mo mic va bat WaveIn ngay (de do duoc on nen trong luc dem nguoc), nhung
    ' CHI ghi vao file tam sau khi dem nguoc ket thuc (xem WaveIn_DataAvailable).
    Private Sub StartRecording()
        If sentences.Count = 0 Then Return
        If cmbDevice.SelectedIndex < 0 Then
            MessageBox.Show("Khong tim thay thiet bi microphone.", "Loi")
            Return
        End If

        tempFilePath = Path.Combine(Path.GetTempPath(), "voicerec_temp_" & Guid.NewGuid().ToString("N") & ".wav")

        waveIn = New WaveInEvent() With {
            .DeviceNumber = cmbDevice.SelectedIndex,
            .WaveFormat = New WaveFormat(44100, 16, 1)
        }
        AddHandler waveIn.DataAvailable, AddressOf WaveIn_DataAvailable
        AddHandler waveIn.RecordingStopped, AddressOf WaveIn_RecordingStopped

        tempWriter = New WaveFileWriter(tempFilePath, waveIn.WaveFormat)

        ' reset thong ke chat luong cho lan ghi nay
        recSampleCount = 0
        recClippedCount = 0
        recPeakLevel = 0
        recSampleRate = waveIn.WaveFormat.SampleRate
        ambientPeak = 0
        cancelPending = False

        liveWaveformPeaks.Clear()
        waveformPanel.Clear()

        isCountingDown = True
        countdownRemaining = PRE_RECORD_COUNTDOWN_SECONDS
        btnRecord.Text = $"Chuan bi... {countdownRemaining}"
        lblStatus.Text = "Chuan bi doc cau, giu yen lang..."
        lblStatus.ForeColor = Color.DarkOrange

        waveIn.StartRecording()
        countdownTimer.Start()
    End Sub

    Private Sub CountdownTimer_Tick(sender As Object, e As EventArgs)
        countdownRemaining -= 1

        If countdownRemaining <= 0 Then
            countdownTimer.Stop()
            isCountingDown = False
            isRecording = True
            liveWaveformPeaks.Clear()

            btnRecord.Text = "Dung ghi am (Space)"
            lblStatus.Text = "Dang ghi am..."
            lblStatus.ForeColor = Color.Red

            If ambientPeak > AMBIENT_NOISE_WARN_THRESHOLD Then
                lblStatus.Text &= "  (canh bao: moi truong hoi on, can nhac chuyen cho yen tinh hon)"
            End If
        Else
            btnRecord.Text = $"Chuan bi... {countdownRemaining}"
        End If
    End Sub

    ' Huy trong luc dang dem nguoc (chua ghi that su vao file nao ca)
    Private Sub CancelCountdown()
        countdownTimer.Stop()
        isCountingDown = False
        cancelPending = True
        waveIn?.StopRecording()

        btnRecord.Text = "Ghi am (Space)"
        lblStatus.Text = "Da huy dem nguoc."
        lblStatus.ForeColor = Color.Gray
        waveformPanel.Clear()
    End Sub

    Private Sub StopRecording()
        waveIn?.StopRecording()
        isRecording = False
        btnRecord.Text = "Ghi am (Space)"
    End Sub

    Private Sub WaveIn_DataAvailable(sender As Object, e As WaveInEventArgs)
        Dim maxSample As Single = 0

        For i = 0 To e.BytesRecorded - 2 Step 2
            Dim sampleVal = BitConverter.ToInt16(e.Buffer, i)
            Dim absRaw = Math.Abs(CInt(sampleVal))
            Dim abs = absRaw / 32768.0F
            If abs > maxSample Then maxSample = abs

            If isCountingDown Then
                ' trong luc dem nguoc: chi do muc on nen, khong ghi vao file va khong tinh chat luong
                If abs > ambientPeak Then ambientPeak = abs
            Else
                If abs > recPeakLevel Then recPeakLevel = abs
                If absRaw >= CLIP_SAMPLE_THRESHOLD Then recClippedCount += 1
                recSampleCount += 1
            End If
        Next

        currentVolume = maxSample

        If Not isCountingDown Then
            tempWriter?.Write(e.Buffer, 0, e.BytesRecorded)
            AppendLiveWaveformPeak(maxSample)
        End If
    End Sub

    Private Sub AppendLiveWaveformPeak(peak As Single)
        liveWaveformPeaks.Add(peak)
        If liveWaveformPeaks.Count > LIVE_WAVEFORM_CAPACITY Then
            liveWaveformPeaks.RemoveAt(0)
        End If
    End Sub

    Private Sub WaveIn_RecordingStopped(sender As Object, e As StoppedEventArgs)
        tempWriter?.Dispose()
        tempWriter = Nothing
        waveIn?.Dispose()
        waveIn = Nothing
        currentVolume = 0
        isRecording = False
        btnRecord.Text = "Ghi am (Space)"

        If cancelPending Then
            cancelPending = False
            Try
                If File.Exists(tempFilePath) Then File.Delete(tempFilePath)
            Catch
                ' bo qua loi xoa file tam
            End Try
            Return
        End If

        Dim errorMsg = CheckRecordingQuality()
        If errorMsg IsNot Nothing Then
            RejectRecording(errorMsg)
        Else
            SaveFinalWav()
        End If
    End Sub

    ' Kiem tra chat luong ban ghi vua thu. Tra ve Nothing neu OK, hoac chuoi mo ta loi neu khong dat.
    Private Function CheckRecordingQuality() As String
        If recSampleCount = 0 Then
            Return "Khong ghi duoc am thanh nao. Vui long kiem tra microphone va ghi lai."
        End If

        Dim durationSeconds = recSampleCount / CDbl(recSampleRate)
        If durationSeconds < MIN_DURATION_SECONDS Then
            Return $"Ghi am qua ngan ({durationSeconds:0.00}s). Vui long doc du cau va ghi lai."
        End If

        If recPeakLevel < MIN_PEAK_LEVEL Then
            Return "Khong phat hien am thanh ro rang (qua nho hoac im lang). Kiem tra lai microphone va ghi lai."
        End If

        Dim clipRatio = recClippedCount / CDbl(recSampleCount)
        If clipRatio > MAX_CLIP_RATIO Then
            Return $"Am thanh bi vo tieng / clipping ({clipRatio:P1} so mau). Noi nho hon hoac lui mic ra xa roi ghi lai."
        End If

        Return Nothing
    End Function

    ' Huy ban ghi loi: xoa file tam, KHONG dong vao file/metadata da luu truoc do (neu co)
    Private Sub RejectRecording(reason As String)
        Try
            If File.Exists(tempFilePath) Then File.Delete(tempFilePath)
        Catch
            ' bo qua loi xoa file tam
        End Try

        lblStatus.Text = "Loi: " & reason
        lblStatus.ForeColor = Color.Red
        MessageBox.Show(reason, "Ban ghi khong dat yeu cau", MessageBoxButtons.OK, MessageBoxIcon.Warning)
        ShowSentence()
    End Sub

    ' Xu ly ban ghi vua thu: doc mau tho -> cat khoang lang dau/cuoi -> chuan hoa am luong (peak
    ' normalize, co gioi han khuech dai) -> resample ve chuan Piper -> luu file .wav cuoi cung.
    Private Sub SaveFinalWav()
        Dim intermediatePath As String = Nothing
        Try
            Dim rawSampleRate As Integer = 0
            Dim rawSamples = ReadAllFloatSamples(tempFilePath, rawSampleRate)

            Dim trimmed = TrimSilence(rawSamples, rawSampleRate, TRIM_SILENCE_THRESHOLD, TRIM_PADDING_MS)
            Dim processed = NormalizeSamples(trimmed, TARGET_PEAK, MAX_NORMALIZE_GAIN)
            If processed.Length = 0 Then processed = rawSamples

            intermediatePath = Path.Combine(Path.GetTempPath(), "voicerec_proc_" & Guid.NewGuid().ToString("N") & ".wav")
            WriteFloatSamplesAsPcm16Wav(intermediatePath, processed, rawSampleRate)

            Dim finalPath = Path.Combine(wavsFolder, GetFileId(currentIndex) & ".wav")
            Using reader As New AudioFileReader(intermediatePath)
                Dim targetFormat = New WaveFormat(TARGET_SAMPLE_RATE, TARGET_BITS, TARGET_CHANNELS)
                Using resampler As New MediaFoundationResampler(reader, targetFormat)
                    resampler.ResamplerQuality = 60
                    WaveFileWriter.CreateWaveFile(finalPath, resampler)
                End Using
            End Using

            File.Delete(tempFilePath)
            File.Delete(intermediatePath)

            recordedIndices.Add(currentIndex)
            UpdateMetadata(currentIndex, sentences(currentIndex))

            Dim clipDuration = If(rawSampleRate > 0, processed.Length / CDbl(rawSampleRate), 0)
            lblStatus.Text = $"Da luu: {GetFileId(currentIndex)}.wav  ({clipDuration:0.0}s, da cat khoang lang + chuan hoa am luong)"
            lblStatus.ForeColor = Color.Green

            ShowSentence() ' se tu nap lai waveform preview tu file vua luu

            If chkAutoNext.Checked AndAlso currentIndex < sentences.Count - 1 Then
                autoNextTimer.Start()
            End If
        Catch ex As Exception
            Try
                If intermediatePath IsNot Nothing AndAlso File.Exists(intermediatePath) Then File.Delete(intermediatePath)
            Catch
                ' bo qua loi don dep file tam
            End Try
            MessageBox.Show("Loi khi luu file audio: " & ex.Message, "Loi", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    Private Sub AutoNextTimer_Tick(sender As Object, e As EventArgs)
        autoNextTimer.Stop()
        If currentIndex < sentences.Count - 1 Then
            currentIndex += 1
            ShowSentence()
        End If
    End Sub

    ' Doc toan bo mau am thanh cua 1 file .wav PCM 16-bit thanh mang Single (-1..1).
    Private Function ReadAllFloatSamples(path As String, ByRef sampleRate As Integer) As Single()
        Using reader As New WaveFileReader(path)
            sampleRate = reader.WaveFormat.SampleRate

            Dim totalBytes = CInt(reader.Length)
            If totalBytes <= 0 Then Return Array.Empty(Of Single)()

            Dim buffer(totalBytes - 1) As Byte
            Dim totalRead = 0
            While totalRead < totalBytes
                Dim n = reader.Read(buffer, totalRead, totalBytes - totalRead)
                If n <= 0 Then Exit While ' het du lieu de doc
                totalRead += n
            End While
            Dim sampleCount = totalRead \ 2 ' 16-bit = 2 byte/mau

            If sampleCount <= 0 Then Return Array.Empty(Of Single)()

            Dim result(sampleCount - 1) As Single
            For i = 0 To sampleCount - 1
                Dim s = BitConverter.ToInt16(buffer, i * 2)
                result(i) = s / 32768.0F
            Next
            Return result
        End Using
    End Function

    ' Cat bo phan dau/cuoi co bien do duoi nguong (khoang lang), giu lai it dem 2 ben.
    Private Function TrimSilence(samples As Single(), sampleRate As Integer, thresholdRatio As Single, paddingMs As Integer) As Single()
        If samples.Length = 0 Then Return samples

        Dim startIdx = 0
        Dim endIdx = samples.Length - 1

        While startIdx < samples.Length AndAlso Math.Abs(samples(startIdx)) < thresholdRatio
            startIdx += 1
        End While
        While endIdx > startIdx AndAlso Math.Abs(samples(endIdx)) < thresholdRatio
            endIdx -= 1
        End While

        ' toan bo duoi nguong (khong nen xay ra vi da qua kiem tra chat luong) -> giu nguyen
        If startIdx >= endIdx Then Return samples

        Dim paddingSamples = CInt(sampleRate * paddingMs / 1000.0)
        startIdx = Math.Max(0, startIdx - paddingSamples)
        endIdx = Math.Min(samples.Length - 1, endIdx + paddingSamples)

        Dim length = endIdx - startIdx + 1
        Dim result(length - 1) As Single
        Array.Copy(samples, startIdx, result, 0, length)
        Return result
    End Function

    ' Chuan hoa am luong theo dinh (peak normalize): dua dinh bien do ve targetPeak,
    ' nhung gioi han he so khuech dai toi da de tranh khuech dai qua muc on nen/tieng ri.
    Private Function NormalizeSamples(samples As Single(), targetPeak As Single, maxGain As Single) As Single()
        If samples.Length = 0 Then Return samples

        Dim peak As Single = 0
        For Each s In samples
            Dim a = Math.Abs(s)
            If a > peak Then peak = a
        Next

        If peak < 0.0001F Then Return samples ' tranh chia cho so gan 0

        Dim gain = targetPeak / peak
        gain = Math.Min(gain, maxGain)
        If gain <= 1.0F Then Return samples ' khong can khuech dai neu da dat/vuot muc tieu

        Dim result(samples.Length - 1) As Single
        For i = 0 To samples.Length - 1
            Dim v = samples(i) * gain
            If v > 1.0F Then v = 1.0F
            If v < -1.0F Then v = -1.0F
            result(i) = v
        Next
        Return result
    End Function

    ' Ghi mang mau Single (-1..1) thanh file .wav PCM 16-bit mono chuan.
    Private Sub WriteFloatSamplesAsPcm16Wav(path As String, samples As Single(), sampleRate As Integer)
        Dim writeFormat = New WaveFormat(sampleRate, 16, 1)
        Using writer As New WaveFileWriter(path, writeFormat)
            If samples.Length = 0 Then Return

            Dim byteBuffer(samples.Length * 2 - 1) As Byte
            For i = 0 To samples.Length - 1
                Dim v = samples(i)
                If v > 1.0F Then v = 1.0F
                If v < -1.0F Then v = -1.0F
                Dim s As Int16 = CType(v * 32767.0F, Int16)
                Dim b = BitConverter.GetBytes(s)
                byteBuffer(i * 2) = b(0)
                byteBuffer(i * 2 + 1) = b(1)
            Next
            writer.Write(byteBuffer, 0, byteBuffer.Length)
        End Using
    End Sub

    ' file metadata.csv dinh dang Piper can: id|text (id = ten file khong co duoi .wav)
    Private Sub UpdateMetadata(index As Integer, text As String)
        Dim id = GetFileId(index)
        Dim lines As New List(Of String)

        If File.Exists(metadataPath) Then
            lines.AddRange(File.ReadAllLines(metadataPath))
        End If

        Dim newLine = $"{id}|{text}"
        Dim found = False
        For i = 0 To lines.Count - 1
            If lines(i).StartsWith(id & "|") Then
                lines(i) = newLine
                found = True
                Exit For
            End If
        Next
        If Not found Then lines.Add(newLine)

        lines.Sort()
        File.WriteAllLines(metadataPath, lines)
    End Sub

    ' ===================== Phat lai =====================

    Private Sub BtnPlay_Click(sender As Object, e As EventArgs)
        PlayCurrent()
    End Sub

    Private Sub PlayCurrent()
        Dim filePath As String = Path.Combine(wavsFolder, GetFileId(currentIndex) & ".wav")
        If Not File.Exists(filePath) Then Return

        Try
            Dim reader As New AudioFileReader(filePath)
            Dim player As New WaveOutEvent()
            player.Init(reader)
            AddHandler player.PlaybackStopped, Sub(s, ev)
                                                    reader.Dispose()
                                                    player.Dispose()
                                                End Sub
            player.Play()
        Catch ex As Exception
            MessageBox.Show("Loi phat lai: " & ex.Message)
        End Try
    End Sub

    ' Nap waveform cua file da luu (sau xu ly) de hien preview khi dang xem lai cau da thu.
    Private Sub LoadWaveformPreview(fileId As String)
        Try
            Dim filePath As String = Path.Combine(wavsFolder, fileId & ".wav")
            If Not File.Exists(filePath) Then
                waveformPanel.Clear()
                Return
            End If

            Dim sr As Integer = 0
            Dim samples = ReadAllFloatSamples(filePath, sr)
            Dim buckets = BuildWaveformBuckets(samples, Math.Max(waveformPanel.ClientSize.Width, 1))
            waveformPanel.SetSamples(buckets, Color.SeaGreen)
        Catch
            waveformPanel.Clear()
        End Try
    End Sub

    ' Gom mang mau thanh 'bucketCount' cot, moi cot lay bien do dinh (peak) trong doan tuong ung -
    ' dung de ve preview waveform vua/dep voi be rong panel co dinh.
    Private Function BuildWaveformBuckets(samples As Single(), bucketCount As Integer) As Single()
        If samples Is Nothing OrElse samples.Length = 0 OrElse bucketCount <= 0 Then
            Return Array.Empty(Of Single)()
        End If

        Dim result(bucketCount - 1) As Single
        Dim samplesPerBucket = Math.Max(1, CInt(samples.Length / CDbl(bucketCount)))

        For b = 0 To bucketCount - 1
            Dim startIdx = b * samplesPerBucket
            Dim endIdx = Math.Min(samples.Length, startIdx + samplesPerBucket)
            Dim peak As Single = 0
            For i = startIdx To endIdx - 1
                Dim a = Math.Abs(samples(i))
                If a > peak Then peak = a
            Next
            result(b) = peak
        Next

        Return result
    End Function

    ' ===================== Xuat dataset =====================

    Private Sub BtnExportZip_Click(sender As Object, e As EventArgs)
        Using sfd As New SaveFileDialog() With {
            .Filter = "Zip files (*.zip)|*.zip",
            .FileName = $"dataset_{DateTime.Now:yyyyMMdd_HHmm}.zip",
            .Title = "Luu dataset thanh file zip"
        }
            If sfd.ShowDialog() <> DialogResult.OK Then Return

            Dim zipPath = sfd.FileName
            btnExportZip.Enabled = False
            lblStatus.Text = "Dang nen dataset..."
            lblStatus.ForeColor = Color.Blue

            Task.Run(Sub()
                         Try
                             If File.Exists(zipPath) Then File.Delete(zipPath)
                             ZipFile.CreateFromDirectory(outputRoot, zipPath, CompressionLevel.Optimal, False)

                             Me.Invoke(Sub()
                                           lblStatus.Text = "Da xuat dataset: " & Path.GetFileName(zipPath)
                                           lblStatus.ForeColor = Color.Green
                                           btnExportZip.Enabled = True
                                       End Sub)
                         Catch ex As Exception
                             Me.Invoke(Sub()
                                           lblStatus.Text = "Loi khi nen dataset."
                                           lblStatus.ForeColor = Color.Red
                                           btnExportZip.Enabled = True
                                           MessageBox.Show("Loi khi nen dataset: " & ex.Message, "Loi", MessageBoxButtons.OK, MessageBoxIcon.Error)
                                       End Sub)
                         End Try
                     End Sub)
        End Using
    End Sub

    ' ===================== Dieu huong =====================

    Private Sub BtnPrev_Click(sender As Object, e As EventArgs)
        If isCountingDown Then CancelCountdown()
        If isRecording Then StopRecording()
        If currentIndex > 0 Then
            currentIndex -= 1
            ShowSentence()
        End If
    End Sub

    Private Sub BtnNext_Click(sender As Object, e As EventArgs)
        If isCountingDown Then CancelCountdown()
        If isRecording Then StopRecording()
        If currentIndex < sentences.Count - 1 Then
            currentIndex += 1
            ShowSentence()
        Else
            MessageBox.Show("Ban da o cau cuoi cung trong danh sach.")
        End If
    End Sub

    Private Sub MeterTimer_Tick(sender As Object, e As EventArgs)
        Dim val = CInt(currentVolume * 100)
        val = Math.Max(0, Math.Min(100, val))
        meterVolume.Value = val

        If isRecording Then
            waveformPanel.SetSamples(liveWaveformPeaks.ToArray(), Color.Crimson)
        End If
    End Sub

    Private Sub MainForm_KeyDown(sender As Object, e As KeyEventArgs)
        ' Khi dang go so trong o "Di toi cau", chi bat phim Enter de nhay cau; cac phim
        ' khac (so, mui ten trai/phai, xoa...) de textbox tu xu ly binh thuong.
        If txtJumpTo.Focused Then
            If e.KeyCode = Keys.Enter Then
                JumpToSentence()
                e.Handled = True
                e.SuppressKeyPress = True
            End If
            Return
        End If

        Select Case e.KeyCode
            Case Keys.Space
                ToggleRecording()
                e.Handled = True
            Case Keys.R
                If Not isRecording AndAlso Not isCountingDown Then StartRecording()
                e.Handled = True
            Case Keys.P
                PlayCurrent()
                e.Handled = True
            Case Keys.Enter
                BtnNext_Click(Nothing, Nothing)
                e.Handled = True
            Case Keys.Left
                BtnPrev_Click(Nothing, Nothing)
                e.Handled = True
            Case Keys.Right
                BtnNext_Click(Nothing, Nothing)
                e.Handled = True
        End Select
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        If isCountingDown Then CancelCountdown()
        If isRecording Then StopRecording()
        SaveLastPosition()
        meterTimer?.Stop()
        countdownTimer?.Stop()
        autoNextTimer?.Stop()
        MyBase.OnFormClosing(e)
    End Sub

End Class
