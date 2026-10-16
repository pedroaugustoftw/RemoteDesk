FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY shared/ shared/
COPY relay/ relay/
RUN dotnet publish relay/RemoteDesk.Relay.csproj -c Release -o /publish
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /publish .
ENV ASPNETCORE_URLS=http://+:8081
EXPOSE 8081
ENTRYPOINT ["dotnet", "RemoteDesk.Relay.dll"]
