# Küstencode Werkbank

Küstencode Werkbank ist eine selbst gehostete, modulare Business-Suite für Rechnungsstellung, Buchhaltung, Zeiterfassung und Projektmanagement – für Selbstständige und kleine Unternehmen, die ihre Geschäftsdaten unter eigener Kontrolle behalten wollen, statt sie einem SaaS-Anbieter anzuvertrauen.

## Was ist Werkbank?

Werkbank ist **kein CRM** – es gibt keine Vertriebspipeline und kein Lead-Management. Stattdessen deckt es den kompletten kaufmännischen Alltag eines Kleinunternehmens ab: Angebote schreiben, Rechnungen stellen, Zeiten erfassen, Projekte steuern, Eingangsrechnungen verarbeiten und die Buchhaltung (EÜR, DATEV) führen.

**Was „modular" konkret bedeutet:** Jedes Modul läuft als eigenständiger Docker-Container mit eigenem Datenbank-Schema. Man installiert nur die Module, die man tatsächlich braucht – z.B. nur Faktura, ohne Rapport oder Saldo. Der Host erkennt per Health-Check automatisch, welche Module laufen, und zeigt in Navigation und Dashboard nur die verfügbaren Module an. Cross-Modul-Funktionen (z.B. Zeiten aus Rapport an eine Rechnung in Faktura anhängen) laufen über definierte HTTP-Schnittstellen, nicht über direkten Datenbankzugriff zwischen Modulen.

**Warum self-hosted:**
- Läuft komplett auf eigener Hardware (NAS, Homeserver, eigener Server) – keine Cloud-Pflicht, kein Abo
- Volle Datenhoheit, insbesondere relevant für DSGVO-sensible Geschäftsdaten
- Open Source (MIT-Lizenz), Docker-basiert, in .NET/Blazor entwickelt
- Fokus auf deutsche Anforderungen: E-Rechnung (XRechnung/ZUGFeRD), EÜR nach § 4 Abs. 3 EStG, DATEV-Export

## Module

### Faktura

Rechnungsprogramm mit Fokus auf saubere Fakturierung und E-Rechnungen.

**Features:**
- Rechnungen erstellen, verwalten und verfolgen (Entwurf, Versendet, Bezahlt, Überfällig)
- PDF-Rechnungen mit anpassbaren Layouts
- E-Rechnungen nach EN16931-Standard (XRechnung, ZUGFeRD)
- GiroCode-QR-Codes für SEPA-Überweisungen
- Kundenverwaltung mit Adressdaten
- E-Mail-Versand mit konfigurierbaren Templates
- Dashboard mit Übersicht offener und überfälliger Rechnungen
- Anzahlungsrechnungen und Abschlagsrechnungen

### Offerte

Angebotserstellung und -verwaltung für professionelle Kundenakquise.

**Features:**
- Angebote erstellen, bearbeiten und verwalten
- Flexibler Statusworkflow (Entwurf → Versendet → Angenommen/Abgelehnt)
- PDF-Angebote mit anpassbaren Layouts und Farbschemata
- E-Mail-Versand mit konfigurierbaren HTML-Templates (Klar, Strukturiert, Betont)
- Gültigkeitsdatum-Tracking
- Angebot direkt in Rechnung umwandeln (Faktura-Integration)

### Rapport

Zeiterfassung und Tätigkeitsdokumentation für projektbasiertes Arbeiten.

**Features:**
- Timer-basierte Zeiterfassung mit Kunden- und Projektzuordnung
- Manuelle Zeiteinträge erstellen und bearbeiten
- Tätigkeitsnachweise als PDF und CSV exportieren
- Einstellungen mit Live-Vorschau für PDF-Layout
- Integration mit Faktura (Tätigkeiten an Rechnungen anhängen)

### Acta

Projektverwaltung und Aufgabenmanagement für strukturiertes Arbeiten.

**Features:**
- Projekte erstellen, bearbeiten und verwalten
- Statusworkflow mit State Machine (Entwurf → Aktiv → Pausiert → Abgeschlossen → Archiviert)
- Aufgabenverwaltung pro Projekt mit Sortierung und Zuweisung
- Automatische Projektnummern-Vergabe (P-YYYY-NNNN)
- Budget-Tracking und Fortschrittsanzeige
- Cross-Modul-Integration mit Rapport (Zeiterfassung auf Projekte buchen)

### Recepta

Eingangsrechnungsverwaltung mit intelligenter Dokumenterkennung.

