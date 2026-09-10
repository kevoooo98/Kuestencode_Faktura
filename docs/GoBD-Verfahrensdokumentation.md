# GoBD-Verfahrensdokumentation — Küstencode Werkbank

## 1. Zweck und Geltungsbereich

Dieses Dokument beschreibt, wie die Module **Faktura** (Rechnungsstellung), **Recepta** (Belegverwaltung) und **Saldo** (EÜR/DATEV) die "Grundsätze zur ordnungsmäßigen Führung und Aufbewahrung von Büchern, Aufzeichnungen und Unterlagen in elektronischer Form" (GoBD) technisch umsetzen: Nummernkreis-Vergabe, Unveränderbarkeit und Korrekturwege, PDF-Aufbewahrung, Änderungsprotokollierung sowie Historisierung der Kontenrahmen-Zuordnung.

**Nicht Teil dieses Dokuments / dieser Umsetzung:**
- GDPdU/IDEA-Datenexport (Z1/Z2/Z3) für den Datenzugriff eines Betriebsprüfers — separates Folgethema.
- Organisatorische Prozesse außerhalb der Software (z. B. wer im Unternehmen welche Rolle bedienen darf).

**Versionsstand dieses Dokuments:** siehe Abschnitt 9.

## 2. Nummernkreis-Vergabe (lückenlose Nummerierung)

Rechnungs-, Gutschrift- und Belegnummern werden über `NumberSequenceService` (Core) vergeben, nicht mehr durch In-Memory-Scan des höchsten bisherigen Werts:

- Je Schema (Faktura, Recepta) existiert eine Tabelle `NumberSequences` (`SequenceKey`, `CurrentValue`, `UpdatedAt`).
- Die nächste Nummer wird über `SELECT … FOR UPDATE` auf der Zähler-Zeile ermittelt — das sperrt die Zeile für die Dauer der Transaktion und macht das Hochzählen atomar, auch bei parallelen Anfragen.
- Der `SequenceKey` enthält Typ und das aus dem Nummernformat gerenderte Präfix (z. B. `Invoice:2026-`, `ER-2026-`) — bei jahresabhängigem Format (`YYYY` im Muster) beginnt der Zähler automatisch mit jedem neuen Jahr bei 1.
- Beim ersten Aufruf für einen neuen `SequenceKey` wird der Zähler einmalig aus den vorhandenen Nummern geseedet (Lazy Seeding), damit bestehende Installationen nahtlos weiterzählen.
- Betroffene Nummernkreise:
  - Faktura: Rechnungen (`InvoiceFormat`, Standard `YYYY-XXXX`), Gutschriften (`CreditNoteFormat`, Standard `GS-YYYY-XXXX`) — `faktura."NumberSequences"`.
  - Recepta: Eingangsrechnungen (`IncomingInvoiceFormat`, Standard `ER-YYYY-XXXX`) — `recepta."NumberSequences"`.
- Format ist je Installation über die Host-Einstellungen konfigurierbar; die Lückenlosigkeit gilt für das jeweils aktive Format.

**Bekannte Einschränkung:** Die Nummer wird bereits beim Öffnen des Erstellen-Formulars reserviert (Vorschau zeigt die nächste echte Nummer). Wird das Formular verlassen ohne zu speichern, entsteht eine Nummernlücke. Für Entwürfe, die nie versendet wurden, ist das unkritisch (keine GoBD-Relevanz vor Versand), wurde aber bewusst nicht behoben (siehe Abschnitt 8).

## 3. Unveränderbarkeit und Korrekturwege

### Faktura (Rechnungen)

