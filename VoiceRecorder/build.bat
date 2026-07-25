@echo off
echo ===================================
echo   Build VoiceRecorder (.NET 9)
echo ===================================

dotnet build -c Release

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo BUILD THAT BAI.
    pause
    exit /b 1
)

echo.
echo Build thanh cong.
echo File .exe nam trong: bin\Release\net9.0-windows\VoiceRecorder.exe
pause
