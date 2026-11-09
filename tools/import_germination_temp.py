#!/usr/bin/env python3
"""Writes a SQL file that sets the germination temperature (min/max in °C) of existing varieties.

Reads the sheet "Samen" (column A = Gattung, C = Sorte, N/O = Keimung T °C min/max) and creates one
UPDATE per variety. The plant type is found through its German name (plant_type_translations, language 'de').
Only the two temperature columns are changed. Safe to run twice.

Usage:
    python tools/import_germination_temp.py data/Samen.xlsx --out data/germination_temp.sql
Run the SQL:
    psql -v owner=you@example.com -f germination_temp.sql

Requires: pip install openpyxl
"""
import argparse
import sys
from pathlib import Path

import openpyxl

FIRST_ROW, COL_TYPE, COL_NAME, COL_TMIN, COL_TMAX = 5, 1, 3, 14, 15
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
    ap.add_argument("--out", default="germination_temp.sql")
    args = ap.parse_args()

    ws = openpyxl.load_workbook(args.excel, data_only=True)["Samen"]
    rows, skipped = [], []
    for r in range(FIRST_ROW, ws.max_row + 1):
        gattung = ws.cell(r, COL_TYPE).value
        name = ws.cell(r, COL_NAME).value
        if gattung is None and name is None:
            continue
        gattung = str(gattung).strip() if gattung is not None else ""
        name = str(name).strip() if name is not None and str(name).strip() else UNNAMED
        tmin, tmax = as_int(ws.cell(r, COL_TMIN).value), as_int(ws.cell(r, COL_TMAX).value)
        if not gattung or (tmin is None and tmax is None):
            skipped.append((r, gattung, name))
            continue
        if tmin is not None and tmax is not None and tmin > tmax:
            tmin, tmax = tmax, tmin
        rows.append((gattung, name, tmin, tmax))

    def num(v):
        return "NULL::int" if v is None else str(v)

    out = [
        "-- Sets the germination temperature (degrees C) of varieties, from the sheet 'Samen'.",
        "-- Usage: psql -v owner=you@example.com -f germination_temp.sql",
        "SET client_encoding = 'UTF8';",
        "BEGIN;",
        "CREATE TEMP TABLE gt (type_de text, name text, tmin int, tmax int) ON COMMIT DROP;",
        "INSERT INTO gt VALUES",
        ",\n".join(f"  ({lit(g)}, {lit(n)}, {num(a)}, {num(b)})" for g, n, a, b in rows) + ";",
        "",
        "UPDATE plants p SET germination_temp_min_c = gt.tmin, germination_temp_max_c = gt.tmax",
        "FROM gt, plant_type_translations tr, asp_net_users u",
        "WHERE u.normalized_email = upper(:'owner')",
        "  AND p.owner_id = u.id",
        "  AND tr.language = 'de' AND tr.name = gt.type_de AND p.plant_type_id = tr.plant_type_id",
        "  AND p.name = gt.name;",
        "",
        "-- Rows of the file without a matching variety in the database (should be few or none):",
        "SELECT gt.type_de AS gattung, gt.name AS sorte FROM gt",
        "WHERE NOT EXISTS (SELECT 1 FROM plants p JOIN plant_type_translations tr",
        "    ON tr.plant_type_id = p.plant_type_id AND tr.language = 'de'",
        "  JOIN asp_net_users u ON u.id = p.owner_id",
        "  WHERE u.normalized_email = upper(:'owner') AND tr.name = gt.type_de AND p.name = gt.name);",
        "COMMIT;",
        "",
    ]
    Path(args.out).write_text("\n".join(out), encoding="utf-8")
    print(f"{len(rows)} varieties with temperature, {len(skipped)} rows without temperature skipped -> {args.out}")
    for r, g, n in skipped:
        print(f"  skipped row {r}: {g} / {n}")


if __name__ == "__main__":
    sys.exit(main())
