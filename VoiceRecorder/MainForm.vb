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

    Private ReadOnly scriptPath As String = Path.Combine(Application.StartupPath, "script.txt")
    Private ReadOnly outputRoot As String = Path.Combine(Application.StartupPath, "dataset")
    Private ReadOnly wavsFolder As String
    Private ReadOnly metadataPath As String

    Private sentences As New List(Of String)
    Private recordedIndices As New HashSet(Of Integer)
    Private currentIndex As Integer = 0

    ' ===== NAudio =====
    Private waveIn As WaveInEvent
    Private tempWriter As WaveFileWriter
    Private tempFilePath As String
    Private isRecording As Boolean = False
    Private currentVolume As Single = 0

    ' ===== UI controls =====
    Private lblProgress As Label
    Private lblSentence As Label
    Private lblStatus As Label
    Private cmbDevice As ComboBox
    Private progressOverall As ProgressBar
    Private meterVolume As ProgressBar
    Private btnRecord As Button
    Private btnPlay As Button
    Private btnPrev As Button
    Private btnNext As Button
    Private meterTimer As Timer

    Public Sub New()
        wavsFolder = Path.Combine(outputRoot, "wavs")
        metadataPath = Path.Combine(outputRoot, "metadata.csv")
        Directory.CreateDirectory(wavsFolder)

        InitUI()
        LoadScript()
        LoadExistingProgress()
        PopulateDevices()
        ShowSentence()
    End Sub

    ' ===================== UI =====================

    Private Sub InitUI()
        Me.Text = "Voice Recorder - Thu am dataset TTS"
        Me.ClientSize = New Size(780, 460)
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

        lblSentence = New Label() With {
            .Location = New Point(20, 55), .Width = 720, .Height = 140,
            .Font = New Font("Segoe UI", 18, FontStyle.Bold),
            .TextAlign = ContentAlignment.MiddleCenter,
            .BorderStyle = BorderStyle.FixedSingle
        }
        Me.Controls.Add(lblSentence)

        meterVolume = New ProgressBar() With {
            .Location = New Point(20, 205), .Width = 720, .Height = 18, .Maximum = 100
        }
        Me.Controls.Add(meterVolume)

        lblStatus = New Label() With {
            .Location = New Point(20, 232), .Width = 720, .Text = "San sang."
        }
        Me.Controls.Add(lblStatus)

        btnPrev = New Button() With {.Location = New Point(20, 265), .Width = 120, .Height = 42, .Text = "<< Cau truoc"}
        AddHandler btnPrev.Click, AddressOf BtnPrev_Click
        Me.Controls.Add(btnPrev)

        btnRecord = New Button() With {.Location = New Point(155, 265), .Width = 210, .Height = 42, .Text = "Ghi am (Space)"}
        AddHandler btnRecord.Click, AddressOf BtnRecord_Click
        Me.Controls.Add(btnRecord)

        btnPlay = New Button() With {.Location = New Point(380, 265), .Width = 150, .Height = 42, .Text = "Nghe lai (P)"}
        AddHandler btnPlay.Click, AddressOf BtnPlay_Click
        Me.Controls.Add(btnPlay)

        btnNext = New Button() With {.Location = New Point(545, 265), .Width = 195, .Height = 42, .Text = "Cau tiep theo (Enter) >>"}
        AddHandler btnNext.Click, AddressOf BtnNext_Click
        Me.Controls.Add(btnNext)

        progressOverall = New ProgressBar() With {
            .Location = New Point(20, 325), .Width = 720, .Height = 25
        }
        Me.Controls.Add(progressOverall)

        Dim lblHint As New Label() With {
            .Location = New Point(20, 360), .Width = 720, .Height = 60,
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

    Private Sub LoadScript()
        If Not File.Exists(scriptPath) Then
            MessageBox.Show("Khong tim thay file script.txt cung thu muc voi file .exe.",
                             "Loi", MessageBoxButtons.OK, MessageBoxIcon.Error)
            sentences.Add("Khong co cau mau nao duoc tai.")
            Return
        End If

        For Each line In File.ReadAllLines(scriptPath)
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

        ' tinh bien do de hien thi thanh volume meter
        Dim maxSample As Single = 0
        For i = 0 To e.BytesRecorded - 2 Step 2
            Dim sampleVal = BitConverter.ToInt16(e.Buffer, i)
            Dim abs = Math.Abs(sampleVal) / 32768.0F
            If abs > maxSample Then maxSample = abs
        Next
        currentVolume = maxSample
    End Sub

    Private Sub WaveIn_RecordingStopped(sender As Object, e As StoppedEventArgs)
        tempWriter?.Dispose()
        tempWriter = Nothing
        waveIn?.Dispose()
        waveIn = Nothing
        currentVolume = 0

        SaveFinalWav()
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
        meterTimer?.Stop()
        MyBase.OnFormClosing(e)
    End Sub

End Class
