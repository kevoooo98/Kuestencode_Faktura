# Kuestencode Architektur

## Modulare Architektur-Übersicht

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                          Kuestencode Werkbank                                 │
├──────────────────────────────────────────────────────────────────────────────┤
│                                                                               │
│  ┌───────────────────┐  ┌───────────────────┐  ┌──────────────────────────┐  │
│  │  Kuestencode.Core │  │Kuestencode.Shared │  │  Kuestencode.Shared.*    │  │
│  │                   │  │       .UI         │  │                          │  │
│  │  • Models         │  │                   │  │  • Contracts (DTOs)      │  │
│  │  • Interfaces     │  │  • Components     │  │  • ApiClients            │  │
│  │  • Validation     │  │  • Layouts        │  │  • Pdf                   │  │
│  │  • Enums          │  │  • Theme          │  │                          │  │
│  │  • Core Services  │  │  • CSS/JS         │  │                          │  │
│  └───────────────────┘  └───────────────────┘  └──────────────────────────┘  │
│           │                      │                          │                 │
│           └──────────────────────┼──────────────────────────┘                 │
│                                  │                                            │
│                                  ▼                                            │
│  ┌────────────────────────────────────────────────────────────────────────┐  │
│  │                              Host                                       │  │
│  │  • Gateway / Reverse Proxy (YARP)                                      │  │
│  │  • Kundenverwaltung                                                    │  │
│  │  • Firmenstammdaten                                                    │  │
│  │  • E-Mail-Konfiguration                                                │  │
│  └────────────────────────────────────────────────────────────────────────┘  │
│                                  │                                            │
│    ┌───────────┬───────────┬───────────┬───────────┬───────────┬───────────┐  │
│    ▼           ▼           ▼           ▼           ▼           ▼           │  │
│  ┌───────────┐┌───────────┐┌───────────┐┌───────────┐┌───────────┐┌───────┐│  │
│  │  Faktura  ││  Offerte  ││  Rapport  ││   Acta    ││  Recepta  ││ Saldo ││  │
│  │           ││           ││           ││           ││           ││       ││  │
│  │• Rechnung.││• Angebote ││• Zeiterfas││• Projekte ││• Eingangs-││• EÜR  ││  │
│  │• PDF/XR.  ││• PDF-Exp. ││• PDF/CSV  ││• Aufgaben ││  rechnung.││• DATEV││  │
│  │• E-Mail   ││• E-Mail   ││• Timer    ││• State M. ││• XRechn.  ││• PDF  ││  │
│  │• GiroCode ││• → Faktura││• → Faktura││• ↔ Rapport││• OCR      ││• SKR  ││  │
│  └───────────┘└───────────┘└───────────┘└───────────┘└───────────┘└───────┘│  │
│                                                                               │
└──────────────────────────────────────────────────────────────────────────────┘
```

## Projekt-Abhängigkeiten

```
Kuestencode.Core (Keine Abhängigkeiten - Basis-Bibliothek)
       │
       ├──────────────────────────────────────┐
       ▼                                      ▼
Kuestencode.Shared.UI              Kuestencode.Shared.Contracts
       │                                      │
       └──────────────┬───────────────────────┘
                      │
       ┌──────────────┼──────────────┬──────────────┬──────────────┬──────────────┐
       ▼              ▼              ▼              ▼              ▼              ▼
   Faktura        Offerte        Rapport         Acta         Recepta         Saldo
```

## Schichten-Architektur

### Kuestencode.Core

Die Core-Bibliothek enthält **keine UI- oder Datenbank-Abhängigkeiten** und ist rein als Klassenbibliothek konzipiert.

```
Kuestencode.Core/
├── Models/               # POCO-Klassen
│   ├── BaseEntity.cs     # Basis mit Id, CreatedAt, UpdatedAt
│   ├── Company.cs        # Firmenstammdaten
│   ├── Customer.cs       # Kundenstammdaten
│   ├── Address.cs        # Adress-Value-Object
│   ├── BankAccount.cs    # Bankverbindung
│   └── SmtpConfiguration.cs
│
├── Interfaces/           # Abstraktionen
│   ├── ICompanyService.cs
│   ├── ICustomerService.cs
│   ├── IEmailEngine.cs
│   ├── IPdfEngine.cs
│   └── IDocumentService.cs
│
├── Services/             # Generische Implementierungen
│   └── CoreEmailService.cs
│
├── Validation/           # DataAnnotations
│   ├── IbanAttribute.cs
│   ├── GermanPostalCodeAttribute.cs
│   ├── FullNameAttribute.cs
│   └── CustomerNumberAttribute.cs
│
├── Enums/
│   ├── Country.cs
│   └── PaymentMethod.cs
│
└── Extensions/
    └── ServiceCollectionExtensions.cs
