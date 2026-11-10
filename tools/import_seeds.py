#!/usr/bin/env python3
"""One-time import of the seed collection from Excel into PostgreSQL.

Reads the sheets "Helper" (plant types) and "Samen" (plants and season windows)
and writes a SQL file. The script does not connect to the database itself.

The database stores English names only. The CSV file (columns german, english, russian, note, category)
gives the English name of each plant type, its category and the translations that go into
the table plant_type_translations (languages de, en, ru). The category comes from the CSV file,
not from the Excel file (the Excel column "Typ" is ignored).

The plants belong to one user, identified by e-mail address. The user must have registered
in the application before SQL with plants is run.

Modes (--mode):
    all          plant types, translations and plants of one user (default, for the local database)
    reference    only the shared data: plant types and translations (for an empty database, e.g. in Azure)
    extra-types  plant types that are not in the Excel file (tools/new_plant_types.csv), with translations
    categories   sets the category of existing plant types from the CSV files (for a database that was
                 filled before the categories changed)
    companions   good and bad neighbours between plant types (tools/companions.csv)
    plants       only the plants and season windows of one user (the plant types must already exist)

The Excel file is only read by the modes all, reference and plants.

Order for an empty database:   reference, extra-types, companions, plants,
                               then tools/crop_attributes.sql (nutrient demand and exposure of 42 plant types)
Order for the local database:  all (done earlier), then extra-types, companions, tools/crop_attributes.sql

Usage:
    python import_seeds.py data/Samen.xlsx --owner-email you@example.com
    python import_seeds.py data/Samen.xlsx --mode reference --out data/reference.sql
    python import_seeds.py --mode extra-types --out data/extra_types.sql
    python import_seeds.py --mode categories --out data/categories.sql
    python import_seeds.py --mode companions --out data/companions.sql
    python import_seeds.py data/Samen.xlsx --mode plants --owner-email you@example.com --out data/plants.sql

Requires: pip install openpyxl
"""
import argparse
import csv
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path

import openpyxl

# ---------------------------------------------------------------------------
# Translation of the German words used in the Excel file
# ---------------------------------------------------------------------------
MONTHS = {m.lower(): i for i, m in enumerate(
    ["Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August",
     "September", "Oktober", "November", "Dezember"], start=1)}

# Categories of plant types (enum PlantCategory); they are set in the CSV files
CATEGORIES = {"FruitVegetable", "RootVegetable", "LeafyVegetable", "StemVegetable", "Brassica",
              "Legume", "Allium", "Melon", "Herb", "Berry", "FruitTree", "Flower"}
NUTRIENT_DEMAND = {"stark": "Heavy", "mittel": "Medium", "schwach": "Light"}
GERMINATION = {"licht": "Light", "dunkel": "Dark", "vegetativ": "Vegetative"}
DEMAND_UNIT = {"": "Plants", "m": "RowMeters", "qm": "SquareMeters"}
CULTIVATION = {"vorkultur": "StartIndoors", "direktsaat": "DirectSow",
               "topfkultur": "Container", "frühbeet": "ColdFrame"}
SEED_TYPE = {"samenfest": "OpenPollinated", "f1": "F1Hybrid"}
STOCK = {"vorhanden": "InStock", "nicht vorhanden": "OutOfStock",
         "aussortiert": "Retired"}
EXPOSURE = {"sonne": "FullSun", "halbschatten": "PartialShade", "schatten": "Shade"}

# Name given to plants that have a plant type but no variety name in the Excel file.
UNNAMED = "(unnamed)"

# ---------------------------------------------------------------------------
# Column numbers (A = 1)
# ---------------------------------------------------------------------------
# Sheet "Helper": main table starts in row 4 and ends at the first empty cell in column A.
H_FIRST_ROW = 4
H_NAME, H_CATEGORY, H_GERMINATION, H_NUTRIENT = 1, 2, 4, 5
H_GROWTH_MIN, H_GROWTH_MAX, H_UNIT, H_DEMAND_MIN, H_DEMAND_MAX = 8, 9, 12, 13, 14

