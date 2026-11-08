#!/usr/bin/env python3
"""One-time import of pots, saucers and raised beds of one user from CSV files into PostgreSQL.

Writes a SQL file; the script does not connect to the database itself. The CSV files hold
personal data, so keep them in the folder data/ (not in git).

Files (separator ;, UTF-8):
    pots.csv         kind;shape;top_cm;bottom_cm;height_cm;quantity;set_size;color;code;note;saucer;uses
                       kind    Pot or Bowl
                       shape   Round or Rectangular
                       saucer  diameter and colour of the saucer, e.g. "34 grau" (must be in saucers.csv)
                       uses    plant types in these pots: "Strawberry:12|Grapevine:2"
                               (English plant type name, number of single pots).
                               Plants that grow together in the same pots are joined with +:
                               "Cranberry+Strawberry:2" (two pots, each with both plants)
    saucers.csv      diameter_cm;color;quantity;note
    raised_beds.csv  name;shape;width_cm;length_cm;height_cm;note      (shape: Oval or Rectangular)

The user must have registered in the application first (the owner is found by e-mail address).
The plant types must exist (import reference and extra-types first).

Usage:
    python tools/import_containers.py --owner-email you@example.com
    python tools/import_containers.py --owner-email you@example.com --data-dir data --out data/containers.sql
"""
import argparse
import csv
import sys
from pathlib import Path

KINDS = {"Pot", "Bowl"}
POT_SHAPES = {"Round", "Rectangular"}
BED_SHAPES = {"Oval", "Rectangular"}


def lit(value):
    if value is None:
        return "NULL"
    if isinstance(value, (int, float)):
        return repr(value)
    return "'" + str(value).replace("'", "''") + "'"


def read_rows(path, required):
    with open(path, encoding="utf-8-sig", newline="") as f:
        reader = csv.DictReader(f, delimiter=";")
        missing = [c for c in required if c not in (reader.fieldnames or [])]
        if missing:
            sys.exit(f"{path}: missing columns {', '.join(missing)}")
        return [(i, {k: (v or "").strip() for k, v in row.items()}) for i, row in enumerate(reader, start=2)]


def decimal(value, where, errors, required=False, minimum=0.0):
    if value == "":
        if required:
            errors.append(f"{where}: value missing")
        return None
    try:
        number = float(value.replace(",", "."))
    except ValueError:
        errors.append(f"{where}: '{value}' is not a number")
        return None
    if number <= minimum:
        errors.append(f"{where}: must be greater than {minimum:g}")
        return None
    return number


def integer(value, where, errors, minimum):
    try:
        number = int(value)
    except ValueError:
        errors.append(f"{where}: '{value}' is not a whole number")
        return None
    if number < minimum:
        errors.append(f"{where}: must be at least {minimum}")
        return None
    return number


def key(diameter, color):
    return f"{diameter:g}|{color.lower()}"


def read_saucers(path, errors):
    saucers = []
    seen = set()
    for i, row in read_rows(path, ["diameter_cm", "color", "quantity", "note"]):
        where = f"saucers.csv row {i}"
        diameter = decimal(row["diameter_cm"], f"{where}, diameter_cm", errors, required=True)
        quantity = integer(row["quantity"], f"{where}, quantity", errors, 0)
        if diameter is None or quantity is None:
            continue
        k = key(diameter, row["color"])
        if k in seen:
            errors.append(f"{where}: duplicate saucer {diameter:g} {row['color']}")
            continue
        seen.add(k)
        saucers.append({"diameter": diameter, "color": row["color"] or None,
                        "quantity": quantity, "note": row["note"] or None})
    return saucers


def parse_saucer_ref(text, where, errors):
    """'34 grau' gives (34.0, 'grau')."""
    parts = text.split(None, 1)
    try:
        return float(parts[0].replace(",", ".")), (parts[1] if len(parts) > 1 else "")
    except (ValueError, IndexError):
        errors.append(f"{where}: cannot read saucer '{text}' (expected e.g. '34 grau')")
        return None


