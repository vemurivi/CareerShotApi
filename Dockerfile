# Use the official .NET 6 SDK image for building the app
FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build

# Set the working directory
WORKDIR /app

# Copy the project file and restore any dependencies
COPY *.csproj ./
RUN dotnet restore

# Copy the remaining files and build the app
COPY . ./
RUN dotnet publish -c Release -o out

# Use the official .NET 6 runtime image for running the app
FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS runtime

# Set the working directory
WORKDIR /app

# Copy the build output from the previous stage
COPY --from=build /app/out ./

# Set the entry point for the container
ENTRYPOINT ["dotnet", "CareerShotApi.dll"]
