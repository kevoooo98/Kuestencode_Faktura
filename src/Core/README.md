# Kuestencode.Core

Kern-Bibliothek mit wiederverwendbaren Modellen, Interfaces und Services für alle Kuestencode-Module.

## Features

- **Models**: Grundlegende Entitäten wie `Company`, `Customer`, `Address`, `BankAccount`
- **Interfaces**: Abstrakte Service-Definitionen für `IEmailService`, etc.
- **Validation**: Wiederverwendbare Validierungsattribute (IBAN, PLZ, Kundennummer)
- **Services**: Generische Implementierungen wie `CoreEmailService`
- **Enums**: Gemeinsame Aufzählungen wie `Country`, `PaymentMethod`

## Installation

```xml
<ProjectReference Include="..\Core\Kuestencode.Core.csproj" />
```

## Verwendung

### Service-Registrierung

```csharp
using Kuestencode.Core.Extensions;

// In Program.cs oder Startup.cs
builder.Services.AddKuestencodeCore<YourCompanyService>();
```

### Models verwenden

```csharp
using Kuestencode.Core.Models;

var company = new Company
{
    OwnerFullName = "Max Mustermann",
    Email = "max@example.com",
    // ...
};
```

### Validation Attributes

```csharp
using Kuestencode.Core.Validation;

public class MyModel
{
    [Iban]
    public string BankAccount { get; set; }

    [GermanPostalCode]
    public string PostalCode { get; set; }

    [FullName]
    public string Name { get; set; }
}
```

## Architektur

```
Kuestencode.Core/
├── Models/           # POCO-Klassen ohne UI-Abhängigkeiten
├── Interfaces/       # Service-Abstraktionen
├── Services/         # Generische Service-Implementierungen
├── Validation/       # DataAnnotation-Attribute
├── Enums/            # Aufzählungstypen
└── Extensions/       # DI-Erweiterungen
```

## Abhängigkeiten

- .NET 9.0
- MailKit (E-Mail-Versand)
- System.ComponentModel.Annotations (Validierung)
- Microsoft.Extensions.Logging.Abstractions
- Microsoft.Extensions.DependencyInjection.Abstractions

## Wichtig

- **Keine UI-Abhängigkeiten**: Dieses Projekt enthält keine Blazor-, WPF- oder andere UI-Komponenten
- **Keine Datenbank-Abhängigkeiten**: Entity Framework etc. wird von konsumierenden Projekten bereitgestellt
- **Erweiterbar**: Interfaces erlauben modulspezifische Implementierungen

## Migration von InvoiceApp

Bei der Migration von `InvoiceApp.Models` zu `Kuestencode.Core.Models`:

```csharp
// Vorher
using InvoiceApp.Models;
using InvoiceApp.Validation;

// Nachher
using Kuestencode.Core.Models;
using Kuestencode.Core.Validation;
```