```

### Kuestencode.Shared.UI

Die Shared.UI-Bibliothek enthält **wiederverwendbare Blazor-Komponenten** und das gemeinsame Theme.

```
Kuestencode.Shared.UI/
├── Components/           # Blazor-Komponenten
│   ├── CustomerPicker.razor
│   ├── AddressForm.razor
│   ├── EmailComposer.razor
│   └── ConfirmDialog.razor
│
├── Layouts/
│   └── ModuleLayout.razor
│
├── wwwroot/
│   ├── css/shared.css
│   └── js/shared.js
│
├── KuestenCodeTheme.cs   # MudBlazor Theme
└── _Imports.razor
```

### Kuestencode.Faktura

Das Faktura-Modul ist eine **vollständige Blazor Server Anwendung** mit eigener Datenbank.

```
Kuestencode.Faktura/
├── Data/
│   ├── ApplicationDbContext.cs
│   └── Repositories/
│       ├── Repository<T>.cs
│       ├── CustomerRepository.cs
│       └── InvoiceRepository.cs
│
├── Models/               # Faktura-spezifische Models
│   ├── Invoice.cs
│   ├── InvoiceItem.cs
│   ├── DownPayment.cs
│   ├── InvoiceStatus.cs
│   ├── DiscountType.cs
│   ├── EmailAttachmentFormat.cs
│   ├── EmailLayout.cs
│   └── PdfLayout.cs
│
├── Services/
│   ├── CompanyService.cs    # ICompanyService Implementierung
│   ├── CustomerService.cs   # ICustomerService Implementierung
│   ├── InvoiceService.cs
│   ├── XRechnungService.cs
│   ├── DashboardService.cs
│   ├── PreviewService.cs
│   ├── Email/
│   │   ├── InvoiceEmailService.cs
│   │   ├── EmailTemplateRenderer.cs
│   │   └── ...
│   └── Pdf/
│       ├── PdfGeneratorService.cs
│       ├── PdfTemplateEngine.cs
│       └── Layouts/
│
├── Pages/
│   ├── Index.razor
│   ├── Invoices/
│   ├── Customers/
│   └── Settings/
│
├── Shared/
│   ├── MainLayout.razor
│   ├── NavMenu.razor
│   └── ...
│
├── FakturaModule.cs      # Service-Registrierung
├── Program.cs            # Entry Point
└── appsettings.json
```

## Design-Prinzipien

### 1. Separation of Concerns

- **Core**: Reine Business-Logik und Datenmodelle
- **Shared.UI**: Wiederverwendbare UI-Komponenten
- **Module**: Feature-spezifische Implementierungen

### 2. Dependency Inversion

Module implementieren Core-Interfaces:

```csharp
// Core definiert
public interface ICompanyService { ... }

// Faktura implementiert
public class CompanyService : ICompanyService { ... }
```

### 3. Open/Closed Principle

Neue Module können hinzugefügt werden, ohne Core zu ändern:

```csharp
// Neues Modul registriert sich selbst
builder.Services.AddFakturaModule(configuration);
builder.Services.AddCrmModule(configuration);  // Zukünftig
```

### 4. Single Responsibility

Jede Schicht hat eine klare Verantwortung:

| Schicht | Verantwortung |
|---------|---------------|
| Core | Datenmodelle, Interfaces, Validierung |
| Shared.UI | UI-Komponenten, Theme, Assets |
| Host | Gateway, Kundenverwaltung, Firmenstammdaten |
| Faktura | Rechnungsverwaltung, PDF, XRechnung |
| Offerte | Angebotsverwaltung, PDF, E-Mail |
| Rapport | Zeiterfassung, Tätigkeitsnachweise |
| Acta | Projektverwaltung, Aufgabenmanagement |
| Recepta | Eingangsrechnungen, XRechnung/ZUGFeRD, OCR |
| Saldo | EÜR, DATEV-Export, Kontenrahmen SKR03/SKR04 |

## Erweiterbarkeit

### Neues Modul hinzufügen

1. Neues Projekt erstellen: `Kuestencode.{ModulName}`
2. Referenzen zu Core und Shared.UI hinzufügen
3. `{ModulName}Module.cs` mit Service-Registrierung erstellen
4. Modul in Solution hinzufügen

```csharp
// Beispiel: CRM-Modul
public static class CrmModule
{
    public static IServiceCollection AddCrmModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<CrmDbContext>(...);
        services.AddScoped<IContactService, ContactService>();
        return services;
    }
}
```

### Shared Components erweitern

1. Neue Komponente in Shared.UI hinzufügen
2. In `_Imports.razor` exportieren
3. In allen Modulen nutzbar

## Datenbank-Strategie

### Option A: Separate Datenbanken pro Modul (empfohlen für große Systeme)

```yaml
services:
  postgres_faktura:
    image: postgres:16
    environment:
      POSTGRES_DB: faktura

  postgres_crm:
    image: postgres:16
    environment:
      POSTGRES_DB: crm
