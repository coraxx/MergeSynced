$ErrorActionPreference = "Stop"
cd ..\MergeSynced

echo Intitialize..
if (Test-Path -Path '..\build\MergeSynced') {
    Remove-Item -Recurse -Force ..\build\MergeSynced
}
if (Test-Path -Path '..\build\MergeSynced_win-x64.zip') {
    Remove-Item -Recurse -Force ..\build\MergeSynced_win-x64.zip
}
if (Test-Path -Path '..\build\MergeSynced_win-x86.zip') {
    Remove-Item -Recurse -Force ..\build\MergeSynced_win-x86.zip
}
if (Test-Path -Path '..\build\MergeSynced_win-arm64.zip') {
    Remove-Item -Recurse -Force ..\build\MergeSynced_win-arm64.zip
}
# if (Test-Path -Path '..\build\MergeSynced_linux-arm64.zip') {
#     Remove-Item -Recurse -Force ..\build\MergeSynced_linux-arm64.zip
# }
# if (Test-Path -Path '..\build\MergeSynced_linux-x64.zip') {
#     Remove-Item -Recurse -Force ..\build\MergeSynced_linux-x64.zip
# }

echo Building x64 windows version...
dotnet clean
dotnet publish -p:RuntimeIdentifier=win-x64 -p:Configuration=Release
echo Generating zip file...
move .\bin\Release\net7.0\win-x64\publish\ ..\build\MergeSynced
Compress-Archive -Path ..\build\MergeSynced -DestinationPath ..\build\MergeSynced_win-x64.zip
echo Cleanup...
Remove-Item -Recurse -Force ..\build\MergeSynced
Remove-Item -Recurse -Force .\bin\Release\net7.0\win-x64

echo Building x86 windows version...
dotnet clean
dotnet publish -p:RuntimeIdentifier=win-x86 -p:Configuration=Release
echo Generating zip file...
move .\bin\Release\net7.0\win-x86\publish\ ..\build\MergeSynced
Compress-Archive -Path ..\build\MergeSynced -DestinationPath ..\build\MergeSynced_win-x86.zip
echo Cleanup...
Remove-Item -Recurse -Force ..\build\MergeSynced
Remove-Item -Recurse -Force .\bin\Release\net7.0\win-x86

echo Building arm64 windows version...
dotnet clean
dotnet publish -p:RuntimeIdentifier=win-arm64 -p:Configuration=Release
echo Generating zip file...
move .\bin\Release\net7.0\win-arm64\publish\ ..\build\MergeSynced
Compress-Archive -Path ..\build\MergeSynced -DestinationPath ..\build\MergeSynced_win-arm64.zip
echo Cleanup...
Remove-Item -Recurse -Force ..\build\MergeSynced
Remove-Item -Recurse -Force .\bin\Release\net7.0\win-arm64

# Moved to build script on mac so chmod +x can be applied correctly
# echo Building x64 linux version...
# dotnet restore linux-x64
# dotnet publish -p:RuntimeIdentifier=linux-x64 -p:Configuration=Release
# echo Generating zip file...
# move .\bin\Release\net7.0\linux-x64\publish\ ..\build\MergeSynced
# bash ../build/setChmod.sh
# Compress-Archive -Path ..\build\MergeSynced -DestinationPath ..\build\MergeSynced_linux-x64.zip
# echo Cleanup...
# Remove-Item -Recurse -Force ..\build\MergeSynced
# Remove-Item -Recurse -Force .\bin\Release\net7.0\linux-x64

# echo Building arm64 linux version...
# dotnet restore linux-arm64
# dotnet build --runtime linux-arm64 -property:Configuration=Release
# dotnet publish -p:RuntimeIdentifier=linux-arm64 -p:Configuration=Release
# echo Generating zip file...
# move .\bin\Release\net7.0\linux-arm64\publish\ ..\build\MergeSynced
# bash ../build/setChmod.sh
# Compress-Archive -Path ..\build\MergeSynced -DestinationPath ..\build\MergeSynced_linux-arm64.zip
# echo Cleanup...
# Remove-Item -Recurse -Force ..\build\MergeSynced
# Remove-Item -Recurse -Force .\bin\Release\net7.0\linux-arm64

dotnet clean
