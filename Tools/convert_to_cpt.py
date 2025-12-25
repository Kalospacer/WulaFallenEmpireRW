#!/usr/bin/env python3
"""
RimWorld 代码转 CPT 训练数据脚本
将 C# 源码和 XML Defs 转换为 JSONL 格式用于继续预训练
支持智能分割超长文件
"""

import os
import json
import re
import argparse
from pathlib import Path
from typing import Generator, List

# 排除的噪声文件（Unity 自动生成、二进制数据等）
EXCLUDED_FILES = {
    'UnitySourceGeneratedAssemblyMonoScriptTypes_v1.txt',
    '__JobReflectionRegistrationOutput__',
    'AssemblyInfo.cs',
    '.csproj',
}

def should_exclude(file_path: Path) -> bool:
    """检查文件是否应该被排除"""
    name = file_path.name
    for pattern in EXCLUDED_FILES:
        if pattern in name:
            return True
    return False

def read_file_content(file_path: Path) -> str:
    """读取文件内容，处理多种编码"""
    encodings = ['utf-8', 'utf-8-sig', 'gbk', 'latin-1']
    for encoding in encodings:
        try:
            with open(file_path, 'r', encoding=encoding) as f:
                return f.read()
        except UnicodeDecodeError:
            continue
    return None

def split_csharp_content(content: str, max_length: int) -> List[str]:
    """智能分割 C# 代码，尽量在类/方法边界分割"""
    if len(content) <= max_length:
        return [content]
    
    chunks = []
    # 按类定义分割
    class_pattern = r'(?=\n(?:public|private|internal|protected)?\s*(?:static\s+)?(?:partial\s+)?class\s+\w+)'
    parts = re.split(class_pattern, content)
    
    current_chunk = ""
    for part in parts:
        if len(current_chunk) + len(part) <= max_length:
            current_chunk += part
        else:
            if current_chunk:
                chunks.append(current_chunk.strip())
            # 如果单个 part 仍然太长，按行分割
            if len(part) > max_length:
                lines = part.split('\n')
                current_chunk = ""
                for line in lines:
                    if len(current_chunk) + len(line) + 1 <= max_length:
                        current_chunk += line + '\n'
                    else:
                        if current_chunk:
                            chunks.append(current_chunk.strip())
                        current_chunk = line + '\n'
            else:
                current_chunk = part
    
    if current_chunk:
        chunks.append(current_chunk.strip())
    
    return [c for c in chunks if len(c) > 100]  # 过滤太短的块

def split_xml_content(content: str, max_length: int) -> List[str]:
    """智能分割 XML 内容，尽量在 Def 边界分割"""
    if len(content) <= max_length:
        return [content]
    
    chunks = []
    # 按顶级 Def 元素分割
    def_pattern = r'(?=\n\s*<[A-Z][a-zA-Z]*Def\s)'
    parts = re.split(def_pattern, content)
    
    current_chunk = ""
    for part in parts:
        if len(current_chunk) + len(part) <= max_length:
            current_chunk += part
        else:
            if current_chunk:
                chunks.append(current_chunk.strip())
            if len(part) > max_length:
                # 强制按长度分割
                for i in range(0, len(part), max_length - 200):
                    chunk = part[i:i + max_length - 200]
                    if len(chunk) > 100:
                        chunks.append(chunk.strip())
                current_chunk = ""
            else:
                current_chunk = part
    
    if current_chunk:
        chunks.append(current_chunk.strip())
    
    return [c for c in chunks if len(c) > 100]

def create_csharp_entries(file_path: Path, content: str, max_length: int) -> List[dict]:
    """创建 C# 代码的训练条目（支持分割）"""
    relative_path = file_path.name
    chunks = split_csharp_content(content, max_length - 50)  # 留空间给 header
    
    entries = []
    for i, chunk in enumerate(chunks):
        if len(chunks) > 1:
            header = f"// File: {relative_path} (Part {i+1}/{len(chunks)})\n"
        else:
            header = f"// File: {relative_path}\n"
        entries.append({"text": header + chunk})
    
    return entries

