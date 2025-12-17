#!/usr/bin/env python3
"""
Generate RimWorld DefInjected English translations for RecipeDef from an in-game export (Auto_CN.xml).

This script intentionally ignores any existing English translations and rewrites output from the
exported Chinese text, with consistent, RimWorld-ish phrasing:
- `label` has no trailing period.
- `jobString` is gerund-form and ends with a period.
- `description` is a sentence and ends with a period.
"""

from __future__ import annotations

import argparse
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
from xml.sax.saxutils import escape


QUOTE_MAP: dict[str, str] = {
    "猫猫": "Kitty",
    "猫猫冲锋队": "Kitty Assault Squad",
    "特战猫猫": "Special Ops Kitty",
    "猫猫劳工": "Kitty Laborer",
    "突击猫猫": "Assault Kitty",
    "地堡猫猫": "Kitty Bunker",
    "灵能泰坦": "Psititan",
    "战车": "Panzer",
    "喷火战车": "Flamethrower Panzer",
    "陆行舰": "Landstrider",
    "放射盾": "Radiant Shield",
    "破墙槌": "Wallbreaker",
    "地狱牙": "Hellfang",
    "三叉戟": "Trident",
    "角砾岩": "Breccia",
    "页岩": "Shale",
    "沸石": "Zeolite",
    "萤石": "Fluorite",
    "蛭石": "Vermiculite",
    "棱晶": "Prism",
    "金红石": "Rutile",
    "深渊": "Abyss",
    "鹅卵石": "Pebble",
    "磁石": "Magnetite",
    "链锯剑": "Chainsaw Sword",
    "钉头锤": "Spiked Mace",
    "装修锤": "Constructor Hammer",
    "铳枪": "Gunlance",
    "星岚": "Stellar Haze",
    "蓝锥": "Blue Cone",
    "熔岩": "Lava",
    "榍石": "Sphene",
    "黑曜石": "Obsidian",
    "鸣石": "Phonolite",
    "磷灰": "Apatite",
}


TERM_MAP: dict[str, str] = {
    "乌拉帝国": "Wula Empire ",
    "机械乌拉": "Wula synth",
    "合成人": "synth",
    "冲锋队帽子": "Assault Trooper helmet",
    "冲锋队装甲": "Assault Trooper armor",
    "冲锋队": "Assault Trooper ",
    "重装": "Heavy ",
    "头盔": "helmet",
    "装甲": "armor",
    "帽子": "hat",
    "连身黑丝": "black bodystocking",
    "连身白丝": "white bodystocking",
    "黑丝裤袜": "black pantyhose",
    "白丝裤袜": "white pantyhose",
    "女仆装": "maid outfit",
    "女仆发带": "maid headband",
    "黑色紧身衣": "black catsuit",
    "内舱壁墙": "bulkhead wall",
    "堡垒墙": "fortress wall",
    "微型传送装置": "Mini-Jumpdrive",
    "编织体": "weaver core",
    "零部件": "components",
    "精密装配台": "fabrication bench",
    "能源核心": "energy core",
    "充能": "recharge",
    "化合燃料": "chemfuel",
    "暗物质": "dark matter",
    "封装的暗物质": "packaged dark matter",
    "暗物质约束装置": "dark matter containment device",
    "暗物质武器": "dark-matter weapons",
    "大型设施": "large installations",
    "武备": "armaments",
    "关机": "shut down",
    "开机": "power up",
    "殖民者": "colonist",
    "合金": "alloy",
    "零素": "neutronium",
    "冠军甲胄": "champion armor",
    "茄子帽": "eggplant hat",
    "盾牌": "shield",
}


CUSTOM_SUMMARY_MAP = {
    "金属": "Metal",
    "纤维": "Textile",
}


