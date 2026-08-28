# Disconnect / Disable Virtual Display Driver
$ErrorActionPreference = "SilentlyContinue"

# 1. Zero monitor count in vdd_settings.xml (silently removes monitors)
$vddSettings = "C:\VirtualDisplayDriver\vdd_settings.xml"
if (Test-Path $vddSettings) {
    try {
        [xml]$xml = Get-Content $vddSettings
        if ($xml.vdd_settings.monitors) {
            $xml.vdd_settings.monitors.count = "0"
            $xml.Save($vddSettings)
        }
    } catch {}
}

# 2. Disable PnP device
& pnputil.exe /disable-device "ROOT\DISPLAY\0000"