def read_pots(path, saucers, errors):
    saucer_keys = {key(s["diameter"], s["color"] or "") for s in saucers}
    pots = []
    for i, row in read_rows(path, ["kind", "shape", "top_cm", "bottom_cm", "height_cm", "quantity",
                                   "set_size", "color", "code", "note", "saucer", "uses"]):
        where = f"pots.csv row {i}"
        before = len(errors)
        if row["kind"] not in KINDS:
            errors.append(f"{where}: kind must be Pot or Bowl, not '{row['kind']}'")
        if row["shape"] not in POT_SHAPES:
            errors.append(f"{where}: shape must be Round or Rectangular, not '{row['shape']}'")
        quantity = integer(row["quantity"], f"{where}, quantity", errors, 0)
        set_size = integer(row["set_size"] or "1", f"{where}, set_size", errors, 1)

        saucer = None
        if row["saucer"]:
            ref = parse_saucer_ref(row["saucer"], where, errors)
            if ref:
                if key(*ref) not in saucer_keys:
                    errors.append(f"{where}: saucer '{row['saucer']}' is not in saucers.csv")
                else:
                    saucer = ref

        uses = []   # (list of plant type names, number of pots)
        for part in filter(None, (p.strip() for p in row["uses"].split("|"))):
            names, _, count = part.rpartition(":")
            n = integer(count, f"{where}, uses '{part}'", errors, 1) if names else None
            plants = [x.strip() for x in names.split("+") if x.strip()]
            if not plants:
                errors.append(f"{where}: use '{part}' must look like 'Strawberry:12'")
            elif n is not None:
                uses.append((plants, n))
        if quantity is not None and sum(n for _, n in uses) > quantity:
            errors.append(f"{where}: {sum(n for _, n in uses)} pots in use, but only {quantity} pots")

        if len(errors) > before:
            continue
        pots.append({
            "kind": row["kind"], "shape": row["shape"],
            "top": decimal(row["top_cm"], f"{where}, top_cm", errors),
            "bottom": decimal(row["bottom_cm"], f"{where}, bottom_cm", errors),
            "height": decimal(row["height_cm"], f"{where}, height_cm", errors),
            "quantity": quantity, "set_size": set_size,
            "color": row["color"] or None, "code": row["code"] or None, "note": row["note"] or None,
            "saucer": saucer, "uses": uses,
        })
    return pots


def read_beds(path, errors):
    beds, seen = [], set()
    for i, row in read_rows(path, ["name", "shape", "width_cm", "length_cm", "height_cm", "note"]):
        where = f"raised_beds.csv row {i}"
        before = len(errors)
        if not row["name"]:
            errors.append(f"{where}: name missing")
        elif row["name"].lower() in seen:
            errors.append(f"{where}: duplicate name '{row['name']}'")
        seen.add(row["name"].lower())
        if row["shape"] not in BED_SHAPES:
            errors.append(f"{where}: shape must be Oval or Rectangular, not '{row['shape']}'")
        width = decimal(row["width_cm"], f"{where}, width_cm", errors, required=True)
        length = decimal(row["length_cm"], f"{where}, length_cm", errors, required=True)
        height = decimal(row["height_cm"], f"{where}, height_cm", errors, required=True)
        if len(errors) > before:
            continue
        beds.append({"name": row["name"], "shape": row["shape"], "width": width,
                     "length": length, "height": height, "note": row["note"] or None})
    return beds


def insert(table, columns, values):
    return f"INSERT INTO {table} ({', '.join(columns)}) VALUES ({', '.join(values)});"