# Sheet "Samen": data starts in row 5.
S_FIRST_ROW = 5
S_TYPE, S_NAME, S_CULTIVATION, S_GERMINATION, S_POT, S_SEED, S_STOCK = 1, 3, 4, 5, 6, 7, 8
S_SOW1_FROM, S_SOW1_TO, S_SOW2_FROM, S_SOW2_TO = 10, 11, 12, 13
S_GERM_DAYS_MIN, S_GERM_DAYS_MAX, S_EXPOSURE = 16, 17, 18
S_ROW_SPACING, S_PLANT_SPACING = 19, 20
S_MATURITY_MIN, S_MATURITY_MAX = 23, 24
S_HARV1_FROM, S_HARV1_TO, S_HARV2_FROM, S_HARV2_TO = 25, 26, 27, 28


@dataclass
class Report:
    messages: list = field(default_factory=list)  # (severity, sheet, row, column, text)

    def add(self, severity, sheet, row, column, text):
        self.messages.append((severity, sheet, row, column, text))

    def errors(self):
        return sum(1 for m in self.messages if m[0] == "ERROR")

    def format(self):
        order = {"ERROR": 0, "WARNING": 1, "INFO": 2}
        lines = []
        for sev, sheet, row, col, text in sorted(
                self.messages, key=lambda m: (order[m[0]], m[1], m[2])):
            lines.append(f"[{sev}] {sheet} row {row}, {col}: {text}")
        return lines


# ---------------------------------------------------------------------------
# Reading helpers
# ---------------------------------------------------------------------------
def text(ws, row, col):
    """Trimmed text of a cell, or None if empty."""
    value = ws.cell(row, col).value
    if value is None:
        return None
    s = str(value).strip()
    return s or None


def number(ws, row, col, sheet, label, report, as_int=True):
    """Number of a cell. Empty gives None. Non-numbers (e.g. #REF!) give a warning and None."""
    value = ws.cell(row, col).value
    if value is None or (isinstance(value, str) and not value.strip()):
        return None
    if isinstance(value, bool):
        report.add("WARNING", sheet, row, label, f"Not a number ('{value}'); left empty.")
        return None
    if isinstance(value, (int, float)):
        return int(round(value)) if as_int else float(value)
    try:
        parsed = float(str(value).strip().replace(",", "."))
        return int(round(parsed)) if as_int else parsed
    except ValueError:
        report.add("WARNING", sheet, row, label, f"Not a number ('{value}'); left empty.")
        return None


def mapped(mapping, value, sheet, row, label, report, required):
    """Translate a German word. Unknown values are errors; missing values depend on 'required'."""
    if value is None:
        if required:
            report.add("ERROR", sheet, row, label, "Value missing.")
        return None
    key = value.lower()
    if key in mapping:
        return mapping[key]
    report.add("ERROR", sheet, row, label, f"Unknown value '{value}'.")
    return None


def month(value):
    return MONTHS.get(value.lower()) if value else None


def window(ws, row, col_from, col_to, kind, label, required, report):
    """Returns (kind, start, end) or None."""
    a, b = text(ws, row, col_from), text(ws, row, col_to)
    if a is None and b is None:
        if required:
            report.add("ERROR", "Samen", row, label, "Window missing.")
        return None
    start, end = month(a), month(b)
    if start is None or end is None:
        report.add("ERROR", "Samen", row, label, f"Unknown or incomplete month ('{a}' to '{b}').")
        return None
    return (kind, start, end)


def pot_size(value, row, report):
    """'10-15' gives (10, 15); '10' gives (10, 10)."""
    if value is None:
        return None, None
    m = re.fullmatch(r"(\d+)\s*(?:-\s*(\d+))?", value)
    if not m:
        report.add("WARNING", "Samen", row, "Topfgröße", f"Cannot read '{value}'; left empty.")
        return None, None
    low = int(m.group(1))
    return low, int(m.group(2)) if m.group(2) else low