**Features:**
- Eingangsrechnungen erfassen, verwalten und verfolgen (Entwurf, Gebucht, Bezahlt)
- XRechnung/ZUGFeRD-Import (XML und ZUGFeRD-PDF) mit 100% strukturierter Datenübernahme
- OCR-Texterkennung mit selbstlernendem Musterabgleich pro Lieferant
- 3-Phasen-Scan-Workflow: Upload → Analyse → Formular
- Lieferantenverwaltung mit USt-ID, IBAN und automatischem Matching
- Dateianhänge mit Vorschau (PDF, JPG, PNG)
- Kategorisierung (Material, Fremdleistung, Büro, Reise, Sonstig)
- Cross-Modul-Integration mit Acta (Belege auf Projekte buchen, Kostenauswertung)

### Saldo

Finanzbuchhaltung mit Einnahmen-Überschuss-Rechnung (EÜR) und DATEV-Export.

**Features:**
- EÜR nach § 4 Abs. 3 EStG (Zufluss-/Abflussprinzip nach Zahlungsdatum)
- Dashboard mit Einnahmen, Ausgaben und Gewinn für beliebige Zeiträume
- Buchungsübersicht mit USt-Aufschlüsselung
- Kategorie-Mapping: Recepta-Kategorien auf DATEV-Konten (SKR03/SKR04), individuell überschreibbar
- DATEV-Export: Buchungsstapel im EXTF-Format (Windows-1252, BU-Schlüssel)
- Belege-ZIP-Export: PDFs aus Faktura und Recepta gesammelt
- PDF-Report: EÜR-Bericht mit Deckblatt, Zusammenfassung und Detailtabellen
- Integration mit Faktura (Einnahmen) und Recepta (Ausgaben)

## Technologie-Stack

| Bereich | Technologie |
|---------|-------------|
| Framework | .NET 9, Blazor Server |
| UI | MudBlazor 8.0 (Material Design) |
| Datenbank | PostgreSQL 16, Entity Framework Core 9 |
| PDF | QuestPDF, iText7 (ZUGFeRD), ZUGFeRD-csharp (Recepta) |
| E-Mail | MailKit, MimeKit |
| Container | Docker, Docker Compose |

## Installation

Küstencode Werkbank wird als Docker-Compose-Stack betrieben (z.B. auf einem NAS oder Server im eigenen Netzwerk).

### Voraussetzungen

- Docker
- Docker Compose

### Schnellstart

```bash
git clone https://github.com/kuesten-code/Werkbank.git
cd Werkbank
docker compose up -d
```

Die Anwendung ist dann erreichbar unter:
- **Host/Übersicht:** http://localhost:8080
- **Faktura:** http://localhost:8080/faktura
- **Offerte:** http://localhost:8080/offerte
- **Rapport:** http://localhost:8080/rapport
- **Acta:** http://localhost:8080/acta
- **Recepta:** http://localhost:8080/recepta
- **Saldo:** http://localhost:8080/saldo

### Produktions-Deployment

Für Produktionsumgebungen liegt ein fertiger Stack im aktuellen Release. 
Dieser muss heruntergeladen und entpackt werden. 
Dann die setup shell (Linux) oder die Powershell (Windows) ausführen.

## Entwicklung

### Lokale Entwicklungsumgebung

```bash
# PostgreSQL starten
docker compose up -d postgres

# Host-Anwendung starten
cd src/Host
dotnet run
```

### Projektstruktur

```
src/
├── Core/                    # Gemeinsame Models, Interfaces, Validierung
├── Shared.UI/               # Wiederverwendbare Blazor-Komponenten
├── Host/                    # Hauptanwendung mit Dashboard und Kundenverwaltung
├── Modules/
│   ├── Faktura/             # Rechnungsmodul
│   ├── Offerte/             # Angebotsmodul
│   ├── Rapport/             # Zeiterfassungsmodul
│   ├── Acta/                # Projektverwaltungsmodul
│   ├── Recepta/             # Eingangsrechnungsmodul
│   └── Saldo/               # EÜR und DATEV-Export
├── Kuestencode.Shared.Contracts/    # DTOs für Modul-Kommunikation
└── Kuestencode.Shared.ApiClients/   # HTTP-Clients für API-Aufrufe
```

## Architektur

Küstencode Werkbank folgt einer modularen Microservice-Architektur:

- **Host** agiert als zentrales Gateway mit Reverse-Proxy (Yarp)
- **Module** laufen als eigenständige Services mit eigenen Datenbank-Schemas – jedes Modul einzeln startbar/abschaltbar, unabhängig von den anderen
- **Erkennung** der laufenden Module durch den Host per Health-Check, keine feste Konfiguration nötig
- **Kommunikation** erfolgt über REST-APIs und geteilte Contracts
- **Deployment** Als Container (Produktion)

Detaillierte Dokumentation: [ARCHITECTURE.md](ARCHITECTURE.md)

## Lizenz

MIT License – Copyright 2026 Kevin Schulze