def write_sql(path, owner_email, saucers, pots, beds):
    owner = ("(SELECT id FROM asp_net_users WHERE normalized_email = "
             f"{lit(owner_email.strip().upper())})")
    used_types = sorted({name for p in pots for names, _ in p["uses"] for name in names})
    types = ", ".join(lit(n) for n in used_types) or "NULL"

    out = [
        "-- Generated by import_containers.py. One-time import of pots, saucers and raised beds.",
        "SET client_encoding = 'UTF8';",
        "BEGIN;",
        "",
        "DO $$",
        "DECLARE missing text;",
        "BEGIN",
        f"  IF NOT EXISTS (SELECT 1 FROM asp_net_users WHERE normalized_email = {lit(owner_email.strip().upper())}) THEN",
        "    RAISE EXCEPTION 'No user with this e-mail address; register in the application first.';",
        "  END IF;",
        f"  IF EXISTS (SELECT 1 FROM pots WHERE owner_id = {owner}) OR EXISTS (SELECT 1 FROM saucers WHERE owner_id = {owner})"
        f" OR EXISTS (SELECT 1 FROM raised_beds WHERE owner_id = {owner}) THEN",
        "    RAISE EXCEPTION 'This user already has pots, saucers or raised beds; import aborted.';",
        "  END IF;",
        "  SELECT string_agg(n, ', ' ORDER BY n) INTO missing",
        f"  FROM unnest(ARRAY[{types}]::text[]) AS n",
        "  WHERE NOT EXISTS (SELECT 1 FROM plant_types WHERE name = n);",
        "  IF missing IS NOT NULL THEN",
        "    RAISE EXCEPTION 'Unknown plant types: %', missing;",
        "  END IF;",
        "END $$;",
        "",
        "-- Saucers",
    ]
    for s in saucers:
        out.append(insert("saucers", ["owner_id", "diameter_cm", "color", "quantity", "note"],
                          [owner, lit(s["diameter"]), lit(s["color"]), lit(s["quantity"]), lit(s["note"])]))

    out += ["", "-- Pots"]
    for p in pots:
        if p["saucer"]:
            color_cond = "color = " + lit(p["saucer"][1]) if p["saucer"][1] else "color IS NULL"
            saucer_id = (f"(SELECT id FROM saucers WHERE owner_id = {owner} "
                         f"AND diameter_cm = {lit(p['saucer'][0])} AND {color_cond})")
        else:
            saucer_id = "NULL"
        out.append(insert(
            "pots",
            ["owner_id", "kind", "shape", "top_cm", "bottom_cm", "height_cm", "quantity", "set_size",
             "color", "code", "note", "saucer_id"],
            [owner, lit(p["kind"]), lit(p["shape"]), lit(p["top"]), lit(p["bottom"]), lit(p["height"]),
             lit(p["quantity"]), lit(p["set_size"]), lit(p["color"]), lit(p["code"]), lit(p["note"]), saucer_id]))

    out += ["", "-- Pots in use (one entry can hold several plants)"]
    for p in pots:
        for names, count in p["uses"]:
            pot_id = (f"(SELECT id FROM pots WHERE owner_id = {owner} "
                      f"AND shape = {lit(p['shape'])} AND quantity = {lit(p['quantity'])} "
                      f"AND top_cm IS NOT DISTINCT FROM {lit(p['top'])} "
                      f"AND bottom_cm IS NOT DISTINCT FROM {lit(p['bottom'])} "
                      f"AND height_cm IS NOT DISTINCT FROM {lit(p['height'])} "
                      f"AND kind = {lit(p['kind'])} ORDER BY id LIMIT 1)")
            type_names = ", ".join(lit(n) for n in names)
            out.append(f"WITH u AS (INSERT INTO pot_uses (pot_id, quantity) VALUES ({pot_id}, {lit(count)}) RETURNING id)")
            out.append("INSERT INTO pot_use_plants (pot_use_id, plant_type_id)")
            out.append(f"SELECT u.id, t.id FROM u, plant_types t WHERE t.name IN ({type_names});")

    out += ["", "-- Raised beds"]
    for b in beds:
        out.append(insert("raised_beds",
                          ["owner_id", "name", "shape", "width_cm", "length_cm", "height_cm", "note"],
                          [owner, lit(b["name"]), lit(b["shape"]), lit(b["width"]), lit(b["length"]),
                           lit(b["height"]), lit(b["note"])]))

    out += ["", "COMMIT;", ""]
    Path(path).write_text("\n".join(out), encoding="utf-8", newline="\n")


def main():
    parser = argparse.ArgumentParser(description="Create a SQL import file for pots, saucers and raised beds.")
    parser.add_argument("--owner-email", required=True, help="e-mail address of the registered user")
    parser.add_argument("--data-dir", default="data", help="folder with pots.csv, saucers.csv, raised_beds.csv")
    parser.add_argument("--out", default="data/containers.sql", help="SQL file to write")
    args = parser.parse_args()

    folder = Path(args.data_dir)
    errors = []
    saucers = read_saucers(folder / "saucers.csv", errors)
    pots = read_pots(folder / "pots.csv", saucers, errors)
    beds = read_beds(folder / "raised_beds.csv", errors)

    print(f"Saucers: {len(saucers)} ({sum(s['quantity'] for s in saucers)} pieces)")
    print(f"Pot groups: {len(pots)} ({sum(p['quantity'] for p in pots)} pots, "
          f"{sum(n for p in pots for _, n in p['uses'])} in use)")
    print(f"Raised beds: {len(beds)}")
    if errors:
        print("\nErrors:")
        for e in errors:
            print(f"  {e}")
        sys.exit("\nNo SQL written: fix the errors above first.")

    Path(args.out).parent.mkdir(parents=True, exist_ok=True)
    write_sql(args.out, args.owner_email, saucers, pots, beds)
    print(f"\nSQL written to {args.out}")


if __name__ == "__main__":
    main()
