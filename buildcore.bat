dotnet restore STAN.Client
dotnet build -c Release STAN.Client
dotnet pack -c Release STAN.Client

dotnet restore examples
dotnet build -c Release examples/stan-sub
dotnet build -c Release examples/stan-pub
