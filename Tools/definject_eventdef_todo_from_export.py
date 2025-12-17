import argparse
import glob
import os
import xml.etree.ElementTree as ET
from pathlib import Path
from xml.sax.saxutils import escape


def load_language_data_files(folder: Path) -> dict[str, ET.Element]:
    merged: dict[str, ET.Element] = {}
    for file_path in glob.glob(str(folder / "**" / "*.xml"), recursive=True):
        try:
            root = ET.parse(file_path).getroot()
        except Exception:
            continue
        if root.tag != "LanguageData":
            continue
        for child in list(root):
            merged[child.tag] = child
    return merged


def element_text_or_empty(elem: ET.Element | None) -> str:
    if elem is None:
        return ""
    return (elem.text or "").strip()


def build_output_lines(export_root: ET.Element, existing_map: dict[str, ET.Element]) -> list[str]:
    lines = ['<?xml version="1.0" encoding="utf-8"?>', "<LanguageData>"]

    for export_elem in list(export_root):
        key = export_elem.tag
        is_list = len(list(export_elem)) > 0

        if not is_list:
            existing = existing_map.get(key)
            value = element_text_or_empty(existing) if existing is not None else "TODO"
            lines.append(f"  <{key}>{escape(value, entities={'\"': '&quot;'})}</{key}>")
            continue

        # List value: expect <li> children in export.
        existing = existing_map.get(key)
        if existing is not None and len(list(existing)) > 0:
            items = [(li.text or "") for li in list(existing)]
        else:
            # Try to reconstruct from indexed keys like foo.descriptions.0, foo.descriptions.1 ...
            items = []
            prefix = key + "."
            idx = 0
            while True:
                indexed_key = f"{prefix}{idx}"
                indexed_elem = existing_map.get(indexed_key)
                if indexed_elem is None:
                    break
                items.append(element_text_or_empty(indexed_elem))
                idx += 1

        if not items:
            # Keep list structure but mark TODO inside.
            items = ["TODO"]

        lines.append(f"  <{key}>")
        for item in items:
            lines.append(f"    <li>{escape(item, entities={'\"': '&quot;'})}</li>")
        lines.append(f"  </{key}>")

    lines.append("</LanguageData>")
    lines.append("")
    return lines


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--export-auto-cn", required=True, help="Path to Auto_CN.xml for WulaFallenEmpire.EventDef export")
    parser.add_argument("--existing-folder", required=True, help="Existing mod DefInjected folder for WulaFallenEmpire.EventDefs")
    parser.add_argument("--out", required=True, help="Output xml file path")
    args = parser.parse_args()

    export_path = Path(args.export_auto_cn)
    existing_folder = Path(args.existing_folder)
    out_path = Path(args.out)

    export_root = ET.parse(export_path).getroot()
    existing_map = load_language_data_files(existing_folder)

    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text("\n".join(build_output_lines(export_root, existing_map)), encoding="utf-8", newline="\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

