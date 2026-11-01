@echo off
rem Builds deliveryquotes_native.dll for Windows x64 with MinGW-w64 gcc (64-bit!).
rem Using Visual Studio instead? Open "x64 Native Tools Command Prompt" and run:
rem   cl /LD /O2 dq_surcharge.c /Fe:out\deliveryquotes_native.dll
cd /d "%~dp0"
if not exist out mkdir out
gcc -shared -O2 -Wall -Wextra -o out\deliveryquotes_native.dll dq_surcharge.c
if errorlevel 1 (
  echo Build failed. Is a 64-bit gcc on PATH?
  exit /b 1
)
echo native library built in native\out
