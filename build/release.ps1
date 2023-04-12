$ErrorActionPreference = "Stop"
cd ..\MergeSynced

echo Intitialize..
if (Test-Path -Path '..\build\MergeSynced') {
    Remove-Item -Recurse -Force ..\build\MergeSynced
}
if (Test-Path -Path '..\build\MergeSynced_win-x64.zip') {
    Remove-Item -Recurse -Force ..\build\MergeSynced_win-x64.zip
}
if (Test-Path -Path '..\build\MergeSynced_linux-arm64.zip') {
    Remove-Item -Recurse -Force ..\build\MergeSynced_linux-arm64.zip
}
if (Test-Path -Path '..\build\MergeSynced_linux-x64.zip') {
    Remove-Item -Recurse -Force ..\build\MergeSynced_linux-x64.zip
}

echo Building x64 windows version...
dotnet restore win-x64
dotnet build --runtime win-x64 -property:Configuration=Release
echo Generating zip file...
move .\bin\Release\net7.0\win-x64\ ..\build\MergeSynced
Start-Sleep -Seconds 2
Compress-Archive -Path ..\build\MergeSynced -DestinationPath ..\build\MergeSynced_win-x64.zip
echo Cleanup...
Remove-Item -Recurse -Force ..\build\MergeSynced

echo Building x86 windows version...
dotnet restore win-x86
dotnet build --runtime win-x86 -property:Configuration=Release
echo Generating zip file...
move .\bin\Release\net7.0\win-x86\ ..\build\MergeSynced
Start-Sleep -Seconds 2
Compress-Archive -Path ..\build\MergeSynced -DestinationPath ..\build\MergeSynced_win-x86.zip
echo Cleanup...
Remove-Item -Recurse -Force ..\build\MergeSynced

echo Building arm64 windows version...
dotnet restore win-arm64
dotnet build --runtime win-arm64 -property:Configuration=Release
echo Generating zip file...
move .\bin\Release\net7.0\win-arm64\ ..\build\MergeSynced
Start-Sleep -Seconds 2
Compress-Archive -Path ..\build\MergeSynced -DestinationPath ..\build\MergeSynced_win-arm64.zip
echo Cleanup...
Remove-Item -Recurse -Force ..\build\MergeSynced

echo Building x64 linux version...
dotnet restore linux-x64
dotnet build --runtime linux-x64 -property:Configuration=Release
echo Generating zip file...
move .\bin\Release\net7.0\linux-x64\ ..\build\MergeSynced
Start-Sleep -Seconds 2
Compress-Archive -Path ..\build\MergeSynced -DestinationPath ..\build\MergeSynced_linux-x64.zip
echo Cleanup...
Remove-Item -Recurse -Force ..\build\MergeSynced

echo Building arm64 linux version...
dotnet restore linux-arm64
dotnet build --runtime linux-arm64 -property:Configuration=Release
echo Generating zip file...
move .\bin\Release\net7.0\linux-arm64\ ..\build\MergeSynced
Start-Sleep -Seconds 2
Compress-Archive -Path ..\build\MergeSynced -DestinationPath ..\build\MergeSynced_linux-arm64.zip
echo Cleanup...
Remove-Item -Recurse -Force ..\build\MergeSynced