OVERRIDES_BY_TAG: dict[str, str] = {
    "Make_Component_By_WULA_Cube_Productor.description": (
        "Make components using the Wula Empire weaver core. Takes longer and costs more resources "
        "than the fabrication bench."
    ),
    "Make_WULA_Charge_Cube.description": (
        "Make a Wula Empire energy core, including a rechargeable capacitor and the energy needed "
        "to power machinery. This is the only acceptable external energy source for Wula synths, "
        "and a precursor for many Wula Empire products."
    ),
    "Make_WULA_Dark_Matter_Item.description": (
        "Make 1 packaged dark matter, composed of a dark matter containment device and dark matter. "
        "A required energy source for Wula Empire large installations and armaments."
    ),
    "Make_WULA_Alloy.description": (
        "A high-density alloy processed from steel. A raw material for many Wula Empire products."
    ),
    "Make_WULA_Alloy_Group.description": (
        "A high-density alloy processed from steel. A raw material for many Wula Empire products."
    ),
    "Make_WULA_Neutronium.description": (
        "Make 1 neutronium. A powerful material that can be used to forge the hardest armor or the strongest melee weapons."
    ),
    "WULA_Build_Wula_Synth.description": (
        "Build a URa-00 \"Wula synth\". Wula synths are the main population of Wula Empire synth colonies, "
        "and as machines they also possess complex, lifelike simulated emotions."
    ),
    "WULA_Shutdown_Synth.description": (
        "Shut down all systems of this Wula synth to avoid potential risks for a while. "
        "A colonist must assist with the shutdown, and powering it back up also requires a colonist."
    ),
    "Recharge_WULA_Charge_Cube.description": "Use chemfuel to recharge a depleted Wula Empire energy core.",
    "Recharge_WULA_Charge_Cube.jobString": "Recharging energy core.",
    "Recharge_WULA_Charge_Cube.label": "Recharge energy core",
    "Recharge_WULA_Charge_Cube_Group.description": "Use chemfuel to recharge depleted Wula Empire energy cores.",
    "Recharge_WULA_Charge_Cube_Group.jobString": "Recharging energy cores.",
    "Recharge_WULA_Charge_Cube_Group.label": "Recharge energy core (x4)",
    "WULA_Build_Mech_WULA_Cat.description": "Build a CAt-11 \"Kitty\".",
    "WULA_Build_Mech_WULA_Cat_Assault.description": "Build a CAt-46 \"Kitty Assault Squad\".",
    "WULA_Build_Mech_WULA_Cat_Constructor.description": "Build a CAt-86 \"Kitty Laborer\".",
    "WULA_Shutdown_Synth.jobString": "Emergency shutdown.",
    "WULA_Shutdown_Synth.label": "Emergency shutdown",
    "WULA_Synth_Power_On.description": "Restart this synth and restore system operation.",
    "WULA_Synth_Power_On.jobString": "Restarting synth.",
    "WULA_Synth_Power_On.successfullyRemovedHediffMessage": "{0} successfully restarted {1}.",
}


def _replace_quoted_terms(text: str) -> str:
    def repl(match: re.Match[str]) -> str:
        inner = match.group(1)
        return '"' + QUOTE_MAP.get(inner, inner) + '"'

    return re.sub(r"\"([^\"]+)\"", repl, text)


def _apply_term_map(text: str) -> str:
    out = text
    for zh, en in sorted(TERM_MAP.items(), key=lambda kv: len(kv[0]), reverse=True):
        out = out.replace(zh, en)
    out = re.sub(r"\s+", " ", out).strip()
    return out


def _ensure_period(sentence: str) -> str:
    s = sentence.strip()
    if not s:
        return s
    if s.endswith((".", "!", "?")):
        return s
    return s + "."


def _strip_zh_period(text: str) -> str:
    return text.strip().removesuffix("。")


