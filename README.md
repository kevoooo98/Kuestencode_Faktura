# Invoice App

Eine moderne Rechnungsverwaltungsanwendung gebaut mit Blazor Server und MudBlazor.

## Technologie-Stack

- **.NET 9** - Neueste .NET Version
- **Blazor Server** - Interaktive Web-UI mit C#
- **MudBlazor** - Material Design UI-Framework
- **PostgreSQL** - Relationale Datenbank
- **Entity Framework Core 9** - ORM für Datenbankzugriff
- **Docker Compose** - Lokales Development Setup

## Erste Schritte

### Voraussetzungen

- .NET 9 SDK
- Docker Desktop
- IDE (Visual Studio 2022, VS Code oder Rider)

### Datenbank starten

```bash
cd InvoiceApp
docker-compose up -d
```

Dies startet eine PostgreSQL 16 Datenbank auf Port 5432.

### Datenbank Migrationen

Erste Migration erstellen:

```bash
dotnet ef migrations add InitialCreate
dotnet ef database update
```

### Anwendung starten

```bash
dotnet restore
dotnet run
```

Die Anwendung ist dann verfügbar unter: `https://localhost:5001` oder `http://localhost:5000`

## Projektstruktur

```
InvoiceApp/
├── Data/                    # DbContext und Datenbank-Konfiguration
├── Models/                  # Datenmodelle
├── Services/               # Business-Logik Services
├── Pages/                  # Razor Pages
├── Shared/                 # Wiederverwendbare Komponenten
├── wwwroot/               # Statische Dateien
├── Program.cs             # Anwendungs-Einstiegspunkt
└── docker-compose.yml     # PostgreSQL Container
```

## Entwicklung

### Datenbank-Verbindung

Die Connection-String ist in `appsettings.json` konfiguriert:

```json
"ConnectionStrings": {
  "DefaultConnection": "Host=localhost;Port=5432;Database=invoiceapp_dev;Username=postgres;Password=dev_password"
}
```

### MudBlazor

MudBlazor ist bereits konfiguriert. Die Dokumentation finden Sie unter: https://mudblazor.com/

## Docker Commands

Datenbank starten:
```bash
docker-compose up -d
```

Datenbank stoppen:
```bash
docker-compose down
```

Datenbank zurücksetzen (ACHTUNG: Löscht alle Daten):
```bash
docker-compose down -v
docker-compose up -d
```

## Lizenz

Privates Projekt
