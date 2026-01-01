# Küstencode Faktura

Eine professionelle Rechnungsverwaltungsanwendung mit umfassenden Features für Rechnungserstellung, Kundenverwaltung und XRechnung-Export.

## Funktionsumfang

- **Rechnungsverwaltung** - Erstellen, bearbeiten und verwalten von Rechnungen
- **Kundenverwaltung** - Vollständige Verwaltung von Kundendaten
- **Firmenprofil** - Konfigurierbare Firmeninformationen
- **PDF-Generierung** - Automatische PDF-Erstellung mit QuestPDF und iText7
- **XRechnung-Export** - Standardkonforme XRechnung-Dateien für B2B und Behörden
- **E-Mail-Versand** - Rechnungsversand per E-Mail (MailKit)
- **QR-Code-Integration** - QR-Codes auf Rechnungen
- **Dashboard** - Übersicht über offene und überfällige Rechnungen
- **Vorschau-System** - Live-Vorschau von Rechnungen vor dem Export

## Technologie-Stack

### Backend
- **.NET 9** - Neueste .NET Version
- **Blazor Server** - Interaktive Web-UI mit C#
- **Entity Framework Core 9** - ORM für Datenbankzugriff
- **PostgreSQL 16** - Relationale Datenbank

### UI Framework
- **MudBlazor 8.0** - Material Design UI-Framework

### PDF & Dokumente
- **QuestPDF 2025.12.0** - Moderne PDF-Generierung
- **iText7 8.0.5** - PDF-Verarbeitung
- **QRCoder 1.7.0** - QR-Code-Generierung
- **SkiaSharp 3.116.1** - Grafikverarbeitung

### E-Mail
- **MailKit 4.9.0** - E-Mail-Versand
- **MimeKit 4.9.0** - MIME-Nachrichtenverarbeitung

### Deployment
- **Docker & Docker Compose** - Containerisierung
- **Npgsql 9.0.2** - PostgreSQL-Provider für EF Core

## Erste Schritte

### Voraussetzungen

- .NET 9 SDK
- Docker Desktop
- IDE (Visual Studio 2022, VS Code oder Rider)

### Installation mit Docker (Empfohlen)

1. **Komplettes System starten**
   ```bash
   docker-compose up -d
   ```

   Dies startet:
   - PostgreSQL Datenbank auf Port 5432
   - Blazor-Anwendung auf Port 8080

   **Wichtig:** Beim ersten Start werden automatisch alle Datenbankmigrationen angewendet. Dies kann einige Sekunden dauern.

2. **Anwendung öffnen**

   Öffnen Sie im Browser: `http://localhost:8080`

   **Hinweis:** Wenn die Anwendung noch nicht erreichbar ist, warten Sie kurz bis die Migrationen abgeschlossen sind. Sie können den Fortschritt mit `docker-compose logs -f invoiceapp` verfolgen.

### Lokale Entwicklung

1. **Nur Datenbank starten**
   ```bash
   docker-compose up -d postgres
   ```

2. **Datenbank Migrationen anwenden**
   ```bash
   dotnet ef database update
   ```

3. **Anwendung starten**
   ```bash
   dotnet restore
   dotnet run
   ```

Die Anwendung ist verfügbar unter: `https://localhost:5001` oder `http://localhost:5000`

## Projektstruktur

```
K-stencode_Faktura/
├── Data/                       # DbContext und Datenbank-Konfiguration
│   └── ApplicationDbContext.cs
├── Models/                     # Datenmodelle
│   ├── Invoice.cs             # Rechnungsmodell
│   ├── Customer.cs            # Kundenmodell
│   └── Company.cs             # Firmenmodell
├── Services/                   # Business-Logik Services
│   ├── InvoiceService.cs      # Rechnungsverwaltung
│   ├── CustomerService.cs     # Kundenverwaltung
│   ├── CompanyService.cs      # Firmenverwaltung
│   ├── PdfGeneratorService.cs # PDF-Generierung
│   ├── XRechnungService.cs    # XRechnung-Export
│   ├── EmailService.cs        # E-Mail-Versand
│   ├── DashboardService.cs    # Dashboard-Daten
│   ├── PreviewService.cs      # Vorschau-System
│   └── InvoiceOverdueService.cs
├── Pages/                      # Razor Pages
│   └── Index.razor            # Hauptseite
├── Shared/                     # Wiederverwendbare Komponenten
│   ├── MainLayout.razor
│   └── NavMenu.razor
├── Validation/                 # Validierungslogik
├── wwwroot/                    # Statische Dateien
├── images/                     # Bilder und Assets
├── Program.cs                  # Anwendungs-Einstiegspunkt
├── docker-compose.yml          # Multi-Container-Setup
├── Dockerfile                  # Container-Image
└── InvoiceApp.csproj          # Projektdatei
```

## Konfiguration

### Datenbank-Verbindung

Die Connection-String ist in [appsettings.json](appsettings.json) konfiguriert:

```json
"ConnectionStrings": {
  "DefaultConnection": "Host=localhost;Port=5432;Database=invoiceapp_dev;Username=postgres;Password=dev_password"
}
```

Für Docker-Deployment wird die Connection-String über Umgebungsvariablen gesetzt.

### E-Mail-Konfiguration

E-Mail-Einstellungen werden in der Datenbank pro Firma gespeichert.

## Docker Commands

**Gesamtes System starten:**
```bash
docker-compose up -d
```

**Nur Datenbank starten:**
```bash
docker-compose up -d postgres
```

**Logs anzeigen:**
```bash
docker-compose logs -f
```

**System stoppen:**
```bash
docker-compose down
```

**System zurücksetzen (ACHTUNG: Löscht alle Daten):**
```bash
docker-compose down -v
docker-compose up -d
```

**Neu bauen nach Code-Änderungen:**
```bash
docker-compose up -d --build
```

## Entwicklung

### Entity Framework Migrationen

**Automatische Migrationen (Docker):**

Bei Verwendung von Docker werden Migrationen automatisch beim Start des Containers angewendet:
- Beim Build wird automatisch eine InitialCreate-Migration erstellt, falls noch keine Migrationen vorhanden sind
- Beim Start der Anwendung wird `Database.Migrate()` ausgeführt, was alle ausstehenden Migrationen anwendet
- Dies funktioniert sowohl beim ersten Start als auch bei Updates

**Manuelle Migrationen (Lokale Entwicklung):**

**Neue Migration erstellen:**
```bash
dotnet ef migrations add <MigrationName>
```

**Migration anwenden:**
```bash
dotnet ef database update
```

**Migration rückgängig machen:**
```bash
dotnet ef database update <PreviousMigrationName>
```

**Hinweis:** Für lokale Entwicklung müssen Sie sicherstellen, dass die dotnet-ef Tools installiert sind:
```bash
dotnet tool install --global dotnet-ef
```

### Nützliche Links

- [MudBlazor Dokumentation](https://mudblazor.com/)
- [QuestPDF Dokumentation](https://www.questpdf.com/)
- [XRechnung Standard](https://www.xoev.de/xrechnung)
- [Blazor Dokumentation](https://learn.microsoft.com/de-de/aspnet/core/blazor/)

## Lizenz

Privates Projekt