def read_names(path, report):
    """CSV with columns german, english, russian, category (note is ignored).
    Returns {german: (english, russian, category)}."""
    names = {}
    with open(path, encoding="utf-8-sig", newline="") as f:
        for i, row in enumerate(csv.DictReader(f, delimiter=";"), start=2):
            de, en, ru, cat = ((row.get(k) or "").strip() for k in ("german", "english", "russian", "category"))
            if not (de and en and ru):
                report.add("ERROR", "names.csv", i, "Zeile", "german, english and russian are all required.")
                continue
            if cat not in CATEGORIES:
                report.add("ERROR", "names.csv", i, de, f"Unknown or missing category '{cat}'.")
                continue
            names[de] = (en, ru, cat)
    english = [en for en, _, _ in names.values()]
    for en in {e for e in english if english.count(e) > 1}:
        report.add("ERROR", "names.csv", 0, "english", f"Duplicate English name '{en}'.")
    return names


def read_extra_types(path, names, report):
    """CSV with columns german, english, russian, category (note is ignored).
    Plant types that are not in the Excel file. Returns a list of dicts."""
    types = []
    known_en = {en.lower() for en, _, _ in names.values()}
    known_de = {de.lower() for de in names}
    with open(path, encoding="utf-8-sig", newline="") as f:
        for i, row in enumerate(csv.DictReader(f, delimiter=";"), start=2):
            de, en, ru, cat = ((row.get(k) or "").strip() for k in ("german", "english", "russian", "category"))
            if not (de and en and ru):
                report.add("ERROR", "extra types", i, "Zeile", "german, english and russian are all required.")
                continue
            if cat not in CATEGORIES:
                report.add("ERROR", "extra types", i, en, f"Unknown or missing category '{cat}'.")
                continue
            if en.lower() in known_en or de.lower() in known_de:
                report.add("ERROR", "extra types", i, en, "Already in the names CSV; it belongs to the Excel plant types.")
                continue
            if any(t["english"].lower() == en.lower() or t["german"].lower() == de.lower() for t in types):
                report.add("ERROR", "extra types", i, en, "Duplicate entry.")
                continue
            types.append({"german": de, "english": en, "russian": ru, "category": cat})
    return types


def read_companions(path, valid_names, report):
    """CSV with columns plant_a, plant_b, relation (Good or Bad), note.
    Names are English plant type names. Returns a list of (plant_a, plant_b, relation, note)."""
    pairs = {}
    valid_lower = {n.lower(): n for n in valid_names}
    with open(path, encoding="utf-8-sig", newline="") as f:
        for i, row in enumerate(csv.DictReader(f, delimiter=";"), start=2):
            a, b, rel = ((row.get(k) or "").strip() for k in ("plant_a", "plant_b", "relation"))
            note = (row.get("note") or "").strip() or None
            if not (a and b and rel):
                report.add("ERROR", "companions", i, "Zeile", "plant_a, plant_b and relation are all required.")
                continue
            bad_name = False
            for label, name in (("plant_a", a), ("plant_b", b)):
                if name.lower() not in valid_lower:
                    report.add("ERROR", "companions", i, label, f"Unknown plant type '{name}'.")
                    bad_name = True
            if bad_name:
                continue
            a, b = valid_lower[a.lower()], valid_lower[b.lower()]
            if rel not in ("Good", "Bad"):
                report.add("ERROR", "companions", i, "relation", f"'{rel}' is not Good or Bad.")
                continue
            if a == b:
                report.add("ERROR", "companions", i, "plant_a/plant_b", f"A plant type cannot be its own neighbour ('{a}').")
                continue
            key = tuple(sorted((a, b)))
            if key in pairs:
                same = pairs[key][2] == rel
                report.add("ERROR", "companions", i, f"{a} / {b}",
                           "Duplicate pair." if same else f"Conflict: also listed as {pairs[key][2]}.")
                continue
            pairs[key] = (a, b, rel, note)
    return list(pairs.values())