def create_xml_entries(file_path: Path, content: str, max_length: int) -> List[dict]:
    """创建 XML 的训练条目（支持分割）"""
    relative_path = file_path.name
    chunks = split_xml_content(content, max_length - 50)
    
    entries = []
    for i, chunk in enumerate(chunks):
        if len(chunks) > 1:
            header = f"<!-- File: {relative_path} (Part {i+1}/{len(chunks)}) -->\n"
        else:
            header = f"<!-- File: {relative_path} -->\n"
        entries.append({"text": header + chunk})
    
    return entries

def process_csharp_files(source_dir: Path, max_length: int) -> Generator[dict, None, None]:
    """处理所有 C# 文件"""
    for ext in ['*.cs', '*.txt']:
        for file_path in source_dir.rglob(ext):
            if file_path.stat().st_size < 100:
                continue
            if should_exclude(file_path):
                continue
            content = read_file_content(file_path)
            if not content:
                continue
            if ext == '*.txt' and not ('class ' in content or 'namespace ' in content or 'public ' in content):
                continue
            for entry in create_csharp_entries(file_path, content, max_length):
                yield entry

def process_xml_files(source_dir: Path, max_length: int) -> Generator[dict, None, None]:
    """处理所有 XML 文件"""
    for file_path in source_dir.rglob('*.xml'):
        if file_path.stat().st_size < 100:
            continue
        content = read_file_content(file_path)
        if content:
            for entry in create_xml_entries(file_path, content, max_length):
                yield entry

def main():
    parser = argparse.ArgumentParser(description='转换 RimWorld 代码为 CPT 训练数据')
    parser.add_argument('--csharp-dir', type=str, help='C# 反编译代码目录')
    parser.add_argument('--xml-dir', type=str, help='XML Defs 目录')
    parser.add_argument('--output', type=str, default='rimworld_cpt_data.jsonl', help='输出文件路径')
    parser.add_argument('--max-length', type=int, default=8000, help='单条数据最大字符数')
    
    args = parser.parse_args()
    
    entries = []
    stats = {'csharp': 0, 'xml': 0, 'split_chunks': 0}
    
    # 处理 C# 文件
    if args.csharp_dir:
        csharp_path = Path(args.csharp_dir)
        if csharp_path.exists():
            print(f"处理 C# 文件: {csharp_path}")
            for entry in process_csharp_files(csharp_path, args.max_length):
                entries.append(entry)
                if "(Part " in entry['text'][:100]:
                    stats['split_chunks'] += 1
                else:
                    stats['csharp'] += 1
    
    # 处理 XML 文件
    if args.xml_dir:
        xml_path = Path(args.xml_dir)
        if xml_path.exists():
            print(f"处理 XML 文件: {xml_path}")
            for entry in process_xml_files(xml_path, args.max_length):
                entries.append(entry)
                if "(Part " in entry['text'][:100]:
                    stats['split_chunks'] += 1
                else:
                    stats['xml'] += 1
    
    # 写入输出文件
    output_path = Path(args.output)
    with open(output_path, 'w', encoding='utf-8') as f:
        for entry in entries:
            f.write(json.dumps(entry, ensure_ascii=False) + '\n')
    
    # 统计信息
    total_size = output_path.stat().st_size / (1024 * 1024)
    print(f"\n=== 转换完成 ===")
    print(f"C# 完整文件: {stats['csharp']}")
    print(f"XML 完整文件: {stats['xml']}")
    print(f"分割产生的块: {stats['split_chunks']}")
    print(f"总条目数: {len(entries)}")
    print(f"输出文件: {output_path}")
    print(f"文件大小: {total_size:.2f} MB")

if __name__ == '__main__':
    main()
