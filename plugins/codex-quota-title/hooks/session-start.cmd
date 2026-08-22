@echo off
setlocal DisableDelayedExpansion

set "CQT_BOOTSTRAP_DIR=%TEMP%\CodexQuotaTitle"
if "%TEMP%"=="" set "CQT_BOOTSTRAP_DIR=%CD%\CodexQuotaTitle"
if not exist "%CQT_BOOTSTRAP_DIR%" md "%CQT_BOOTSTRAP_DIR%" >nul 2>nul
set "CQT_BOOTSTRAP_LOG=%CQT_BOOTSTRAP_DIR%\hook-bootstrap.log"

call :log "SESSION_START_CMD_ENTERED"
call :log "CURRENT_DIRECTORY=%CD%"
call :environment "PLUGIN_ROOT" "%PLUGIN_ROOT%"
call :environment "PLUGIN_DATA" "%PLUGIN_DATA%"
call :environment "CODEX_HOME" "%CODEX_HOME%"

if "%PLUGIN_ROOT%"=="" (
    call :log "LAUNCHER_NOT_STARTED_PLUGIN_ROOT_MISSING"
    exit /b 0
)

set "CQT_EXECUTABLE=%PLUGIN_ROOT%\bin\win-x64\CodexQuotaTitle.exe"
if not exist "%CQT_EXECUTABLE%" (
    call :log "LAUNCHER_NOT_STARTED_EXECUTABLE_MISSING"
    exit /b 0
)

call :log "LAUNCHER_STARTING"
call :log "LAUNCHER_STARTED"
"%CQT_EXECUTABLE%" --hook-launcher 1>nul 2>>"%CQT_BOOTSTRAP_DIR%\launcher-stderr.log"
set "CQT_EXIT_CODE=%ERRORLEVEL%"
call :log "LAUNCHER_EXIT_CODE=%CQT_EXIT_CODE%"
exit /b %CQT_EXIT_CODE%

:environment
if "%~2"=="" (
    call :log "%~1_PRESENT=FALSE"
    exit /b 0
)

call :log "%~1_PRESENT=TRUE %~1_VALUE=%~2"
exit /b 0

:log
>>"%CQT_BOOTSTRAP_LOG%" 2>nul echo [%DATE% %TIME%] %~1
exit /b 0
