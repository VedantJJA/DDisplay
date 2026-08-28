# Connect / Enable Virtual Display Driver
$ErrorActionPreference = "SilentlyContinue"

# 1. Ensure C:\VirtualDisplayDriver directory and vdd_settings.xml exist
$vddDir = "C:\VirtualDisplayDriver"
if (-not (Test-Path $vddDir)) {
    New-Item -ItemType Directory -Path $vddDir -Force | Out-Null
}

$vddSettings = "C:\VirtualDisplayDriver\vdd_settings.xml"
if (Test-Path $vddSettings) {
    # Set monitor count to 1 without destroying resolutions
    try {
        [xml]$xml = Get-Content $vddSettings
        if ($xml.vdd_settings.monitors) {
            $xml.vdd_settings.monitors.count = "1"
            $xml.Save($vddSettings)
        }
    } catch {}
} else {
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
            <width>1080</width>
            <height>1920</height>
            <refresh_rate>60</refresh_rate>
        </resolution>
        <resolution>
            <width>2400</width>
            <height>1080</height>
            <refresh_rate>60</refresh_rate>
        </resolution>
        <resolution>
            <width>1080</width>
            <height>2400</height>
            <refresh_rate>60</refresh_rate>
        </resolution>
    </resolutions>
</vdd_settings>
"@
    [System.IO.File]::WriteAllText($vddSettings, $xmlContent)
}

# 2. Enable PnP device directly
& pnputil.exe /enable-device "ROOT\DISPLAY\0000"
