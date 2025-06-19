@echo off
chcp 65001

echo Creating build configurations...
dotnet build -c Release

echo Building Self-Contained Release...
dotnet publish -c Release -p:SelfContained=true -p:PublishSingleFile=true -p:RuntimeIdentifier=win-x64 -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:DebugSymbols=false -p:EnableCompressionInSingleFile=true -o publish/self-contained
echo Building Trimmed Release...
dotnet publish -c Release -p:SelfContained=false -p:PublishSingleFile=true -p:RuntimeIdentifier=win-x64 -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:DebugSymbols=false -o publish/trimmed
echo Build completed.
echo Self-contained version is in: publish/self-contained
echo Trimmed version is in: publish/trimmed
pause