# ---------------------------------------------------------------------------
# Sheet "Helper": plant types
# ---------------------------------------------------------------------------
def read_plant_types(ws, names, report):
    types = {}
    row = H_FIRST_ROW
    while text(ws, row, H_NAME) is not None:
        name = text(ws, row, H_NAME)
        before = report.errors()

        nutrient = mapped(NUTRIENT_DEMAND, text(ws, row, H_NUTRIENT), "Helper", row, "Zehrer", report, False)
        germination = mapped(GERMINATION, text(ws, row, H_GERMINATION), "Helper", row, "Keimung", report, False)
        unit = mapped(DEMAND_UNIT, text(ws, row, H_UNIT) or "", "Helper", row, "Einheit", report, False)

        if name.lower() in {k.lower() for k in types}:
            report.add("ERROR", "Helper", row, "Typ", f"Duplicate plant type '{name}'.")

        if name not in names:
            report.add("ERROR", "Helper", row, "Typ", f"No translation for '{name}' in the names CSV.")
            english, russian, category = name, name, None
        else:
            english, russian, category = names[name]

        record = {
            "name": english,
            "translations": {"de": name, "en": english, "ru": russian},
            "category": category,
            "nutrient_demand": nutrient,
            "germination_type": germination,
            "growth_days_min": number(ws, row, H_GROWTH_MIN, "Helper", "Wachstumszeit min", report),
            "growth_days_max": number(ws, row, H_GROWTH_MAX, "Helper", "Wachstumszeit max", report),
            "demand_per_person_min": number(ws, row, H_DEMAND_MIN, "Helper", "Bedarf min", report, as_int=False),
            "demand_per_person_max": number(ws, row, H_DEMAND_MAX, "Helper", "Bedarf max", report, as_int=False),
            "demand_unit": unit,
            "exposure": None,  # filled in later from the varieties (see assign_exposure)
        }
        if record["demand_per_person_min"] is None and record["demand_per_person_max"] is None:
            record["demand_unit"] = None  # no demand recorded, so no unit either
        if record["nutrient_demand"] is None:
            report.add("INFO", "Helper", row, "Zehrer", f"No nutrient demand for '{name}'.")

        if report.errors() == before:
            types[name] = record
        row += 1
    return types


# ---------------------------------------------------------------------------
# Sheet "Samen": plants and season windows
# ---------------------------------------------------------------------------
def read_plants(ws, types, report):
    plants = []
    seen = set()
    for row in range(S_FIRST_ROW, ws.max_row + 1):
        type_name, plant_name = text(ws, row, S_TYPE), text(ws, row, S_NAME)
        if type_name is None and plant_name is None:
            continue
        before = report.errors()

        if type_name is None:
            report.add("ERROR", "Samen", row, "Gattung", "Value missing.")
        elif type_name not in types:
            report.add("ERROR", "Samen", row, "Gattung", f"Plant type '{type_name}' not found in sheet 'Helper'.")
        if plant_name is None and type_name is not None:
            plant_name = UNNAMED
            report.add("INFO", "Samen", row, "Sorte",
                       f"No variety name for '{type_name}'; imported as '{UNNAMED}'.")
        elif plant_name is None:
            report.add("ERROR", "Samen", row, "Sorte", "Value missing.")
        if type_name and plant_name:
            key = (type_name.lower(), plant_name.lower())
            if key in seen:
                report.add("ERROR", "Samen", row, "Gattung/Sorte", f"Duplicate: '{type_name} / {plant_name}'.")
            seen.add(key)

        cultivation = mapped(CULTIVATION, text(ws, row, S_CULTIVATION), "Samen", row, "Kultur", report, False)
        if text(ws, row, S_CULTIVATION) is None:
            report.add("INFO", "Samen", row, "Kultur", "Value missing; left empty.")
        seed = mapped(SEED_TYPE, text(ws, row, S_SEED), "Samen", row, "Kreuzung", report, True)
        stock = mapped(STOCK, text(ws, row, S_STOCK), "Samen", row, "Bestand", report, True)
        exposure = mapped(EXPOSURE, text(ws, row, S_EXPOSURE), "Samen", row, "Standort", report, False)

        # Germination lives on the plant type (sheet "Helper"); "Samen" only repeats it.
        germ_here = mapped(GERMINATION, text(ws, row, S_GERMINATION), "Samen", row, "Keimung", report, False)
        if type_name in types and germ_here is not None:
            germ_type = types[type_name]["germination_type"]
            if germ_type is None:
                types[type_name]["germination_type"] = germ_here
            elif germ_type != germ_here:
                report.add("WARNING", "Samen", row, "Keimung",
                           f"'{plant_name}' says {germ_here}, plant type '{type_name}' says {germ_type}; "
                           f"keeping {germ_type}.")

        windows = [w for w in (
            window(ws, row, S_SOW1_FROM, S_SOW1_TO, "Sowing", "Aussaat", True, report),
            window(ws, row, S_SOW2_FROM, S_SOW2_TO, "Sowing", "Aussaat 2", False, report),
            window(ws, row, S_HARV1_FROM, S_HARV1_TO, "Harvest", "Ernte", False, report),
            window(ws, row, S_HARV2_FROM, S_HARV2_TO, "Harvest", "Ernte 2", False, report),
        ) if w]

        pot_min, pot_max = pot_size(text(ws, row, S_POT), row, report)

        if report.errors() > before:
            report.add("INFO", "Samen", row, "Zeile", f"Row skipped ({type_name} / {plant_name}).")
            continue

        plants.append({
            "type_name": type_name, "name": plant_name,
            "seed_type": seed, "stock_status": stock, "cultivation_method": cultivation,
            "exposure": exposure,
            "germination_days_min": number(ws, row, S_GERM_DAYS_MIN, "Samen", "Keimdauer min", report),
            "germination_days_max": number(ws, row, S_GERM_DAYS_MAX, "Samen", "Keimdauer max", report),
            "maturity_days_min": number(ws, row, S_MATURITY_MIN, "Samen", "Reifezeit min", report),
            "maturity_days_max": number(ws, row, S_MATURITY_MAX, "Samen", "Reifezeit max", report),
            "row_spacing_cm": number(ws, row, S_ROW_SPACING, "Samen", "Abstand Zeile", report),
            "plant_spacing_cm": number(ws, row, S_PLANT_SPACING, "Samen", "Abstand Spalte", report),
            "pot_size_min_liters": pot_min, "pot_size_max_liters": pot_max,
            "windows": windows,
        })
    return plants


