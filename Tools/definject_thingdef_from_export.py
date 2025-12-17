#!/usr/bin/env python3
"""
Generate/normalize RimWorld DefInjected translations for ThingDef using an in-game export.

Workflow (this repo):
1) Use the in-game exporter to produce Auto_CN.xml (Chinese strings) for ThingDef.
2) Run this script to:
   - prune existing mod ThingDefs/*.xml to the export keyset (removes "extra" keys),
   - generate a new ZZZ_* file containing all "missing" keys translated to English (best-effort).

NOTE: The translation here is heuristic and intended to reduce manual work. Any remaining
Chinese text can be found via grep and fixed by hand.
"""

from __future__ import annotations

import argparse
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


_CJK_RE = re.compile(r"[\u4e00-\u9fff\u3040-\u30ff\uac00-\ud7af]")


_TERM_MAP = {
    "乌拉帝国": "Wula Empire",
    "乌拉": "Wula",
    "合成人": "synth",
    "充电站": "charging station",
    "维护舱": "maintenance pod",
    "地下维护站": "underground maintenance station",
    "轨道输送信标": "orbital transfer beacon",
    "运输舱": "transport pod",
    "物资输送舱": "supply pod",
    "物资回收舱": "recovery pod",
    "大门": "gate",
    "地板": "floor",
    "堡垒墙": "fortress wall",
    "掩体": "shelter",
    "暗物质发电机": "dark matter generator",
    "聚变发电机": "fusion generator",
    "火山炮": "volcano cannon",
    "反战车炮塔": "anti-vehicle turret",
    "激光炮塔": "laser turret",
    "迫击炮塔": "mortar turret",
    "预制件": "prefab",
    "空投": "airdrop",
    "信标": "beacon",
    "碉堡": "bunker",
    "要塞": "fortress",
    "大型": "large",
    "小型": "small",
    "前哨站": "outpost",
    "炮塔群": "turret group",
    "地堡": "bunker",
    "感应地雷": "proximity mine",
    "跃迁引擎": "teleport engine",
    "编织体": "weaver core",
    "作业通讯台": "operations comms console",
    "挖掘机": "excavator",
    "战斗挖掘机": "combat excavator",
    "陆行舰": "landstrider",
    "放射盾": "radiant shield",
    "灵能泰坦": "Psititan",
    "蛭石": "Vermiculite",
    "巡飞弹": "loitering munition",
    "猫猫": "Kitty",
    "猫猫冲锋队": "Kitty Assault Squad",
    "特战猫猫": "Special Ops Kitty",
    "猫猫劳工": "Kitty Laborer",
    "突击猫猫": "Assault Kitty",
    "兵蚁": "Ant Trooper",
    "战车": "Panzer",
    "喷火战车": "Flamethrower Panzer",
    "渡鸦": "Raven",
    "金红石": "Rutile",
    "棱晶": "Prism",
    "深渊": "Abyss",
    "鹅卵石": "Pebble",
    "磁石": "Magnetite",
    "奇怪的": "Strange",
    "空投区": "drop zone",
    "区域": "area",
    "中型": "medium",
    "微型": "mini",
    "突击护航舰": "assault escort ship",
    "桌子": "table",
    "旗帜": "flag",
    "帝国舰队": "Imperial Fleet",
    "轰炸机": "bomber",
    "蜂群无人机": "swarm drone",
    "攻击机": "striker",
    "迫击炮弹": "mortar shell",
    "迫击炮": "mortar",
    "等离子体": "plasma",
    "爆弹": "blast round",
    "铬铁": "chromite",
    "磷灰": "apatite",
    "机械乌拉": "Wula synth",
    "神人大鹅": "Legendary Goose",
    "落地中": "landing",
    "建造中": "building",
    "科研蓝图": "Techprint",
    "许可": "permit",
    "安装隐藏式天线": "Install concealed antenna",
    "帝国攻击舰队已抵达": "Imperial strike fleet has arrived",
    "帝国巡洋舰已抵达": "Imperial cruiser has arrived",
    "帝国母舰已抵达": "Imperial mothership has arrived",
    "帝国攻击舰队响应请求抵达殖民地上空！": "The Imperial strike fleet has arrived above the colony in response to your request!",
    "一艘帝国巡洋舰响应请求抵达殖民地上空！": "An Imperial cruiser has arrived above the colony in response to your request!",
    "一艘帝国母舰响应请求抵达殖民地上空！": "An Imperial mothership has arrived above the colony in response to your request!",
    "射程": "Range",
    "冲击半径": "Impact radius",
    "供电半径": "Power radius",
    "暗物质燃料": "Dark matter fuel",
    "需要填入封装的暗物质": "Requires packaged dark matter.",
    "石块": "Stone chunks",
    "需要填入石块": "Requires stone chunks.",
    "零部件": "Components",
    "磁力光束": "Magnetic beam",
    "双子魔眼": "Twin Demon Eyes",
    "魔眼": "Demon Eye",
    "月长石": "Moonstone",
    "青金石": "Lapis Lazuli",
    "火欧泊": "Fire Opal",
    "铱锇": "Iridosmium",
    "晶丛": "Crystal Cluster",
    "陨磷": "Meteoric Phosphorus",
    "横扫": "Sweep",
    "链锯": "Chainsaw",
    "槌头": "Hammerhead",
    "无法接触。": "Cannot be reached.",
}


def _title_case_simple(text: str) -> str:
    return " ".join(w[:1].upper() + w[1:] if w else "" for w in text.split())


