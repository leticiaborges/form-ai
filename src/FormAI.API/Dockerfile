FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/", "src/"]
RUN dotnet restore "src/FormAI.API/FormAI.API.csproj"
RUN dotnet publish "src/FormAI.API/FormAI.API.csproj" -c Release -o /app/publish --no-restore


FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "FormAI.API.dll"]
