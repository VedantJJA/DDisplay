# Connect / Enable Virtual Display Driver
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Start-Process powershell.exe -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs -Wait
    exit
}

Write-Host "Configuring and Enabling Virtual Display Driver..." -ForegroundColor Cyan

# 1. Ensure C:\VirtualDisplayDriver directory and vdd_settings.xml exist
$vddDir = "C:\VirtualDisplayDriver"
if (-not (Test-Path $vddDir)) {
    New-Item -ItemType Directory -Path $vddDir -Force | Out-Null
}

$vddSettings = "C:\VirtualDisplayDriver\vdd_settings.xml"
$repoSettings = Join-Path $PSScriptRoot "Virtual-Display-Driver\Virtual Display Driver (HDR)\vdd_settings.xml"

if (Test-Path $repoSettings) {
    Copy-Item -Path $repoSettings -Destination $vddSettings -Force
} elseif (-not (Test-Path $vddSettings)) {
    $xmlContent = @"
<?xml version="1.0" encoding="utf-8"?>
<vdd_settings>
    <monitors>
        <count>1</count>
    </monitors>
    <gpu>
        <friendlyname>default</friendlyname>
    </gpu>
    <global>
        <g_refresh_rate>60</g_refresh_rate>
        <g_refresh_rate>90</g_refresh_rate>
        <g_refresh_rate>120</g_refresh_rate>
    </global>
    <resolutions>
        <resolution>
            <width>1920</width>
            <height>1080</height>
            <refresh_rate>60</refresh_rate>
        </resolution>
        <resolution>
            <width>2400</width>
            <height>1080</height>
            <refresh_rate>60</refresh_rate>
        </resolution>
    </resolutions>
</vdd_settings>
"@
    [System.IO.File]::WriteAllText($vddSettings, $xmlContent)
}

# 2. Try pnputil direct enable and restart
& pnputil.exe /enable-device "ROOT\DISPLAY\0000"
& pnputil.exe /restart-device "ROOT\DISPLAY\0000"

# 3. Try PnpDevice enable
$devices = Get-PnpDevice -Class Display -ErrorAction SilentlyContinue | Where-Object { 
    $_.FriendlyName -like "*Virtual Display*" -or 
    $_.FriendlyName -like "*IddSample*" -or 
    $_.InstanceId -like "*DISPLAY\0000*"
}

if ($devices) {
    foreach ($dev in $devices) {
        Write-Host "Enabling: $($dev.FriendlyName) ($($dev.InstanceId))" -ForegroundColor Yellow
        $dev | Enable-PnpDevice -Confirm:$false -ErrorAction SilentlyContinue
    }
    Write-Host "Virtual Display Driver has been enabled/connected." -ForegroundColor Green
} else {
    Write-Host "No Virtual Display Driver device found." -ForegroundColor Red
}

Start-Sleep -Seconds 1
