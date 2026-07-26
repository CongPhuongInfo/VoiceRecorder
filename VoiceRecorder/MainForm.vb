Imports System.IO
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

    ' ===== NAudio =====
    Private waveIn As WaveInEvent
    Private tempWriter As WaveFileWriter
    Private tempFilePath As String
    Private isRecording As Boolean = False
    Private currentVolume As Single = 0

    ' thong ke chat luong cho lan ghi hien tai
    Private recSampleCount As Long = 0
    Private recClippedCount As Long = 0
    Private recPeakLevel As Single = 0
    Private recSampleRate As Integer = 44100

    ' ===== UI controls =====
    Private lblProgress As Label
    Private lblSentence As Label
    Private lblStatus As Label
    Private cmbDevice As ComboBox
    Private lblScript As Label
    Private cmbScript As ComboBox
    Private progressOverall As ProgressBar
    Private meterVolume As ProgressBar
    Private btnRecord As Button
    Private btnPlay As Button
    Private btnPrev As Button
    Private btnNext As Button
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
        Me.ClientSize = New Size(780, 490)
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

        meterVolume = New ProgressBar() With {
            .Location = New Point(20, 235), .Width = 720, .Height = 18, .Maximum = 100
        }
        Me.Controls.Add(meterVolume)

        lblStatus = New Label() With {
            .Location = New Point(20, 262), .Width = 720, .Text = "San sang."
        }
        Me.Controls.Add(lblStatus)

        btnPrev = New Button() With {.Location = New Point(20, 295), .Width = 120, .Height = 42, .Text = "<< Cau truoc"}
        AddHandler btnPrev.Click, AddressOf BtnPrev_Click
        Me.Controls.Add(btnPrev)

        btnRecord = New Button() With {.Location = New Point(155, 295), .Width = 210, .Height = 42, .Text = "Ghi am (Space)"}
        AddHandler btnRecord.Click, AddressOf BtnRecord_Click
        Me.Controls.Add(btnRecord)

        btnPlay = New Button() With {.Location = New Point(380, 295), .Width = 150, .Height = 42, .Text = "Nghe lai (P)"}
        AddHandler btnPlay.Click, AddressOf BtnPlay_Click
        Me.Controls.Add(btnPlay)

        btnNext = New Button() With {.Location = New Point(545, 295), .Width = 195, .Height = 42, .Text = "Cau tiep theo (Enter) >>"}
        AddHandler btnNext.Click, AddressOf BtnNext_Click
        Me.Controls.Add(btnNext)

        progressOverall = New ProgressBar() With {
            .Location = New Point(20, 355), .Width = 720, .Height = 25
        }
        Me.Controls.Add(progressOverall)

        Dim lblHint As New Label() With {
            .Location = New Point(20, 390), .Width = 720, .Height = 60,
            .ForeColor = Color.Gray,
            .Text = "Phim tat: Space = Bat dau/Dung ghi am | R = Thu lai | P = Nghe lai | Enter hoac mui ten phai = Cau tiep theo | Mui ten trai = Cau truoc"
        }
        Me.Controls.Add(lblHint)

        AddHandler Me.KeyDown, AddressOf MainForm_KeyDown

        meterTimer = New Timer() With {.Interval = 50}
        AddHandler meterTimer.Tick, AddressOf MeterTimer_Tick
        meterTimer.Start()
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

    Private Sub LoadScriptFile(path As String)
        If Not File.Exists(path) Then
            MessageBox.Show($"Khong tim thay file: {Path.GetFileName(path)}",
                             "Loi", MessageBoxButtons.OK, MessageBoxIcon.Error)
            sentences.Add("Khong co cau mau nao duoc tai.")
            Return
        End If

        For Each line In File.ReadAllLines(path)
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

        SaveLastPosition()
    End Sub

    Private Function GetFileId(index As Integer) As String
        Return (index + 1).ToString("D4")
    End Function

    ' ===================== Ghi am =====================

    Private Sub BtnRecord_Click(sender As Object, e As EventArgs)
        ToggleRecording()
    End Sub

    Private Sub ToggleRecording()
        If isRecording Then
            StopRecording()
        Else
            StartRecording()
        End If
    End Sub

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

        waveIn.StartRecording()
        isRecording = True
        btnRecord.Text = "Dung ghi am (Space)"
        lblStatus.Text = "Dang ghi am..."
        lblStatus.ForeColor = Color.Red
    End Sub

    Private Sub StopRecording()
        waveIn?.StopRecording()
        isRecording = False
        btnRecord.Text = "Ghi am (Space)"
    End Sub

    Private Sub WaveIn_DataAvailable(sender As Object, e As WaveInEventArgs)
        tempWriter?.Write(e.Buffer, 0, e.BytesRecorded)

        ' tinh bien do de hien thi thanh volume meter, dong thoi gom thong ke chat luong
        Dim maxSample As Single = 0
        For i = 0 To e.BytesRecorded - 2 Step 2
            Dim sampleVal = BitConverter.ToInt16(e.Buffer, i)
            Dim absRaw = Math.Abs(CInt(sampleVal))
            Dim abs = absRaw / 32768.0F
            If abs > maxSample Then maxSample = abs
            If abs > recPeakLevel Then recPeakLevel = abs
            If absRaw >= CLIP_SAMPLE_THRESHOLD Then recClippedCount += 1
            recSampleCount += 1
        Next
        currentVolume = maxSample
    End Sub

    Private Sub WaveIn_RecordingStopped(sender As Object, e As StoppedEventArgs)
        tempWriter?.Dispose()
        tempWriter = Nothing
        waveIn?.Dispose()
        waveIn = Nothing
        currentVolume = 0

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

    Private Sub SaveFinalWav()
        Try
            Dim finalPath = Path.Combine(wavsFolder, GetFileId(currentIndex) & ".wav")

            Using reader As New AudioFileReader(tempFilePath)
                Dim targetFormat = New WaveFormat(TARGET_SAMPLE_RATE, TARGET_BITS, TARGET_CHANNELS)
                Using resampler As New MediaFoundationResampler(reader, targetFormat)
                    resampler.ResamplerQuality = 60
                    WaveFileWriter.CreateWaveFile(finalPath, resampler)
                End Using
            End Using

            File.Delete(tempFilePath)

            recordedIndices.Add(currentIndex)
            UpdateMetadata(currentIndex, sentences(currentIndex))

            lblStatus.Text = $"Da luu: {GetFileId(currentIndex)}.wav"
            lblStatus.ForeColor = Color.Green
            ShowSentence()
        Catch ex As Exception
            MessageBox.Show("Loi khi luu file audio: " & ex.Message, "Loi", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
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

    ' ===================== Dieu huong =====================

    Private Sub BtnPrev_Click(sender As Object, e As EventArgs)
        If currentIndex > 0 Then
            currentIndex -= 1
            ShowSentence()
        End If
    End Sub

    Private Sub BtnNext_Click(sender As Object, e As EventArgs)
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
    End Sub

    Private Sub MainForm_KeyDown(sender As Object, e As KeyEventArgs)
        Select Case e.KeyCode
            Case Keys.Space
                ToggleRecording()
                e.Handled = True
            Case Keys.R
                If Not isRecording Then StartRecording()
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
        If isRecording Then StopRecording()
        SaveLastPosition()
        meterTimer?.Stop()
        MyBase.OnFormClosing(e)
    End Sub

End Class
