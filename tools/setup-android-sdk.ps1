# Ставит портативные JDK 17 + Android SDK + Gradle в D:\LazyASF\_sdk (без админки)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = Join-Path $PSScriptRoot '..\_sdk' | Resolve-Path -ErrorAction SilentlyContinue
if (-not $root) { $root = New-Item -ItemType Directory -Force (Join-Path $PSScriptRoot '..\_sdk') }
$root = "$root"
$dl = Join-Path $root 'downloads'
New-Item -ItemType Directory -Force $dl | Out-Null

function Get-Zip($url, $name) {
    $f = Join-Path $dl $name
    if (-not (Test-Path $f)) { Write-Host "Downloading $url"; curl.exe -L --fail -o $f $url; if ($LASTEXITCODE) { throw "download failed: $url" } }
    return $f
}

# JDK 17 (Microsoft Build of OpenJDK)
$jdk = Join-Path $root 'jdk17'
if (-not (Test-Path "$jdk\bin\java.exe")) {
    $z = Get-Zip 'https://aka.ms/download-jdk/microsoft-jdk-17-windows-x64.zip' 'jdk17.zip'
    $tmp = Join-Path $root 'jdk_tmp'; Expand-Archive $z $tmp -Force
    Move-Item (Get-ChildItem $tmp -Directory | Select -First 1).FullName $jdk; Remove-Item $tmp -Recurse -Force
}

# Android command-line tools
$sdk = Join-Path $root 'android'
$cl = Join-Path $sdk 'cmdline-tools\latest'
if (-not (Test-Path "$cl\bin\sdkmanager.bat")) {
    $z = Get-Zip 'https://dl.google.com/android/repository/commandlinetools-win-11076708_latest.zip' 'cmdline-tools.zip'
    $tmp = Join-Path $root 'cl_tmp'; Expand-Archive $z $tmp -Force
    New-Item -ItemType Directory -Force (Split-Path $cl) | Out-Null
    Move-Item (Join-Path $tmp 'cmdline-tools') $cl; Remove-Item $tmp -Recurse -Force
}

# Gradle
$gradle = Join-Path $root 'gradle-8.11.1'
if (-not (Test-Path "$gradle\bin\gradle.bat")) {
    $z = Get-Zip 'https://services.gradle.org/distributions/gradle-8.11.1-bin.zip' 'gradle.zip'
    Expand-Archive $z $root -Force
}

$env:JAVA_HOME = $jdk
$env:ANDROID_HOME = $sdk
$env:Path = "$jdk\bin;$env:Path"
Write-Host "Accepting licenses + installing platform/build-tools"
cmd /c "(for /l %i in (1,1,40) do @echo y) | `"$cl\bin\sdkmanager.bat`" --sdk_root=`"$sdk`" --licenses" | Out-Null
& "$cl\bin\sdkmanager.bat" --sdk_root=$sdk "platform-tools" "platforms;android-35" "build-tools;35.0.0"
if ($LASTEXITCODE) { throw "sdkmanager failed" }
Write-Host "SDK READY: $root"
