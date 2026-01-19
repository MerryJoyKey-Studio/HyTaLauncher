# HyTaLauncher Build Script
# Reads secrets from .env file and injects into Config.cs before build

param(
    [switch]$Portable,
    [switch]$Release,
    [switch]$Sign
)

$ErrorActionPreference = "Stop"

# Read .env file
$envFile = ".env"
$apiKey = ""
$mirrorUrl = ""
$russifierUrl = ""
$onlineFixUrl = ""
$certPassword = ""

if (Test-Path $envFile) {
    Get-Content $envFile | ForEach-Object {
        if ($_ -match "^CURSEFORGE_API_KEY=(.+)$") {
            $apiKey = $matches[1].Trim()
        }
        if ($_ -match "^MIRROR_URL=(.+)$") {
            $mirrorUrl = $matches[1].Trim()
        }
        if ($_ -match "^RUSSIFIER_URL=(.+)$") {
            $russifierUrl = $matches[1].Trim()
        }
        if ($_ -match "^ONLINEFIX_URL=(.+)$") {
            $onlineFixUrl = $matches[1].Trim()
        }
        if ($_ -match "^CERT_PASSWORD=(.+)$") {
            $certPassword = $matches[1].Trim()
        }
    }
}

# Function to sign executable
function Sign-Executable {
    param([string]$FilePath)
    
    $certPath = "HyTaLauncher.pfx"
    
    if (-not (Test-Path $certPath)) {
        Write-Host "Certificate not found. Run create-certificate.ps1 first." -ForegroundColor Yellow
        return $false
    }
    
    if ([string]::IsNullOrEmpty($certPassword)) {
        Write-Host "CERT_PASSWORD not found in .env file" -ForegroundColor Yellow
        return $false
    }
    
    Write-Host "Signing $FilePath..." -ForegroundColor Cyan
    
    try {
        $certPassword_secure = ConvertTo-SecureString -String $certPassword -Force -AsPlainText
        
        # Use signtool if available, otherwise use Set-AuthenticodeSignature
        $signtool = "C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe"
        
        if (Test-Path $signtool) {
            & $signtool sign /f $certPath /p $certPassword /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 $FilePath
        } else {
            # Fallback to PowerShell signing
            $cert = Get-PfxCertificate -FilePath $certPath -Password $certPassword_secure
            Set-AuthenticodeSignature -FilePath $FilePath -Certificate $cert -TimestampServer "http://timestamp.digicert.com" | Out-Null
        }
        
        Write-Host "Signed successfully: $FilePath" -ForegroundColor Green
        return $true
    }
    catch {
        Write-Host "Failed to sign: $_" -ForegroundColor Red
        return $false
    }
}

# Warnings for missing secrets
if ([string]::IsNullOrEmpty($apiKey)) {
    Write-Host "Warning: CURSEFORGE_API_KEY not found - Mods feature will not work" -ForegroundColor Yellow
}
if ([string]::IsNullOrEmpty($mirrorUrl)) {
    Write-Host "Warning: MIRROR_URL not found - Mirror feature will not work" -ForegroundColor Yellow
}
if ([string]::IsNullOrEmpty($russifierUrl)) {
    Write-Host "Warning: RUSSIFIER_URL not found - Russifier feature will not work" -ForegroundColor Yellow
}
if ([string]::IsNullOrEmpty($onlineFixUrl)) {
    Write-Host "Warning: ONLINEFIX_URL not found - Online fix feature will not work" -ForegroundColor Yellow
}

# Backup and modify Config.cs
$configPath = "HyTaLauncher\Config.cs"
$configContent = Get-Content $configPath -Raw
$configBackup = $configContent

# Escape special regex characters in replacement - use [regex]::Escape for pattern
$configContent = $configContent -replace [regex]::Escape('#{CURSEFORGE_API_KEY}#'), $apiKey
$configContent = $configContent -replace [regex]::Escape('#{MIRROR_URL}#'), $mirrorUrl
$configContent = $configContent -replace [regex]::Escape('#{RUSSIFIER_URL}#'), $russifierUrl
$configContent = $configContent -replace [regex]::Escape('#{ONLINEFIX_URL}#'), $onlineFixUrl
Set-Content $configPath $configContent

try {
    # Build
    $config = if ($Release) { "Release" } else { "Debug" }
    
    Write-Host "Building HyTaLauncher ($config)..." -ForegroundColor Cyan
    
    if ($Portable) {
        dotnet publish HyTaLauncher -c $config -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
        
        if ($LASTEXITCODE -eq 0) {
            $exePath = "HyTaLauncher\bin\$config\net8.0-windows\win-x64\publish\HyTaLauncher.exe"
            
            # Sign executable if requested
            if ($Sign) {
                Sign-Executable -FilePath $exePath
            }
            
            # Create portable zip
            $publishDir = "HyTaLauncher\bin\$config\net8.0-windows\win-x64\publish"
            $portableDir = "portable"
            $version = "1.0.7"
            $zipName = "HyTaLauncher_Portable_$version.zip"
            
            if (Test-Path $portableDir) { Remove-Item -Recurse -Force $portableDir }
            New-Item -ItemType Directory -Path $portableDir | Out-Null
            
            Copy-Item "$publishDir\HyTaLauncher.exe" $portableDir
            Copy-Item -Recurse "HyTaLauncher\Fonts" "$portableDir\Fonts"
            Copy-Item -Recurse "HyTaLauncher\Languages" "$portableDir\Languages"
            
            # Copy Addons folder if exists
            if (Test-Path "HyTaLauncher\Addons") {
                Copy-Item -Recurse "HyTaLauncher\Addons" "$portableDir\Addons"
            }
            
            if (Test-Path $zipName) { Remove-Item $zipName }
            Compress-Archive -Path "$portableDir\*" -DestinationPath $zipName -CompressionLevel Optimal
            
            Remove-Item -Recurse -Force $portableDir
            
            Write-Host "Portable build created: $zipName" -ForegroundColor Green
        }
    } else {
        dotnet build HyTaLauncher -c $config
        
        if ($LASTEXITCODE -eq 0 -and $Sign) {
            $exePath = "HyTaLauncher\bin\$config\net8.0-windows\HyTaLauncher.exe"
            if (Test-Path $exePath) {
                Sign-Executable -FilePath $exePath
            }
        }
    }
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Build successful!" -ForegroundColor Green
    } else {
        Write-Host "Build failed!" -ForegroundColor Red
    }
}
finally {
    # Restore Config.cs
    Set-Content $configPath $configBackup
}