```

### Option B: Shared Database mit Schema-Trennung (aktuell)

```csharp
// Faktura DbContext
modelBuilder.HasDefaultSchema("faktura");

// CRM DbContext (zukünftig)
modelBuilder.HasDefaultSchema("crm");
```

## Testing-Strategie

```
tests/
├── Kuestencode.Core.Tests/         # Unit Tests für Core
│   ├── ValidationTests.cs
│   └── ModelTests.cs
│
├── Kuestencode.Faktura.Tests/      # Integration Tests für Faktura
│   ├── InvoiceServiceTests.cs
│   └── XRechnungServiceTests.cs
│
└── Kuestencode.E2E.Tests/          # End-to-End Tests (optional)
    └── InvoiceWorkflowTests.cs
```

## Deployment

### Docker Compose (Entwicklung)

```bash
docker-compose -f docker-compose.modular.yml up -d
```

### Kubernetes (Produktion)

Jedes Modul kann als separater Microservice deployed werden:

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: kuestencode-faktura
spec:
  template:
    spec:
      containers:
        - name: faktura
          image: kuestencode/faktura:latest
```


## Module

### Kuestencode.Faktura

- Rechnungserstellung und -verwaltung
- PDF-Generierung mit anpassbaren Layouts
- XRechnung/ZUGFeRD nach EN16931-Standard
- GiroCode-QR-Codes für SEPA-Überweisungen
- E-Mail-Versand mit konfigurierbaren Templates
- DB-Schema: `faktura`

### Kuestencode.Offerte

- Angebotserstellung und -verwaltung
- Statusworkflow (Entwurf → Versendet → Angenommen/Abgelehnt)
- PDF-Generierung mit anpassbaren Layouts
- E-Mail-Versand mit HTML-Templates (Klar, Strukturiert, Betont)
- Integration mit Faktura (Angebot → Rechnung)
- DB-Schema: `offerte`

### Kuestencode.Rapport

- Zeiterfassung (Timer + manuelle Einträge)
- Kunden- und Projektzuordnung
- PDF/CSV Export (Tätigkeitsnachweis)
- Einstellungen inkl. Live-PDF-Vorschau
- Integration mit Faktura (Zeiteinträge an Rechnungen)
- DB-Schema: `rapport`

### Kuestencode.Acta

- Projektverwaltung mit State Machine (Draft → Active → Paused → Completed → Archived)
- Aufgabenmanagement pro Projekt (CRUD, Sortierung, Zuweisung)
- Automatische Projektnummern (P-YYYY-NNNN)
- Budget-Tracking und Fortschrittsanzeige
- Cross-Modul-Integration mit Rapport (Zeiterfassung auf Projekte, Aufwand-Karte)
- 3-Schichten-Architektur: Domain, Data, Application
- DB-Schema: `acta`

### Kuestencode.Recepta

- Eingangsrechnungsverwaltung (CRUD, Statusworkflow: Draft → Booked → Paid)
- XRechnung/ZUGFeRD-Import (XML direkt, ZUGFeRD-PDF mit eingebettetem XML)
- OCR-Texterkennung (Tesseract) mit selbstlernendem Pattern-Matching pro Lieferant
- Lieferantenverwaltung mit USt-ID/IBAN-basiertem Auto-Matching
- Dateianhänge auf Dateisystem (PDF, JPG, PNG)
- Kategorisierung (Material, Subcontractor, Office, Travel, Other)
- Cross-Modul-Integration mit Acta (Projektzuordnung, Kostenauswertung)
- 3-Schichten-Architektur: Domain, Data, Application
- DB-Schema: `recepta`

### Kuestencode.Werkbank.Saldo

- EÜR nach § 4 Abs. 3 EStG (Zufluss-/Abflussprinzip, Filterung nach `PaidDate`)
- Aggregation aus Faktura (Einnahmen) und Recepta (Ausgaben) via HTTP-API
- Kontenrahmen SKR03/SKR04 konfigurierbar; Kategorie-Mapping Recepta → DATEV-Konto
- DATEV EXTF-Export: Buchungsstapel (Windows-1252, BU-Schlüssel, Semikolon-Trennzeichen)
- Belege-ZIP-Export: Rechnungs-PDFs + Recepta-Anhänge in einem Archiv
- PDF-Report: EÜR-Bericht mit QuestPDF (Deckblatt, Zusammenfassung, Detailtabellen)
- 3-Schichten-Architektur: Domain, Data, Application
- DB-Schema: `saldo`

## Tests

- `tests/Kuestencode.Faktura.Tests` (Unit/Integration)
- `tests/Kuestencode.Offerte.Tests` (Unit/Integration)
- `tests/Kuestencode.Rapport.IntegrationTests` (Integration)
- `src/Modules/Saldo/Kuestencode.Werkbank.Saldo.Tests` (Unit)
