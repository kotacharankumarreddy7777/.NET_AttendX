FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["AttendX/AttendX.csproj", "AttendX/"]
COPY ["AttendX.Tests/AttendX.Tests.csproj", "AttendX.Tests/"]
RUN dotnet restore "AttendX/AttendX.csproj"
COPY . .
WORKDIR "/src/AttendX"
RUN dotnet build "AttendX.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "AttendX.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=publish /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "AttendX.dll"]
