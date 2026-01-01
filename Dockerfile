# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Install EF Core tools for migrations
RUN dotnet tool install --global dotnet-ef

# Add dotnet tools to PATH
ENV PATH="${PATH}:/root/.dotnet/tools"

# Copy csproj and restore dependencies
COPY ["InvoiceApp.csproj", "./"]
RUN dotnet restore "InvoiceApp.csproj"

# Copy everything else and build
COPY . .
RUN dotnet build "InvoiceApp.csproj" -c Release -o /app/build

# Create initial migration if Migrations folder doesn't exist
RUN if [ ! -d "Migrations" ]; then \
        dotnet ef migrations add InitialCreate --output-dir Migrations || true; \
    fi

# Publish Stage
FROM build AS publish
RUN dotnet publish "InvoiceApp.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Copy published app
COPY --from=publish /app/publish .

# Create directory for data protection keys
RUN mkdir -p /app/data/keys

ENTRYPOINT ["dotnet", "InvoiceApp.dll"]