def _apply_term_map(text: str) -> str:
    out = text
    for cn, en in sorted(_TERM_MAP.items(), key=lambda kv: len(kv[0]), reverse=True):
        out = out.replace(cn, en)
    return out


def translate_cn_to_en(text: str) -> str:
    raw = (text or "").replace("\r", "").strip()
    if not raw:
        return ""

    # Already English-ish or code; keep.
    if not _CJK_RE.search(raw):
        return raw

    # Blueprint labels: X（蓝图） -> X (Blueprint)
    raw = raw.replace("（蓝图）", " (Blueprint)")
    raw = raw.replace("（建造中）", " (building)")
    raw = raw.replace("（落地中）", " (landing)")

    # Corpse labels: "...尸体" -> "Corpse of ..."
    if raw.endswith("尸体") and "的尸体" not in raw:
        name = raw.removesuffix("尸体")
        name = _apply_term_map(name).strip()
        return f"Corpse of {name}"

    # Corpse descriptions: "...的尸体。" -> "The corpse of ..."
    if raw.endswith("的尸体。"):
        name = raw.removesuffix("的尸体。")
        name = _apply_term_map(name)
        return f"The corpse of {name}."

    # Common frame instruction prefix.
    raw = raw.replace(
        "清理出一块场地并准备好资源，使得乌拉帝国可以向此处投放建筑。",
        "Clear a landing zone and prepare the resources so the Wula Empire can airdrop a building here.",
    )
    raw = raw.replace(
        "清理出一块场地并准备好资源，使得乌拉帝国母舰可以向此处投放大型战争机械。",
        "Clear a landing zone and prepare the resources so the Wula Empire mothership can drop a large war machine here.",
    )
    raw = raw.replace(
        "清理出一块场地并准备好资源，使得乌拉帝国母舰可以向此处派遣一艘穿梭机。",
        "Clear a landing zone and prepare the resources so the Wula Empire mothership can dispatch a shuttle here.",
    )

    # Per-line term substitutions.
    lines = raw.split("\n")
    lines = [_apply_term_map(line) for line in lines]
    out = "\n".join(lines)

    # Quick polish for a few common lowercase nouns after mapping.
    out = out.replace("Wula Empire synth", "Wula Empire synth")
    out = out.replace("synth", "Synth")
    out = out.replace("comms", "comms")

    return out


def parse_langdata(path: Path) -> dict[str, str]:
    root = ET.parse(path).getroot()
    return {c.tag: (c.text or "") for c in root}


def write_langdata(path: Path, entries: list[tuple[str, str]]) -> None:
    root = ET.Element("LanguageData")
    for k, v in entries:
        el = ET.SubElement(root, k)
        el.text = v
    tree = ET.ElementTree(root)
    ET.indent(tree, space="  ", level=0)
    path.parent.mkdir(parents=True, exist_ok=True)
    tree.write(path, encoding="utf-8", xml_declaration=True)


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--export", type=Path, required=True, help="Exported Auto_CN.xml for ThingDef")
    ap.add_argument("--mod-dir", type=Path, required=True, help="Mod Languages/English/DefInjected/ThingDefs directory")
    ap.add_argument(
        "--write-missing",
        type=Path,
        required=True,
        help="Output path for generated missing translations (ZZZ_* file recommended)",
    )
    ap.add_argument(
        "--prune-existing",
        action="store_true",
        help="Rewrite existing ThingDefs/*.xml to only keep keys present in export",
    )
    args = ap.parse_args(argv)

    export_root = ET.parse(args.export).getroot()
    export_items = [(c.tag, (c.text or "").replace("\r", "")) for c in export_root]
    export_keys = [k for k, _ in export_items]
    export_set = set(export_keys)
    export_cn = {k: v for k, v in export_items}

    existing_files = sorted(args.mod_dir.glob("*.xml"))
    # If regenerating the missing file, do not treat the previous output as existing input.
    existing_files = [p for p in existing_files if p.resolve() != args.write_missing.resolve()]
    existing_by_file: dict[Path, dict[str, str]] = {}
    merged_existing: dict[str, str] = {}

    for f in existing_files:
        data = parse_langdata(f)
        existing_by_file[f] = data
        # simulate in-game merge by filename order
        for k, v in data.items():
            merged_existing[k] = v

    missing = [k for k in export_keys if k not in merged_existing]
    extra = sorted([k for k in merged_existing.keys() if k not in export_set])

    print(f"export_keys={len(export_keys)} present={len(export_keys)-len(missing)} missing={len(missing)} extra={len(extra)}")

    if args.prune_existing and extra:
        for f, data in existing_by_file.items():
            kept = [(k, v) for k, v in data.items() if k in export_set]
            if len(kept) == len(data):
                continue
            write_langdata(f, kept)
        print(f"pruned_existing_files={sum(1 for f,d in existing_by_file.items() if any(k not in export_set for k in d))}")

    missing_entries: list[tuple[str, str]] = []
    for k in export_keys:
        if k not in merged_existing:
            missing_entries.append((k, translate_cn_to_en(export_cn.get(k, ""))))

    write_langdata(args.write_missing, missing_entries)
    print(f"wrote_missing_file={args.write_missing} missing_entries={len(missing_entries)}")

    # Warn if any CJK remains.
    remain = [(k, v) for k, v in missing_entries if _CJK_RE.search(v or "")]
    print(f"missing_entries_with_cjk={len(remain)}")
    for k, v in remain[:20]:
        snippet = (v or "").strip().replace("\n", "\\n")
        print(f"CJK {k} -> {snippet[:120]}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
