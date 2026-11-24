# Bedienkonzept (Entwurf, 2026-11-22)

*English version: [UI-CONCEPT.md](UI-CONCEPT.md)*

Ziel: Jede Seite verhält sich gleich, damit man nichts neu lernen muss. Die Regeln sind aus dem vorhandenen Code abgeleitet und gelten für Neues und beim Anfassen alter Seiten. Das Aussehen (Papierweiß, Tinte, Terrakotta, Serifenschrift) steht in `site.css` und `CLAUDE.md` und ändert sich hier nicht.

Status: Entwurf, noch nicht umgesetzt. Abschnitt 8 listet die Abweichungen.

## 1. Navigation

Vier Gruppen nach Aufgabe, jede mit Untermenü (die Seite der Gruppe ist der erste Eintrag):

| Gruppe | Einträge |
| --- | --- |
| Jetzt | Diesen Monat · Kalender |
| Planen | Pläne · Bedarf |
| Sammlung | Sorten · Pflanzenarten · Rollen |
| Garten | Mein Garten · Beete · Töpfe · Untersetzer |

- Die aktive Gruppe ist unterstrichen (wie heute `active`), im Untermenü ist der aktive Eintrag markiert.
- Das Menü funktioniert ohne Maus (Tab, Enter, Pfeiltasten) und auf dem Handy als eine Liste zum Auf- und Zuklappen.
- "Seed Planner" führt zur Startseite. Die Startseite zeigt "Diesen Monat" als Haupteinstieg und die Gruppen als Kacheln.
- Namen in Menü und Seitentitel sind gleich (heute teils anders, z. B. "Garten" und "Mein Garten (Beete)").

## 2. Seitenaufbau

1. Genau eine `<h1>` mit dem Namen der Seite (die Gartenseiten bekommen auch eine).
2. Darunter, falls nötig, ein Satz Erklärung (`text-muted`).
3. Werkzeugzeile: links Filter und Suche, rechts die Hauptaktion der Seite ("Neu …").
4. Inhalt.

Listenseiten, Bearbeitungsseiten (Formular) und Detailseiten haben jeweils denselben Aufbau, siehe 4 und 5.

## 3. Aktionen (Buttons)

| Rolle | Aussehen | Regel |
| --- | --- | --- |
| Hauptaktion | `btn btn-primary` (Terrakotta) | höchstens eine pro Seite oder Formular: Speichern, Neu, Berechnen |
| Nebenaktion | `btn btn-outline-secondary` | alles Gleichwertige: Bearbeiten, Export, Zurück zur Liste |
| Gefährlich | `btn btn-outline-danger` | Löschen, Zurücksetzen; immer mit Bestätigung (Abschnitt 6) |
| Navigation im Text | normaler Link | nur zum Wechseln auf eine andere Seite, nie zum Ändern von Daten |

- Das Wort im Button ist das Verb, das passiert ("Speichern", "Plan berechnen", "Gesät"), nicht "OK".
- Reihenfolge von links nach rechts: Hauptaktion, Abbrechen, dann Gefährliches rechts abgesetzt.
- `btn-success` und `btn-link text-danger` kommen nicht mehr vor. In `site.css` sind `btn-success` und `btn-primary` schon gleich gefärbt, es ändert sich also nur der Klassenname.
- Aktionen an einer Tabellenzeile sind Nebenaktionen in klein (`btn-sm`), Löschen nur auf der Bearbeitungsseite, nicht in der Liste.

## 4. Listen

- Filter und Suche oben (`_FilterBar`), Sortierung über die Spaltenköpfe, Ergebniszahl sichtbar ("12 von 203").
- Der Name in der ersten Spalte ist der Link zur Detail- oder Bearbeitungsseite; die Zeile selbst ist nicht klickbar.
- Leere Liste: ein Satz plus Link zur Hauptaktion ("Noch keine Töpfe. Topf anlegen").
- Listen mit Unterpunkten sind immer faltbar (`<details>`), die Zahl der Unterpunkte steht in der Überschrift. Per Default ist alles zugeklappt, auch wenn eine Gruppe Arbeit enthält; die Überschrift zeigt dafür die Zahl (z. B. "Ernten (3)"). Es gibt einen gemeinsamen Schalter "Alle auf/zu" (heute `fold-groups`). Ausnahmen: Bereiche, die erst durch eine Aktion erscheinen (Eigenschaften der gewählten Fläche im Garten-Editor), und Bereiche mit aktivem Inhalt (gesetzter Filter, gerade bearbeitete Zeile, gewählte Töpfe) sind offen.
- Zahlen rechtsbündig, Texte linksbündig, Einheiten in der Spaltenüberschrift.

## 5. Formulare

