#!/usr/bin/env python3
"""Writes a SQL file that sets plant height (min/max in cm) and longest vine of existing varieties.

Reads the sheet "Höhen" of data/Samen_Hoehen.xlsx (A = row in Samen, B = Art, C = Sorte, E/F = height min/max,
H = longest vine) and creates one UPDATE per variety. The plant type is found through its German
name (plant_type_translations, language 'de'). Only the three height columns are changed. Safe to run twice.

Usage:
    python tools/import_heights.py data/Samen_Hoehen.xlsx --out data/heights.sql
Run the SQL (after the migration that adds the columns):
    psql -v owner=you@example.com -f heights.sql

Requires: pip install openpyxl
"""
import argparse
import sys
from pathlib import Path

import openpyxl

SHEET = "Höhen"
FIRST_ROW, COL_TYPE, COL_NAME, COL_HMIN, COL_HMAX, COL_VINE = 2, 2, 3, 5, 6, 8
UNNAMED = "(unnamed)"


def lit(v):
    return "'" + str(v).replace("'", "''") + "'"


def as_int(value):
    if value is None or isinstance(value, bool):
        return None
    try:
        return int(round(float(str(value).strip().replace(",", "."))))
    except ValueError:
        return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("excel")
    ap.add_argument("--out", default="heights.sql")
    args = ap.parse_args()

    ws = openpyxl.load_workbook(args.excel, data_only=True)[SHEET]
    rows, skipped = [], []
    for r in range(FIRST_ROW, ws.max_row + 1):
        gattung = ws.cell(r, COL_TYPE).value
        name = ws.cell(r, COL_NAME).value
        if gattung is None and name is None:
            continue
        gattung = str(gattung).strip() if gattung is not None else ""
        name = str(name).strip() if name is not None and str(name).strip() else UNNAMED
        if name == "(ohne Sortenname)":
            name = UNNAMED
        hmin, hmax = as_int(ws.cell(r, COL_HMIN).value), as_int(ws.cell(r, COL_HMAX).value)
        vine = as_int(ws.cell(r, COL_VINE).value)
        if not gattung or (hmin is None and hmax is None):
            skipped.append((r, gattung, name))
            continue
        if hmin is not None and hmax is not None and hmin > hmax:
            hmin, hmax = hmax, hmin
        rows.append((gattung, name, hmin, hmax, vine))

    def num(v):
        return "NULL::int" if v is None else str(v)

    out = [
        "-- Sets height and longest vine of varieties, from data/Samen_Hoehen.xlsx.",
        "-- Usage: psql -v owner=you@example.com -f heights.sql",
        "SET client_encoding = 'UTF8';",
        "BEGIN;",
        "CREATE TEMP TABLE ht (type_de text, name text, hmin int, hmax int, vine int) ON COMMIT DROP;",
        "INSERT INTO ht VALUES",
        ",\n".join(f"  ({lit(g)}, {lit(n)}, {num(a)}, {num(b)}, {num(v)})" for g, n, a, b, v in rows) + ";",
        "",
        "UPDATE plants p SET height_min_cm = ht.hmin, height_max_cm = ht.hmax,",
        "  vine_length_max_cm = ht.vine",
        "FROM ht, plant_type_translations tr, asp_net_users u",
        "WHERE u.normalized_email = upper(:'owner')",
        "  AND p.owner_id = u.id",
        "  AND tr.language = 'de' AND tr.name = ht.type_de AND p.plant_type_id = tr.plant_type_id",
        "  AND p.name = ht.name;",
        "",
        "-- Rows of the file without a matching variety in the database (should be few or none):",
        "SELECT ht.type_de AS gattung, ht.name AS sorte FROM ht",
        "WHERE NOT EXISTS (SELECT 1 FROM plants p JOIN plant_type_translations tr",
        "    ON tr.plant_type_id = p.plant_type_id AND tr.language = 'de'",
        "  JOIN asp_net_users u ON u.id = p.owner_id",
        "  WHERE u.normalized_email = upper(:'owner') AND tr.name = ht.type_de AND p.name = ht.name);",
        "COMMIT;",
        "",
    ]
    Path(args.out).write_text("\n".join(out), encoding="utf-8")
    print(f"{len(rows)} varieties with height, {len(skipped)} rows skipped -> {args.out}")
    for r, g, n in skipped:
        print(f"  skipped row {r}: {g} / {n}")


if __name__ == "__main__":
    sys.exit(main())
