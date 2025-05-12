# Use the official .NET 9.0 SDK image for building
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy the project file and restore dependencies
COPY ["Vehicle-Data.csproj", "./"]
RUN dotnet restore

# Copy the rest of the source code
COPY . .

# Create Data directory and copy CSV file
RUN mkdir -p /src/Data
COPY Data/archive/sample-vin-data-20250512172431.csv /src/Data/sample-vin-data.csv

# Build the application
RUN dotnet build -c Release -o /app/build

# Publish the application
FROM build AS publish
RUN dotnet publish -c Release -o /app/publish

# Use the official .NET 9.0 runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Copy the published application
COPY --from=publish /app/publish .

# Create Data directory in the final image
RUN mkdir -p /app/Data

# Copy the CSV file to the final image
COPY --from=build /src/Data/sample-vin-data.csv /app/Data/

# Set environment variables
ENV ASPNETCORE_URLS=http://+:80
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_HTTP_PORTS=80

# Expose port 80
EXPOSE 80

# Start the application
ENTRYPOINT ["dotnet", "Vehicle-Data.dll"] 