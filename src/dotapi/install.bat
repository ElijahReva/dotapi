dotnet pack -c release -o nupkg
dotnet tool uninstall -g dotapi
dotnet tool install --add-source ./nupkg -g dotapi