- `Invoice.IsLocked` ist `true`, sobald `Status != Draft`. Entwürfe bleiben frei bearbeit- und löschbar.
- Serverseitig (nicht nur UI-Kosmetik): `PUT`/`DELETE` auf eine gesperrte Rechnung liefern `409 Conflict` mit Hinweis auf Gutschrift/Storno.
- Zwei Korrekturwege ab dem Zeitpunkt, an dem eine Rechnung versendet wurde (per E-Mail oder "Als gedruckt markieren"):
  - **Gutschrift**: erzeugt ein eigenständiges Korrekturdokument (negative Positionen), die Ursprungsrechnung bleibt unverändert bestehen.
  - **Storno** (`POST /api/invoice/{id}/cancel`): setzt `Status = Cancelled`, `CancelledAt`, optional `CancellationReason`. Der Datensatz und seine Nummer bleiben erhalten — die Nummer wird nicht erneut vergeben. Nicht möglich für Entwürfe (die werden gelöscht, nicht storniert) und nicht doppelt für bereits stornierte Rechnungen.

### Recepta (Eingangsbelege)

- `Document.IsEditable` ist nur `true` im Status `Draft`. Ab `Booked` sind Bearbeiten, Datei-Upload/-Löschung und OCR-Text-Änderung serverseitig gesperrt.
- Der Rücksprung `Booked → Draft` wurde entfernt (ehemalige Backdoor); damit greift auch der bereits vorhandene Löschschutz für gebuchte Belege zuverlässig.
- Löschen ist ausschließlich für echte Entwürfe möglich; dabei werden zugehörige Dateien mitgelöscht (keine Dateileichen).
- Ein eigenständiger "Storno" ist für Recepta nicht vorgesehen: eingehende Belege sind unveränderliche Fremdbelege (die Originalrechnung des Lieferanten). Korrekturen an der Verbuchung erfolgen über neue Zahlungen/Buchungen, niemals durch nachträgliches Verändern des ursprünglichen Belegs.

## 4. PDF-Aufbewahrung (Faktura)

Das PDF, das der Kunde tatsächlich erhalten hat, muss unveränderlich abrufbar bleiben — unabhängig von späteren Änderungen an Firmendaten, Logo oder Vorlage.

- `IPdfGeneratorService.FreezeSnapshotAsync(invoiceId)` rendert das PDF einmalig und speichert es als `InvoiceAttachment` mit `IsFrozenSnapshot = true` in der Datenbank (nicht im Dateisystem — liegt damit im gesicherten Postgres-Backup-Bereich).
- Auslösepunkte: erfolgreicher E-Mail-Versand (`EmailService`, nach bestätigtem Versand — ein fehlgeschlagener Versandversuch hinterlässt keinen Snapshot auf einer noch bearbeitbaren Rechnung) sowie `InvoiceService.MarkAsPrintedAsync` (Postversand).
- Idempotent: existiert bereits ein Snapshot für die Rechnung, wird kein zweiter erzeugt.
- Leseseite: PDF-Abruf (Anzeige, erneuter E-Mail-Versand, Druck) liefert ab dem Zeitpunkt des Einfrierens immer den gespeicherten Snapshot statt einer Live-Neuberechnung. Entwürfe zeigen weiterhin die Live-Vorschau.

## 5. Änderungsprotokollierung (Audit-Log)

Änderungen werden nicht punktuell aus einzelnen UI-Aktionen heraus protokolliert, sondern zentral über einen `SaveChanges`/`SaveChangesAsync`-Interceptor auf DbContext-Ebene (`AuditChangeCollector`, Core) — das erfasst jede Änderung lückenlos, unabhängig vom Aufrufer.

**Erfasste Felder je Entität (Whitelist, kein "alle Felder"-Rauschen):**

| Modul | Entität | Protokollierte Felder |
|---|---|---|
| Faktura | `Invoice` | Status, InvoiceDate, DueDate, Notes, CustomerId, DiscountType, DiscountValue, IsReverseCharge, CancelledAt, CancellationReason |
| Faktura | `InvoicePayment` | Amount, PaymentDate, Notes (nur Anlage/Löschung, Zahlungen werden nie geändert) |
| Recepta | `Document` | Status, InvoiceNumber, InvoiceDate, DueDate, AmountNet, AmountTax, AmountGross, SupplierId, Category, Notes, OcrRawText |
| Recepta | `DocumentPayment` | Amount, PaymentDate, Notes (nur Anlage/Löschung) |