def assign_exposure(types, plants, report):
    """The plant type gets the most common exposure of its varieties (default).
    A variety keeps its own exposure only if it differs from the type's default."""
    from collections import Counter
    for type_name, t in types.items():
        counts = Counter(p["exposure"] for p in plants
                         if p["type_name"] == type_name and p["exposure"])
        if not counts:
            continue
        ranked = counts.most_common()
        if len(ranked) > 1 and ranked[0][1] == ranked[1][1]:
            report.add("INFO", "Samen", 0, type_name,
                       f"Exposure tie {dict(counts)}; type default set to FullSun, please review.")
            t["exposure"] = "FullSun"
        else:
            t["exposure"] = ranked[0][0]
        if len(ranked) > 1:
            report.add("INFO", "Samen", 0, type_name,
                       f"Varieties differ {dict(counts)}; default {t['exposure']}, others keep their own value.")
    for p in plants:
        t = types[p["type_name"]]
        if p["exposure"] == t["exposure"]:
            p["exposure"] = None


# ---------------------------------------------------------------------------
# SQL output
# ---------------------------------------------------------------------------
def lit(value):
    """SQL literal."""
    if value is None:
        return "NULL"
    if isinstance(value, (int, float)):
        return repr(value)
    return "'" + str(value).replace("'", "''") + "'"


def insert(table, columns, values):
    cols = ", ".join(columns)
    vals = ", ".join(values)
    return f"INSERT INTO {table} ({cols}) VALUES ({vals});"


