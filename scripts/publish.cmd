@echo off
rem Double-click to publish. Bypasses the default PowerShell execution policy.
rem (publish.ps1 only runs dotnet publish and creates the desktop shortcut.)
rem NOTE: keep this file pure ASCII + CRLF. cmd.exe parses batch files byte-wise
rem under the ANSI codepage, and UTF-8 Chinese comments corrupt the parse.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish.ps1" %*
pause
