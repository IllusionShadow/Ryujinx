@echo off
set branch=%1
for /f "tokens=1,2 delims=:" %%a in ("%branch%") do (
  set BEFORE=%%a
  set AFTER=%%b
)

git pull https://github.com/%BEFORE%/Ryujinx.git %AFTER%