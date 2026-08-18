# ScanMe — Änderungsverlauf / Changelog

Die vollständigen Release Notes einer Version, in einer Fassung, die sich an Kunden weitergeben lässt,
liegen unter [`docs/releases/`](docs/releases/). Diese Datei fasst sie zusammen.

Vor dieser Datei stand hier der Änderungsverlauf von NAPS2, dem Projekt, auf dem ScanMe aufbaut. Er ist
unter https://github.com/cyanfish/naps2/blob/master/CHANGELOG.md weiterhin einsehbar.

---

## 1.0.16.0 — 18. August 2026

Erste veröffentlichte Version. Vollständige Release Notes: [`docs/releases/1.0.16.0.md`](docs/releases/1.0.16.0.md)

**Deutsch**

- Scannen von WIA-, TWAIN- und ESCL-Geräten nach PDF, TIFF, JPEG oder PNG, wahlweise mit Texterkennung.
- Automatische Dokumententrennung an Code 39, Code 128, EAN/UPC und Patch-T-Trennblättern. Ein
  wiederholter Barcode setzt das Dokument fort, statt eine Kopie davon zu beginnen.
- Der trennende Barcode ist der Wert, unter dem abgelegt wird: er benennt die Datei, bestimmt den
  SharePoint-Ordner und liefert den SAP-Objektschlüssel.
- Upload nach SharePoint über Microsoft Graph und in das SAP-Archiv über ArchiveLink. Fällt ein Ziel
  aus, wird das andere trotzdem bedient.
- Dokumentenliste mit Inspektor: Kennzeichnung prüfen und korrigieren, bevor archiviert wird. Ein
  fehlgeschlagener Upload bleibt in der Liste und lässt sich wiederholen.
- Diagnose-Konsole, die jeden Schritt protokolliert — auch dann, wenn ein Schritt bewusst nichts tut.
- Deutschsprachige Oberfläche im Fluent-Stil von Windows 11, hell und dunkel.
- Der Installer legt wieder einen Startmenü-Eintrag an, nennt „up in blue GmbH" als Herausgeber und
  zeigt die Lizenz von ScanMe.
- Der Info-Dialog wurde neu gestaltet.

**English**

- Scanning from WIA, TWAIN and ESCL devices to PDF, TIFF, JPEG or PNG, optionally with OCR.
- Automatic document separation at Code 39, Code 128, EAN/UPC and patch-T separator sheets. A repeated
  barcode continues the document instead of starting a copy of it.
- The separating barcode is the value the document is filed under: it names the file, decides the
  SharePoint folder and supplies the SAP object key.
- Upload to SharePoint through Microsoft Graph and to the SAP archive through ArchiveLink. If one target
  is down, the other is still served.
- Document list with inspector: check and correct the identification before archiving. A failed upload
  stays in the list and can be retried.
- Diagnostic console logging every step — including when a step deliberately does nothing.
- German-language interface in the Windows 11 Fluent style, light and dark.
- The installer creates a Start menu entry again, names "up in blue GmbH" as the publisher, and shows
  ScanMe's licence.
- The About dialog was redesigned.
