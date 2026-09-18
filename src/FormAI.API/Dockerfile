FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["FormAI.sln", "."]
COPY ["src/", "src/"]
COPY ["tests/", "tests/"]
RUN dotnet restore "FormAI.sln"
RUN dotnet publish "src/FormAI.API/FormAI.API.csproj" -c Release -o /app/publish --no-restore


FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "FormAI.API.dll"]