def translate_value(tag: str, cn: str) -> str:
    cn = (cn or "").replace("\r", "").strip()
    if not cn:
        return ""

    if tag.endswith(".filter.customSummary"):
        return CUSTOM_SUMMARY_MAP.get(cn, cn)

    if tag in OVERRIDES_BY_TAG:
        return OVERRIDES_BY_TAG[tag]

    cn = cn.replace("（10块）", " (x10)")
    cn = cn.replace("（无面罩）", "(No mask)")
    cn = cn.replace(" (周)", " (Weekly)")
    cn = cn.replace("(周)", "(Weekly)")
    cn = cn.replace("（4个）", " (x4)")

    cn = _replace_quoted_terms(cn)
    cn = _apply_term_map(cn)

    def strip_counters(x: str) -> str:
        x = x.strip()
        for prefix in ("一个", "一台", "1份", "1个", "1台"):
            if x.startswith(prefix):
                x = x[len(prefix) :].strip()
        return x

    def render(action: str, x: str) -> str:
        x = strip_counters(x)
        if action == "install":
            if tag.endswith(".label"):
                return f"Install {x}"
            if tag.endswith(".jobString"):
                return _ensure_period(f"Installing {x}")
            if tag.endswith(".description"):
                return _ensure_period(f"Install a {x}")
        if action == "make":
            if tag.endswith(".label"):
                return f"Make {x}"
            if tag.endswith(".jobString"):
                return _ensure_period(f"Making {x}")
            if tag.endswith(".description"):
                return _ensure_period(f"Make {x}")
        if action == "recharge":
            if tag.endswith(".label"):
                return f"Recharge {x}"
            if tag.endswith(".jobString"):
                return _ensure_period(f"Recharging {x}")
            if tag.endswith(".description"):
                return _ensure_period(f"Recharge {x}")
        if action == "forge":
            if tag.endswith(".label"):
                return f"Forge {x}"
            if tag.endswith(".jobString"):
                return _ensure_period(f"Forging {x}")
            if tag.endswith(".description"):
                return _ensure_period(f"Forge {x}")
        if action == "compress":
            if tag.endswith(".label"):
                return f"Compress {x}"
            if tag.endswith(".jobString"):
                return _ensure_period(f"Compressing {x}")
            if tag.endswith(".description"):
                return _ensure_period(f"Compress {x}")
        if action == "build":
            if tag.endswith(".label"):
                return f"Build {x}"
            if tag.endswith(".jobString"):
                return _ensure_period(f"Building {x}")
            if tag.endswith(".description"):
                return _ensure_period(f"Build {x}")
        if action == "shutdown":
            if tag.endswith(".label"):
                return f"Shut down {x}"
            if tag.endswith(".jobString"):
                return _ensure_period(f"Shutting down {x}")
            if tag.endswith(".description"):
                return _ensure_period(f"Shut down {x}")
        return x

    patterns: list[tuple[re.Pattern[str], str]] = [
        (re.compile(r"^安装一个(.+?)。?$"), "install"),
        (re.compile(r"^安装(.+?)$"), "install"),
        (re.compile(r"^正在安装(.+?)。?$"), "install"),
        (re.compile(r"^制作(.+?)。?$"), "make"),
        (re.compile(r"^正在制作(.+?)。?$"), "make"),
        (re.compile(r"^制造(.+?)。?$"), "make"),
        (re.compile(r"^正在制造(.+?)。?$"), "make"),
        (re.compile(r"^充能(.+?)$"), "recharge"),
        (re.compile(r"^正在为(.+?)充能。?$"), "recharge"),
        (re.compile(r"^锻造(.+?)。?$"), "forge"),
        (re.compile(r"^正在锻造(.+?)$"), "forge"),
        (re.compile(r"^压缩(.+?)。?$"), "compress"),
        (re.compile(r"^正在压缩(.+?)。?$"), "compress"),
        (re.compile(r"^建造(.+?)。?$"), "build"),
        (re.compile(r"^正在建造(.+?)。?$"), "build"),
        (re.compile(r"^将(.+?)的所有系统关闭.*$"), "shutdown"),
    ]

    for rx, action in patterns:
        m = rx.match(cn)
        if m:
            x = m.group(1).strip()
            return render(action, x)

    # Fallback handling for labels/jobStrings/descriptions:
    if tag.endswith(".label"):
        return _strip_zh_period(cn)
    if tag.endswith(".jobString"):
        return _ensure_period(cn)
    if tag.endswith(".description"):
        return _ensure_period(cn)
    return cn


def build_language_data_xml(tags_and_text: list[tuple[str, str]]) -> str:
    lines = ['<?xml version="1.0" encoding="utf-8"?>', "<LanguageData>"]
    for tag, value in tags_and_text:
        escaped_value = escape(value, entities={"\"": "&quot;"})
        lines.append(f"  <{tag}>{escaped_value}</{tag}>")
    lines.append("</LanguageData>")
    lines.append("")
    return "\n".join(lines)


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--export-auto-cn", required=True, help="Path to Auto_CN.xml for RecipeDef export")
    parser.add_argument("--out", required=True, help="Output xml file path")
    args = parser.parse_args(argv)

    export_path = Path(args.export_auto_cn)
    out_path = Path(args.out)

    root = ET.parse(export_path).getroot()
    tags_and_text: list[tuple[str, str]] = []
    for elem in list(root):
        tags_and_text.append((elem.tag, translate_value(elem.tag, elem.text or "")))

    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(build_language_data_xml(tags_and_text), encoding="utf-8", newline="\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
