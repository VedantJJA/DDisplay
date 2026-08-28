# Connect / Enable Virtual Display Driver
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Start-Process powershell.exe -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs -Wait
    exit
}

Write-Host "Enabling Virtual Display Driver..." -ForegroundColor Cyan

# 1. Try pnputil direct enable
& pnputil.exe /enable-device "ROOT\DISPLAY\0000"

# 2. Try PnpDevice enable
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