def write_sql(path, types, plants, owner_email, mode):
    type_cols = ["name", "category", "nutrient_demand", "germination_type",
                 "growth_days_min", "growth_days_max",
                 "demand_per_person_min", "demand_per_person_max", "demand_unit", "exposure"]
    plant_cols = ["seed_type", "stock_status", "cultivation_method", "exposure",
                  "germination_days_min", "germination_days_max",
                  "maturity_days_min", "maturity_days_max",
                  "row_spacing_cm", "plant_spacing_cm",
                  "pot_size_min_liters", "pot_size_max_liters"]

    with_types = mode in ("all", "reference")
    with_plants = mode in ("all", "plants")
    # ASP.NET Core Identity stores the e-mail address in upper case in normalized_email
    owner_id = ("(SELECT id FROM asp_net_users WHERE normalized_email = "
                f"{lit(owner_email.strip().upper())})") if owner_email else None

    if mode == "all":
        guard = ("IF EXISTS (SELECT 1 FROM plant_types) OR EXISTS (SELECT 1 FROM plants) "
                 "OR EXISTS (SELECT 1 FROM plant_type_translations) THEN")
        message = "Tables are not empty; import aborted."
    elif mode == "reference":
        guard = ("IF EXISTS (SELECT 1 FROM plant_types) OR EXISTS (SELECT 1 FROM plant_type_translations) THEN")
        message = "Plant types already exist; import aborted."
    else:
        guard = (f"IF NOT EXISTS (SELECT 1 FROM plant_types) OR EXISTS "
                 f"(SELECT 1 FROM plants WHERE owner_id = {owner_id}) THEN")
        message = "Plant types are missing or this user already has plants; import aborted."

    out = [
        f"-- Generated by import_seeds.py (mode: {mode}). One-time import.",
        "SET client_encoding = 'UTF8';",
        "BEGIN;",
        "",
        "DO $$ BEGIN",
        f"  {guard}",
        f"    RAISE EXCEPTION '{message}';",
        "  END IF;",
        "END $$;",
    ]
    if with_types:
        out += ["", "-- Plant types"]
        for t in types.values():
            out.append(insert("plant_types", type_cols, [lit(t[c]) for c in type_cols]))

    if with_types:
        out += ["", "-- Plant type translations"]
        for t in types.values():
            type_id = f"(SELECT id FROM plant_types WHERE name = {lit(t['name'])})"
            for language, translated in t["translations"].items():
                out.append(insert("plant_type_translations", ["plant_type_id", "language", "name"],
                                  [type_id, lit(language), lit(translated)]))

    if with_plants:
        out += ["", "-- Plants"]
        for p in plants:
            type_id = f"(SELECT id FROM plant_types WHERE name = {lit(types[p['type_name']]['name'])})"
            out.append(insert("plants", ["owner_id", "plant_type_id", "name"] + plant_cols,
                              [owner_id, type_id, lit(p["name"])] + [lit(p[c]) for c in plant_cols]))

        out += ["", "-- Season windows"]
        for p in plants:
            plant_id = ("(SELECT p.id FROM plants p JOIN plant_types t ON t.id = p.plant_type_id "
                        f"WHERE t.name = {lit(types[p['type_name']]['name'])} AND p.name = {lit(p['name'])} "
                        f"AND p.owner_id = {owner_id})")
            for kind, start, end in p["windows"]:
                out.append(insert("season_windows", ["plant_id", "kind", "start_month", "end_month"],
                                  [plant_id, lit(kind), lit(start), lit(end)]))

    out += ["", "COMMIT;", ""]
    Path(path).write_text("\n".join(out), encoding="utf-8", newline="\n")


def write_extra_types_sql(path, extra):
    names = ", ".join(lit(t["english"]) for t in extra)
    out = [
        "-- Generated by import_seeds.py (mode: extra-types). One-time import.",
        "SET client_encoding = 'UTF8';",
        "BEGIN;",
        "",
        "DO $$ BEGIN",
        f"  IF EXISTS (SELECT 1 FROM plant_types WHERE name IN ({names})) THEN",
        "    RAISE EXCEPTION 'One of the extra plant types already exists; import aborted.';",
        "  END IF;",
        "END $$;",
        "",
        "-- Plant types",
    ]
    for t in extra:
        out.append(insert("plant_types", ["name", "category"], [lit(t["english"]), lit(t["category"])]))
    out += ["", "-- Plant type translations"]
    for t in extra:
        type_id = f"(SELECT id FROM plant_types WHERE name = {lit(t['english'])})"
        for language, translated in (("de", t["german"]), ("en", t["english"]), ("ru", t["russian"])):
            out.append(insert("plant_type_translations", ["plant_type_id", "language", "name"],
                              [type_id, lit(language), lit(translated)]))
    out += ["", "COMMIT;", ""]
    Path(path).write_text("\n".join(out), encoding="utf-8", newline="\n")


