@echo off
cd /d "%~dp0"
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
"%CSC%" /out:WindKeyClient.exe Program.cs
if %ERRORLEVEL% == 0 (
    echo Build Success! Run WindKeyClient.exe
) else (
    echo Build Failed.
)
pause
