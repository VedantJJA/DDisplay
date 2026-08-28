# One-click installer for Virtual Display Driver (VDD)
# Requires Administrator privileges

param(
    [string]$NefConURL = "https://github.com/nefarius/nefcon/releases/download/v1.14.0/nefcon_v1.14.0.zip",
    [string]$DriverURL = "https://github.com/VirtualDrivers/Virtual-Display-Driver/releases/download/25.7.23/VirtualDisplayDriver-x86.Driver.Only.zip"
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "   DDisplay - Virtual Display Driver" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Create temp working directory
$tempDir = Join-Path $env:TEMP "DDisplay_VDD_Install"
if (Test-Path $tempDir) {
    Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

try {
    # 1. Download and extract NefCon
    Write-Host "[1/4] Downloading NefCon device installer..." -ForegroundColor Yellow
    $nefConZip = Join-Path $tempDir "nefcon.zip"
    Invoke-WebRequest -Uri $NefConURL -OutFile $nefConZip -UseBasicParsing
    Expand-Archive -Path $nefConZip -DestinationPath $tempDir -Force
    $nefConExe = Join-Path $tempDir "x64\nefconw.exe"

    # 2. Download and extract VDD Driver
    Write-Host "[2/4] Downloading Virtual Display Driver..." -ForegroundColor Yellow
    $driverZip = Join-Path $tempDir "driver.zip"
    Invoke-WebRequest -Uri $DriverURL -OutFile $driverZip -UseBasicParsing
    Expand-Archive -Path $driverZip -DestinationPath $tempDir -Force

    # 3. Import Driver Certificates
    Write-Host "[3/4] Installing TrustedPublisher certificates..." -ForegroundColor Yellow
    $catFile = Join-Path $tempDir "VirtualDisplayDriver\mttvdd.cat"
    if (Test-Path $catFile) {
        $catBytes = [System.IO.File]::ReadAllBytes($catFile)
        $certs = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2Collection
        $certs.Import($catBytes)

        $certsDir = Join-Path $tempDir "certs"
        New-Item -ItemType Directory -Path $certsDir -Force | Out-Null

        foreach ($cert in $certs) {
            $certPath = Join-Path $certsDir "$($cert.Thumbprint).cer"
            $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert) | Set-Content -Path $certPath -Encoding Byte
            Import-Certificate -FilePath $certPath -CertStoreLocation "Cert:\LocalMachine\TrustedPublisher" | Out-Null
        }
    }

    # 4. Install Driver
    Write-Host "[4/4] Installing driver to system..." -ForegroundColor Yellow
    Push-Location $tempDir
    & $nefConExe install .\VirtualDisplayDriver\MttVDD.inf "Root\MttVDD"
    Start-Sleep -Seconds 5
    Pop-Location

    Write-Host "Virtual Display Driver installed successfully!" -ForegroundColor Green
}
catch {
    Write-Host "Installation failed: $($_.Exception.Message)" -ForegroundColor Red
}
finally {
    Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Press any key to close..."
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