def write_companions_sql(path, pairs):
    used = sorted({n for a, b, _, _ in pairs for n in (a, b)})
    array = ", ".join(lit(n) for n in used)
    rows = ",\n  ".join(f"({lit(a)}, {lit(b)}, {lit(rel)}, {lit(note)})" for a, b, rel, note in pairs)
    out = [
        "-- Generated by import_seeds.py (mode: companions). One-time import.",
        "SET client_encoding = 'UTF8';",
        "BEGIN;",
        "",
        "DO $$ BEGIN",
        "  IF EXISTS (SELECT 1 FROM plant_type_companions) THEN",
        "    RAISE EXCEPTION 'plant_type_companions is not empty; import aborted.';",
        "  END IF;",
        "END $$;",
        "",
        "-- All plant types used below must exist (reference and extra-types come first).",
        "DO $$",
        "DECLARE missing text;",
        "BEGIN",
        "  SELECT string_agg(n, ', ' ORDER BY n) INTO missing",
        f"  FROM unnest(ARRAY[{array}]) AS n",
        "  WHERE NOT EXISTS (SELECT 1 FROM plant_types WHERE name = n);",
        "  IF missing IS NOT NULL THEN",
        "    RAISE EXCEPTION 'Unknown plant types (import reference and extra-types first): %', missing;",
        "  END IF;",
        "END $$;",
        "",
        "-- Each pair is stored once, the smaller id first.",
        "INSERT INTO plant_type_companions (plant_type_id, other_plant_type_id, relation, note)",
        "SELECT LEAST(a.id, b.id), GREATEST(a.id, b.id), v.relation, v.note",
        "FROM (VALUES",
        f"  {rows}",
        ") AS v(plant_a, plant_b, relation, note)",
        "JOIN plant_types a ON a.name = v.plant_a",
        "JOIN plant_types b ON b.name = v.plant_b;",
        "",
        "COMMIT;",
        "",
    ]
    Path(path).write_text("\n".join(out), encoding="utf-8", newline="\n")


def write_categories_sql(path, entries):
    """entries: list of (english name, category). Sets the category of existing plant types."""
    array = ", ".join(lit(n) for n, _ in entries)
    out = [
        "-- Generated by import_seeds.py (mode: categories). Sets the category of existing plant types.",
        "SET client_encoding = 'UTF8';",
        "BEGIN;",
        "",
        "DO $$",
        "DECLARE missing text;",
        "BEGIN",
        "  SELECT string_agg(n, ', ' ORDER BY n) INTO missing",
        f"  FROM unnest(ARRAY[{array}]) AS n",
        "  WHERE NOT EXISTS (SELECT 1 FROM plant_types WHERE name = n);",
        "  IF missing IS NOT NULL THEN",
        "    RAISE EXCEPTION 'Unknown plant types (import reference and extra-types first): %', missing;",
        "  END IF;",
        "END $$;",
        "",
    ]
    for name, category in entries:
        out.append(f"UPDATE plant_types SET category = {lit(category)} WHERE name = {lit(name)};")
    out += ["", "COMMIT;", ""]
    Path(path).write_text("\n".join(out), encoding="utf-8", newline="\n")


