@echo off
rem Builds AutoClicker.exe with the C# compiler bundled with .NET Framework 4.x (no install needed)
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /codepage:65001 /target:winexe /optimize+ /out:AutoClicker.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll AutoClicker.cs
if %errorlevel%==0 (echo OK: AutoClicker.exe gerado) else (echo Falha na compilacao)
pause