Jeder Eintrag enthält: **wer** (`ChangedByUserId`/`ChangedByUserName`, aus dem JWT-Auth-Context via `ICurrentUserAccessor`), **wann** (`ChangedAt`), **was** (`FieldName`, `OldValue`, `NewValue`) und **welche Aktion** (`Created`/`Modified`/`Deleted`). Zahlungen erscheinen als vollständige Created/Deleted-Zeile mit sichtbarem Betrag (statt einer leeren Sammel-Zeile) und werden der zugehörigen Rechnung/dem Beleg zugeordnet, nicht der Zahlung selbst.

Abrufbar über:
- `GET /api/invoice/{id}/audit-log` (Faktura)
- `GET /api/recepta/documents/{id}/audit-log` (Recepta)

Beide werden auf der jeweiligen Detailseite als read-only Tab "Änderungshistorie" angezeigt.

## 6. Kontenrahmen-Historisierung und Exportnachweise (Saldo)

- **Zeitversionierte Kategorie-Overrides**: `KontoMappingOverride` trägt `GueltigAb`/`GueltigBis`. Eine Änderung schließt die bisher offene Version (setzt `GueltigBis`) und legt eine neue Version ab dem gewählten Datum an — es wird nichts überschrieben. Ein partieller Unique-Index in der Datenbank stellt sicher, dass pro Kontenrahmen+Kategorie höchstens eine offene Version existiert.
- Die EÜR-/DATEV-Berechnung (`AusgabenService.GetAusgabenAsync`) löst das anzuwendende Konto **zum Zahlungsdatum** jeder einzelnen Zahlung auf. Eine heutige Mapping-Änderung wirkt sich damit nie rückwirkend auf bereits verbuchte/exportierte Zeiträume aus.
- **Exportnachweis**: jeder DATEV-/Belege-Export wird in `ExportLog` mit Zeitraum, Dateiname, Anzahl Buchungen, Zeitpunkt und dem tatsächlich ausführenden Nutzer (`ExportedByUserId`, über `ICurrentUserAccessor`) protokolliert und ist über die Export-Historie einsehbar.
- **Periodenabschluss** (`PeriodClose`): rein informativer, expliziter Vermerk ("Zeitraum abschließen"), den der Nutzer nach einem Export selbst setzt. Zeigt auf dem Dashboard einen Hinweis-Banner ("Zeitraum wurde am … exportiert und abgeschlossen"). Blockiert bewusst **keine** Bearbeitung in Faktura/Recepta — kein modulübergreifender Hard-Lock, um die Module unabhängig voneinander zu halten.

## 7. Aufbewahrung (organisatorisch)

Alle GoBD-relevanten Daten liegen in PostgreSQL (Bind-Mount `./data/postgres`) — inklusive der eingefrorenen Rechnungs-PDFs und hochgeladenen Belegdateien, die in der Datenbank gespeichert werden, nicht im Dateisystem. Ein vollständiges Datenbank-Backup deckt damit auch Belege, Audit-Trail und Exportprotokolle ab.

Die gesetzliche Aufbewahrungsfrist für Rechnungen, Belege und Buchungsunterlagen beträgt 10 Jahre (§ 147 AO). Regelmäßige Backups des Postgres-Volumes (z. B. `pg_dump`, extern gelagert) liegen in der Verantwortung des Betreibers — Werkbank automatisiert dies nicht selbst.

## 8. Bekannte Einschränkungen

- Nummernvorschau reserviert bereits beim Öffnen des Formulars eine echte Nummer (siehe Abschnitt 2) — akzeptiertes Restrisiko für abgebrochene Entwürfe, kein GoBD-Verstoß.
- Kein GDPdU/IDEA-Export (Z1/Z2/Z3) für den Datenzugriff eines Betriebsprüfers — nicht Teil dieser Umsetzung.
- Kein automatisiertes Backup-/Archivierungssystem in der Software — Aufbewahrung ist Betreiberpflicht (siehe Abschnitt 7).

## 9. Versionsstand

| Modul | Version |
|---|---|
| host | 2.11.4 |
| faktura | 3.15.1 |
| recepta | 2.12.1 |
| saldo | 1.4.0 |

Stand: 2026-09-10.
