Imports System.ComponentModel
Imports System.Drawing
Imports System.Windows.Forms

''' <summary>
''' Control ve waveform don gian: nhan mot mang gia tri bien do (0..1, da lay peak
''' theo tung "cot") va ve thanh cac vach doc doi xung qua duong giua.
''' Dung cho ca 2 truong hop:
'''   - Live: trong luc dang ghi am, cap nhat lien tuc tu ring buffer cac peak gan nhat.
'''   - Preview: sau khi ghi xong (hoac khi luot lai cau da thu), hien toan bo dang song
'''     cua file da luu (sau khi da cat khoang lang + chuan hoa am luong).
''' </summary>
Public Class WaveformPanel
    Inherits Panel

    Private samples As Single() = Array.Empty(Of Single)()

    ''' <summary>Mau ve waveform hien tai (doi theo ngu canh: dang ghi / da luu / rong).</summary>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)>
    <Browsable(False)>
    Public Property WaveColor As Color = Color.FromArgb(0, 120, 215)

    Public Sub New()
        Me.SetStyle(ControlStyles.OptimizedDoubleBuffer Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.UserPaint, True)
        Me.DoubleBuffered = True
        Me.BackColor = Color.White
        Me.BorderStyle = BorderStyle.FixedSingle
    End Sub

    ''' <summary>Cap nhat du lieu waveform va ve lai. color (neu co) doi luon mau ve.</summary>
    Public Sub SetSamples(newSamples As Single(), Optional color As Color? = Nothing)
        samples = If(newSamples, Array.Empty(Of Single)())
        If color.HasValue Then WaveColor = color.Value
        Me.Invalidate()
    End Sub

    Public Sub Clear()
        samples = Array.Empty(Of Single)()
        Me.Invalidate()
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)

        Dim g = e.Graphics
        g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias

        Dim w = Me.ClientSize.Width
        Dim h = Me.ClientSize.Height
        If w <= 0 OrElse h <= 0 Then Return

        Dim midY = h \ 2

        ' duong giua lam moc
        Using midPen As New Pen(Color.FromArgb(225, 225, 225))
            g.DrawLine(midPen, 0, midY, w, midY)
        End Using

        If samples Is Nothing OrElse samples.Length = 0 Then Return

        Dim n = samples.Length
        Using pen As New Pen(WaveColor, 1)
            For x = 0 To w - 1
                Dim idx = CInt(x / CDbl(w) * n)
                If idx >= n Then idx = n - 1
                If idx < 0 Then idx = 0

                Dim amp = Math.Min(1.0F, Math.Abs(samples(idx)))
                Dim barHeight = CInt(amp * (h / 2.0 - 2))
                If barHeight < 1 Then barHeight = 1

                g.DrawLine(pen, x, midY - barHeight, x, midY + barHeight)
            Next
        End Using
    End Sub

End Class
