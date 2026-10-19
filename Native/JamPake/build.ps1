param([string]$BoringSslSource = "$PSScriptRoot/boringssl")
$ErrorActionPreference = 'Stop'
$revision = '5fbad2285b096858fc9afa3e4c949fde39452070'
if (!(Test-Path -LiteralPath "$BoringSslSource/.git")) {
    git clone https://boringssl.googlesource.com/boringssl $BoringSslSource
    if ($LASTEXITCODE) { throw 'BoringSSL clone failed' }
    git -C $BoringSslSource checkout --detach $revision
}
$actual = git -C $BoringSslSource rev-parse HEAD
if ($actual -ne $revision) { throw "Unexpected BoringSSL revision: $actual" }
cmake -S $PSScriptRoot -B "$PSScriptRoot/build" -G 'Visual Studio 17 2022' -A x64 "-DBORINGSSL_SOURCE=$BoringSslSource"
if ($LASTEXITCODE) { throw 'CMake configure failed' }
cmake --build "$PSScriptRoot/build" --config Release --target JamPake --parallel 8
if ($LASTEXITCODE) { throw 'Native build failed' }
$destination = "$PSScriptRoot/../../YeusepesLowLevelTools/Natives"
New-Item -ItemType Directory -Force -Path $destination | Out-Null
Copy-Item -LiteralPath "$PSScriptRoot/build/Release/JamPake.dll" -Destination $destination
Copy-Item -LiteralPath "$BoringSslSource/LICENSE" -Destination "$destination/BoringSSL-LICENSE.txt"