def run_small_mode(args):
    """Modes extra-types and companions: no Excel file needed."""
    report = Report()
    names = read_names(args.names, report)
    extra = read_extra_types(args.extra_types, names, report)
    if args.mode == "extra-types":
        summary = [f"Mode: extra-types", f"Plant types: {len(extra)}", f"Translations: {len(extra) * 3}"]
        writer = lambda: write_extra_types_sql(args.out, extra)
    elif args.mode == "categories":
        entries = [(en, cat) for en, _, cat in names.values()] + [(t["english"], t["category"]) for t in extra]
        summary = ["Mode: categories", f"Plant types: {len(entries)}"]
        writer = lambda: write_categories_sql(args.out, entries)
    else:
        valid = {en for en, _, _ in names.values()} | {t["english"] for t in extra}
        pairs = read_companions(args.companions, valid, report)
        good = sum(1 for p in pairs if p[2] == "Good")
        summary = ["Mode: companions", f"Pairs: {len(pairs)} (Good: {good}, Bad: {len(pairs) - good})"]
        writer = lambda: write_companions_sql(args.out, pairs)
    summary.append(f"Errors: {report.errors()}")
    lines = summary + [""] + report.format()
    Path(args.report).parent.mkdir(parents=True, exist_ok=True)
    Path(args.report).write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("\n".join(lines))
    if report.errors():
        sys.exit(f"\nNo SQL written: fix the errors above first (report: {args.report}).")
    Path(args.out).parent.mkdir(parents=True, exist_ok=True)
    writer()
    print(f"\nSQL written to {args.out}, report to {args.report}")


# ---------------------------------------------------------------------------
def main():
    parser = argparse.ArgumentParser(description="Create a SQL import file from the seed Excel file.")
    parser.add_argument("excel", nargs="?", help="path to the Excel file, e.g. data/Samen.xlsx "
                                                 "(not needed for --mode extra-types, categories and companions)")
    parser.add_argument("--mode", choices=["all", "reference", "extra-types", "categories", "companions", "plants"], default="all",
                        help="all (default), reference (shared data only), extra-types, categories, companions "
                             "or plants (plants of one user only)")
    parser.add_argument("--owner-email",
                        help="e-mail address of the registered user who owns the plants "
                             "(only needed for --mode all and plants)")
    parser.add_argument("--names", default="tools/plant_type_names.csv",
                        help="CSV with german;english;russian plant type names")
    parser.add_argument("--extra-types", default="tools/new_plant_types.csv",
                        help="CSV with german;english;russian names of plant types that are not in the Excel file")
    parser.add_argument("--companions", default="tools/companions.csv",
                        help="CSV with plant_a;plant_b;relation;note")
    parser.add_argument("--out", default="data/import.sql", help="SQL file to write")
    parser.add_argument("--report", default="data/import_report.txt", help="report file to write")
    args = parser.parse_args()

    if args.mode in ("extra-types", "categories", "companions"):
        run_small_mode(args)
        return
    if not args.excel:
        parser.error(f"the Excel file is required for --mode {args.mode}")
    if args.mode in ("all", "plants") and not args.owner_email:
        parser.error("--owner-email is required for --mode all and plants")

    workbook = openpyxl.load_workbook(args.excel, data_only=True)  # cached formula results
    for sheet in ("Helper", "Samen"):
        if sheet not in workbook.sheetnames:
            sys.exit(f"Sheet '{sheet}' not found in {args.excel}")

    report = Report()
    names = read_names(args.names, report)
    types = read_plant_types(workbook["Helper"], names, report)
    plants = read_plants(workbook["Samen"], types, report)
    assign_exposure(types, plants, report)

    used = {p["type_name"] for p in plants}
    windows = sum(len(p["windows"]) for p in plants)
    summary = [
        f"Mode: {args.mode}",
        f"Plant types: {len(types)} ({len(types) - len(used)} without plants)",
        f"Translations: {sum(len(t['translations']) for t in types.values())}",
        f"Plants: {len(plants)}",
        f"Season windows: {windows}",
        f"Errors: {report.errors()}, "
        f"warnings: {sum(1 for m in report.messages if m[0] == 'WARNING')}, "
        f"info: {sum(1 for m in report.messages if m[0] == 'INFO')}",
    ]

    Path(args.out).parent.mkdir(parents=True, exist_ok=True)
    write_sql(args.out, types, plants, args.owner_email, args.mode)
    lines = summary + [""] + report.format()
    Path(args.report).write_text("\n".join(lines) + "\n", encoding="utf-8")

    print("\n".join(lines))
    print(f"\nSQL written to {args.out}, report to {args.report}")


if __name__ == "__main__":
    main()
