#!/usr/bin/env python3
"""
Sync/repair BackstoryDef DefInjected English files by copying known-good strings from:
- AlienRace.AlienBackstoryDef translation file (for backstory titles/descriptions)
- HediffDefs translation file (for *_Hediff.* entries)

This fixes mojibake/untranslated fragments in older BackstoryDefs/Solid files.
"""

from __future__ import annotations

import argparse
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def read_langdata(path: Path) -> dict[str, str]:
    root = ET.parse(path).getroot()
    return {c.tag: (c.text or "") for c in root}


def write_langdata(path: Path, mapping: dict[str, str], keep_order_from: list[str]) -> None:
    root = ET.Element("LanguageData")
    for tag in keep_order_from:
        el = ET.SubElement(root, tag)
        el.text = mapping.get(tag, "")
    tree = ET.ElementTree(root)
    ET.indent(tree, space="  ", level=0)
    path.parent.mkdir(parents=True, exist_ok=True)
    tree.write(path, encoding="utf-8", xml_declaration=True)


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--alien-backstories", type=Path, required=True)
    ap.add_argument("--hediff", type=Path, required=True)
    ap.add_argument("--solid-child", type=Path, required=True)
    ap.add_argument("--solid-adult", type=Path, required=True)
    args = ap.parse_args(argv)

    alien = read_langdata(args.alien_backstories)
    hediff = read_langdata(args.hediff)

    def sync_file(path: Path, special_hediff: bool) -> int:
        root = ET.parse(path).getroot()
        tags = [c.tag for c in root]
        updated = 0
        out = {}
        for t in tags:
            if special_hediff and ("_Hediff." in t) and (t in hediff):
                out[t] = hediff[t]
                updated += 1
                continue
            if t in alien:
                out[t] = alien[t]
                updated += 1
                continue
            # keep original if no source found
            out[t] = (root.find(t).text or "") if root.find(t) is not None else ""
        write_langdata(path, out, tags)
        return updated

    u1 = sync_file(args.solid_child, special_hediff=True)
    u2 = sync_file(args.solid_adult, special_hediff=False)
    print(f"updated_solid_child={u1} updated_solid_adult={u2}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))

