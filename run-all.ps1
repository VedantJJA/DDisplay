# DDisplay one-click launcher for Android and Windows desktop apps
$ErrorActionPreference = "Stop"

Set-Location $PSScriptRoot

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "       DDisplay All-in-One Launcher     " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# 1. Locate ADB
$adbPath = "adb"
$localAdb = "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe"
if (Test-Path $localAdb) {
    $adbPath = $localAdb
}

# 2. Check for connected Android device
Write-Host "`n[1/4] Checking connected Android devices..." -ForegroundColor Yellow
$devicesOutput = & $adbPath devices
$devices = @()
foreach ($line in ($devicesOutput -split "`r?`n" | Select-Object -Skip 1)) {
    $parts = $line.Trim() -split "`t"
    if ($parts.Length -eq 2 -and $parts[1].Trim() -eq "device") {
        $devices += $parts[0].Trim()
    }
}

if ($devices.Count -gt 0) {
    $serial = $devices[0]
    Write-Host "Found Android device: $serial" -ForegroundColor Green

    # Set Java Home for Gradle build
    $jbrPath = "C:\Program Files\Android\Android Studio\jbr"
    if (Test-Path $jbrPath) {
        $env:JAVA_HOME = $jbrPath
    }

    # 3. Build & install Android app
    Write-Host "`n[2/4] Building Android app..." -ForegroundColor Yellow
    Push-Location "android"
    try {
        & ".\gradlew.bat" assembleDebug
    } finally {
        Pop-Location
    }

    Write-Host "`n[3/4] Installing and launching Android app on $serial..." -ForegroundColor Yellow
    $apkPath = "android\app\build\outputs\apk\debug\app-debug.apk"
    if (Test-Path $apkPath) {
        & $adbPath -s $serial install -r $apkPath
        & $adbPath -s $serial reverse tcp:7878 tcp:7878
        & $adbPath -s $serial shell am start -n com.ddisplay.app/.ui.MainActivity
        Write-Host "Android app started successfully." -ForegroundColor Green
    } else {
        Write-Host "APK not found at $apkPath" -ForegroundColor Red
    }
} else {
    Write-Host "No Android device detected over USB. Continuing with Windows app..." -ForegroundColor Yellow
}

# 4. Run Windows Desktop App
Write-Host "`n[4/4] Starting DDisplay Windows Host App..." -ForegroundColor Yellow
dotnet run --project windows/DDisplay.App/DDisplay.App.csproj
