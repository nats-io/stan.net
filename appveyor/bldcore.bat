dotnet restore STAN.Client
dotnet build -c Release STAN.Client
dotnet pack -c Release STAN.Client

dotnet restore examples STAN.Client.UnitTests
dotnet build -c Release examples/stan-sub
dotnet build -c Release examples/stan-pub

REM msbuild STANcore.sln /logger:"C:\Program Files\AppVeyor\BuildAgent\Appveyor.MSBuildLogger.dll"
