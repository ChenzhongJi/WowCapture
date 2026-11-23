@echo off
rem 双击即可发布：绕过 PowerShell 默认执行策略（脚本本身只做 dotnet publish + 建快捷方式）。
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish.ps1" %*
pause
