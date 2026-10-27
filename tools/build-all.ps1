# Пересобрать всё в D:\LazyASF\dist
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$env:JAVA_HOME = "$root\_sdk\jdk17"
$env:Path = "$env:JAVA_HOME\bin;$env:Path"

Push-Location "$root\desktop\LazyASF.Desktop"
dotnet publish -c Release -o "$root\dist\LazyASF-PC" -p:PublishSingleFile=true --self-contained false -r win-x64 -nologo
Pop-Location
Remove-Item "$root\dist\LazyASF-PC\*.pdb" -ErrorAction SilentlyContinue
Copy-Item "$root\tools\allow-firewall.cmd" "$root\dist\LazyASF-PC\" -Force

Push-Location "$root\android"
# --no-daemon: фоновый Gradle иначе держит вывод скрипта открытым, и тот «висит» после сборки
.\gradlew.bat assembleRelease --console=plain --no-daemon
Pop-Location
Copy-Item "$root\android\app\build\outputs\apk\release\app-release.apk" "$root\dist\LazyASF-phone.apk" -Force
Write-Host "Готово: $root\dist"