- Beschriftung über dem Feld, Pflichtfelder mit `*`, Hilfetext unter dem Feld (`form-text`), Fehler direkt am Feld und zusätzlich oben als Zusammenfassung.
- Felder in einem 12er-Raster, die Reihenfolge folgt dem Denken: Was ist es (Art, Name) → Woher (Bezug, Bestand) → Wie wächst es (Aussaat, Kulturort, Standort, Rolle) → Maße → Zeiten → Notizen.
- Seltene Felder in einem faltbaren Abschnitt, nicht im Hauptformular.
- Unten, immer in dieser Reihenfolge: **Speichern** · Abbrechen · (Abstand) Löschen.
- Nach dem Speichern: zurück zur Liste (Neuanlage) oder zur Detailseite (Änderung) mit grüner Bestätigung "Gespeichert". Bei Fehlern bleibt der Eingabestand erhalten.
- Datumsfelder sind echte Datumsfelder. Wenn ein Feld ein anderes bestimmt (Start → Ende), wird das automatisch gesetzt, aber überschreibbar.

## 6. Rückmeldung und Bestätigung

| Fall | Muster |
| --- | --- |
| Erfolg | grüne `alert-success`, verschwindet nicht von selbst, nach Reload weg |
| Warnung / Hinweis | `alert-warning` mit dem nächsten Schritt als Link ("Gartenfläche festlegen") |
| Fehler | `alert-danger` oben + Meldung am Feld |
| Löschen | Bestätigungsdialog mit Namen ("Topf „Blau 1“ löschen?") und, wenn etwas daran hängt, was mitgelöscht oder verwaist wird |
| Zurücksetzen / Umschalten, das Eingaben verwirft | gleicher Bestätigungsdialog |
| Rückgängig möglich (Gartenkarte, Erledigt-Haken) | keine Bestätigung, dafür Rückgängig-Knopf |

- Hinweise zu fehlenden Daten (z. B. keine Gartenfläche, kein Abstand an der Sorte) nennen immer, wo man das behebt.

## 7. Texte und Sprache

- Alle Texte über Ressourcen (DE/EN/RU), nie im Code. Namen in der Datenbank englisch, Anzeige übersetzt.
- Gleiche Dinge heißen überall gleich: "Sorte" (Plant), "Pflanzenart" (PlantType), "Plan", "Beet", "Topf". Verb-Schreibweise: "anlegen" für Neues, "bearbeiten", "löschen", "speichern".
- Datum und Zahlen nach Kultur des Browsers, Maße immer mit Einheit (m², cm, l).

## 8. Abweichungen im Code (Stand heute)

| Bereich | Heute | Soll |
| --- | --- | --- |
| Navigation | 11 Einträge flach | 4 Gruppen (Abschnitt 1) |
| Hauptbutton | 25× `btn-success`, 2× `btn-primary` (Töpfe, Beete) | überall `btn-primary` |
| Löschen | rot umrandet (6 Seiten) und `btn-link text-danger` (6 Stellen) gemischt | immer `btn-outline-danger` |
| Bestätigung | 11 `confirm`-Aufrufe, Text je Seite verschieden | gemeinsamer Helfer, Text mit Namen |
| Abbrechen | fehlt bei Plänen-Liste, Demand, ThisMonth-Haken | nach Abschnitt 5 überall in Formularen |
| Seitentitel | Gartenseiten ohne `<h1>` im Seitenkörper, Titel aus dem Editor | `<h1>` wie überall |
| Faltbare Gruppen | Verteilung 9, Garten 18, Plants/Edit 3, Kalender 1, Diesen Monat 2 – Standard offen/zu uneinheitlich (z. B. Diesen Monat und Verteilung "Einstellungen" sind offen) | alles per Default zu (Abschnitt 4) |
| Meldungen | `alert` an 5 Stellen in Verteilung, sonst teils `TempData` | gemeinsame Teilansicht `_Message` |
| Formular Sorten | Reihenfolge gerade umgebaut | als Muster für die anderen Formulare |
| Menütexte | "Garten" ↔ "Mein Garten (Beete)" ↔ "Grundriss und Grundstück" | ein Name je Seite |

## 9. Umsetzung in Schritten

1. Teilansichten und Helfer: `_Message`, `_Confirm` (Bestätigungsdialog), Bestätigungsregel, Formular-Fußzeile (`_FormActions`: Speichern/Abbrechen/Löschen).
2. Buttons und Löschen vereinheitlichen (rein mechanisch, `btn-success` → `btn-primary`, Löschen → Outline).
3. Navigation in Gruppen mit Untermenü (Layout, Resources, CSS, Handy-Ansicht).
4. Seiten nacheinander auf Aufbau und Formularmuster bringen: Töpfe, Untersetzer, Beete, Pflanzenarten, Sorten, Pläne, Garten.
5. Texte angleichen (Menü- und Seitennamen DE/EN/RU).
6. Dieses Dokument am Ende auf "umgesetzt" setzen.

Nach jedem Schritt: Build und Tests, dann Commit.
