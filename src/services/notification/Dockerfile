FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["ShopNGo.slnx", "./"]
COPY ["Directory.Packages.props", "./"]
COPY ["src/shared/contracts/Contracts.csproj", "src/shared/contracts/"]
COPY ["src/shared/building-blocks/BuildingBlocks.csproj", "src/shared/building-blocks/"]
COPY ["src/services/notification/NotificationService.csproj", "src/services/notification/"]
RUN dotnet restore "src/services/notification/NotificationService.csproj"

COPY . .
RUN dotnet publish "src/services/notification/NotificationService.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "NotificationService.dll"]
