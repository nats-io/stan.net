@setlocal
@echo off

REM This builds the NATS Streaming .NET Client NuGet package, and is 
REM intended for use by the NATS maintainers to create NuGet Packages.

nuget >nul 2>&1
if errorlevel 9009 if not errorlevel 9010 (
    echo 'nuget.exe' is not in the path.
    goto End
)

set STAN_CLIENT_45=..\net45\STAN.Client\bin\Release\STAN.Client.dll
set STAN_CLIENT_XML_45=..\net45\STAN.Client\bin\Release\STAN.Client.xml
set STAN_CORE_DIR=..\STAN.Client\bin\Release\netstandard1.6

if NOT EXIST %STAN_CLIENT_45% (
    echo Cannot find %STAN_CLIENT_45%
    goto End
)


if NOT EXIST %STAN_CLIENT_XML_45% (
    echo Cannot find %STAN_CLIENT_XML_45%
    goto End
)

if NOT EXIST %STAN_CORE_DIR% (
    echo Cannot find .NET core build.
    goto End
)

mkdir tmp 2>NUL
mkdir tmp\lib 2>NUL
mkdir tmp\lib\net45 2>NUL
mkdir tmp\lib\netstandard1.6 2>NUL

copy %STAN_CLIENT_45% tmp\lib\net45 1>NUL
copy %STAN_CLIENT_XML_45% tmp\lib\net45 1>NUL
copy %STAN_CORE_DIR%\* tmp\lib\netstandard1.6 1>NUL

REM (to recreate) nuget spec -f -Verbosity detailed -AssemblyPath STAN.Client.dll

cd tmp

copy ..\STAN.Client.nuspec . 1>NUL

nuget pack STAN.Client.nuspec

move *.nupkg .. 1>NUL

cd ..

:End

rmdir /S /Q tmp 2>NUL
