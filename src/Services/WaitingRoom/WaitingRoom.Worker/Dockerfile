FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .
RUN dotnet restore src/Services/WaitingRoom/WaitingRoom.Worker/WaitingRoom.Worker.csproj
RUN dotnet publish src/Services/WaitingRoom/WaitingRoom.Worker/WaitingRoom.Worker.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "WaitingRoom.Worker.dll"]
