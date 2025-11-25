# -*- coding: utf-8 -*-
import os
import sys
import logging
import json
import re
import time
import glob
from http import HTTPStatus
from collections import defaultdict
from typing import List, Dict, Any, Optional, Tuple
import threading

import dashscope
from tenacity import retry, stop_after_attempt, wait_random_exponential
from dotenv import load_dotenv
from mcp.server.fastmcp import FastMCP
import xml.etree.ElementTree as ET

# 1. --- 配置与初始化 ---

# 路径配置
MCP_DIR = os.path.dirname(os.path.abspath(__file__))
LOG_FILE_PATH = os.path.join(MCP_DIR, 'rimworld_rag.log')

# 设置日志
logging.basicConfig(filename=LOG_FILE_PATH, level=logging.INFO,
                   format='%(asctime)s - %(levelname)s - %(message)s',
                   encoding='utf-8')

# 加载环境变量
load_dotenv(os.path.join(MCP_DIR, '.env'))
DASHSCOPE_API_KEY = os.getenv("DASHSCOPE_API_KEY")
if not DASHSCOPE_API_KEY:
    logging.warning("Missing DASHSCOPE_API_KEY. Semantic reranking will be disabled.")
else:
    dashscope.api_key = DASHSCOPE_API_KEY

# 数据根目录
# 根据之前的 list_files 结果调整路径
RIMWORLD_DATA_ROOT = os.path.abspath(os.path.join(MCP_DIR, "..", "..", "..", "Data"))
RIMWORLD_SOURCE_ROOT = os.path.abspath(os.path.join(MCP_DIR, "..", "..", "..", "dll1.6"))

logging.info(f"Data Root: {RIMWORLD_DATA_ROOT}")
logging.info(f"Source Root: {RIMWORLD_SOURCE_ROOT}")

# 2. --- 核心索引类 (内存版) ---

class SymbolIndex:
    """
    内存索引构建器。
    替代 C# 项目中的 Lucene/MetadataStore。
    启动时扫描所有文件，建立 Symbol -> FilePath 的映射。
    """
    def __init__(self):
        self.symbol_map = {}  # symbol_id -> file_path
        self.files_cache = [] # 所有文件路径列表
        self.is_initialized = False
        self._lock = threading.Lock()

    def initialize(self):
        with self._lock:
            if self.is_initialized: return
            logging.info("正在构建内存索引...")
            start_t = time.time()

            # 1. 扫描 C# 源码
            if os.path.exists(RIMWORLD_SOURCE_ROOT):
                for root, _, files in os.walk(RIMWORLD_SOURCE_ROOT):
                    for file in files:
                        if file.endswith(('.cs', '.txt')):
                            full_path = os.path.join(root, file)
                            # 假设文件名即类名 (简化逻辑)
                            symbol = os.path.splitext(file)[0]
                            self.symbol_map[symbol] = full_path
                            self.files_cache.append(full_path)
            else:
                logging.warning(f"Source root not found: {RIMWORLD_SOURCE_ROOT}")

            # 2. 扫描 XML Defs
            # 扫描 Data 目录下的所有子目录
            if os.path.exists(RIMWORLD_DATA_ROOT):
                for root, _, files in os.walk(RIMWORLD_DATA_ROOT):
                    for file in files:
                        if file.endswith('.xml'):
                            full_path = os.path.join(root, file)
                            self.files_cache.append(full_path)
                            # 快速解析 XML 找 defName
                            try:
                                self._scan_xml_defs(full_path)
                            except Exception as e:
                                logging.warning(f"解析 XML 失败 {full_path}: {e}")
            else:
                logging.warning(f"Data root not found: {RIMWORLD_DATA_ROOT}")

            logging.info(f"索引构建完成，耗时 {time.time() - start_t:.2f}s，收录符号 {len(self.symbol_map)} 个")
            self.is_initialized = True

    def _scan_xml_defs(self, path):
        content = read_file_content(path)
        if not content: return
        
        # 使用正则快速提取 defName
        def_names = re.findall(r'<defName>(.*?)</defName>', content)
        for name in def_names:
            self.symbol_map[f"xml:{name}"] = path

    def search_symbols(self, keyword: str, kind: str = None) -> List[Tuple[str, str]]:
        """简单的关键词匹配"""
        results = []
        kw_lower = keyword.lower()
        for sym, path in self.symbol_map.items():
            if kind == 'csharp' and sym.startswith('xml:'): continue
            if kind == 'xml' and not sym.startswith('xml:'): continue
            
            if kw_lower in sym.lower():
                results.append((sym, path))
            # 移除硬限制，以便后续排序能找到最佳结果
            # if len(results) > 200: break
        return results

# 全局索引实例
global_index = SymbolIndex()

# 3. --- 辅助工具函数 ---

def read_file_content(path: str) -> str:
    """健壮的文件读取"""
    encodings = ['utf-8', 'utf-8-sig', 'gbk', 'latin-1']
    for enc in encodings:
        try:
            with open(path, 'r', encoding=enc) as f:
                return f.read()
        except UnicodeDecodeError:
            continue
        except Exception:
            continue
    return ""

def extract_xml_fragment(file_path: str, def_name: str) -> str:
    """提取 XML 中特定的 Def 块"""
    content = read_file_content(file_path)
    try:
        # 尝试正则匹配整个 Def 块
        # 假设 Def 格式为 <DefType defName="NAME">...</DefType> 或 <DefType><defName>NAME</defName>...</DefType>
        
        # 策略 1: 查找 <defName>NAME</defName>，然后向上找最近的 <DefType>
        pattern = r"<(\w+)\s*(?:Name=\"[^\"]*\")?>\s*<defName>" + re.escape(def_name) + r"</defName>"
        match = re.search(pattern, content)
        
        if not match:
             # 策略 2: 查找 defName="NAME"
             pattern = r"<(\w+)\s+[^>]*defName=\"" + re.escape(def_name) + r"\""
             match = re.search(pattern, content)

        if match:
            tag_name = match.group(1)
            # 找到开始位置
            start_pos = match.start()
            # 寻找对应的结束标签 </Tag>
            # 这是一个简化的查找，不支持嵌套同名标签，但在 RimWorld Defs 中通常足够
            end_tag = f"</{tag_name}>"
            end_pos = content.find(end_tag, start_pos)
            if end_pos != -1:
                return content[start_pos:end_pos + len(end_tag)]
    except Exception as e:
        logging.error(f"Error extracting XML fragment: {e}")
    
    # 降级方案：返回文件内容，如果太长则截断
    lines = content.split('\n')
    if len(lines) > 100:
        return "\n".join(lines[:100]) + "\n... (XML too long, truncated)"
    return content

def extract_csharp_fragment(file_path: str, symbol: str) -> str:
    """提取 C# 类或方法"""
    content = read_file_content(file_path)
    # 简化：直接返回整个文件，如果太大则截断
    lines = content.split('\n')
    if len(lines) > 500:
        return "\n".join(lines[:500]) + "\n\n// ... (File too long, truncated)"
    return content

# 4. --- 功能实现类 ---

class RoughSearcher:
    """模仿 C# 项目的 RoughSearcher，结合关键词过滤和语义重排序"""
    
    def __init__(self, config: Dict):
        self.config = config
        global_index.initialize()

    def search(self, query: str) -> List[Dict]:
        # 1. 粗筛：在文件名和 Symbol 中查找
        candidates = global_index.search_symbols(query, self.config.get('kind'))
        
        # 如果没有找到直接匹配，尝试更宽松的搜索（如分词）
        if not candidates:
            tokens = query.split()
            if len(tokens) > 1:
                candidates = global_index.search_symbols(tokens[0], self.config.get('kind'))

        if not candidates:
            return []

        # 优化：对候选结果进行预排序
        # 优先级：完全匹配 > 前缀匹配 > 长度更短 > 字母顺序
        candidates.sort(key=lambda x: (
            x[0].lower() != query.lower(),
            not x[0].lower().startswith(query.lower()),
            len(x[0]),
            x[0]
        ))

        # 2. 准备重排序文档
        docs = []
        valid_candidates = []
        
        # 增加重排数量到 50，提高召回率
        for sym, path in candidates[:50]:
            content = read_file_content(path)
            # 截取一部分内容用于语义判断
            snippet = content[:1000]
            docs.append(f"Title: {sym}\nContent: {snippet}")
            valid_candidates.append((sym, path))

        # 3. 使用 DashScope Rerank (如果配置了key)
        ranked_results = []
        if DASHSCOPE_API_KEY and docs:
            try:
                response = dashscope.TextReRank.call(
                    model='gte-rerank', 
                    query=query, 
                    documents=docs, 
                    top_n=self.config.get('max_results', 10)
                )
                if response.status_code == HTTPStatus.OK:
                    for res in response.output['results']:
                        idx = res['index']
                        sym, path = valid_candidates[idx]
                        score = res['score']
                        ranked_results.append(self._format_result(sym, path, score))
                else:
                    logging.error(f"Rerank API error: {response}")
                    # 降级：直接返回
                    ranked_results = [self._format_result(s, p, 0.5) for s, p in valid_candidates]
            except Exception as e:
                logging.error(f"Rerank exception: {e}")
                ranked_results = [self._format_result(s, p, 0.5) for s, p in valid_candidates]
        else:
             # 无 Key 模式：按名称长度排序作为简单 heuristic
             valid_candidates.sort(key=lambda x: len(x[0]))
             ranked_results = [self._format_result(s, p, 1.0) for s, p in valid_candidates[:self.config.get('max_results', 10)]]

        return ranked_results

    def _format_result(self, symbol, path, score):
        is_xml = symbol.startswith('xml:')
        return {
            "symbolId": symbol,
            "kind": "xml" if is_xml else "csharp",
            "symbolKind": "definition" if is_xml else "class",
            "path": path,
            "title": symbol,
            "score": float(score),
            "preview": "Use get_item to view content"
        }

class GraphQuerier:
    """
    模拟 C# 项目的 GraphQuerier。
    由于没有预计算的 .bin 图数据，这里使用实时解析 (Heuristic) 实现。
    """
    def __init__(self):
        global_index.initialize()

    def query_uses(self, symbol: str, kind: str = "all") -> List[Dict]:
        """查询下游依赖 (Symbol 引用了什么)"""
        if symbol not in global_index.symbol_map:
            return []
        
        file_path = global_index.symbol_map[symbol]
        content = read_file_content(file_path)
        edges = []

        if symbol.startswith('xml:'):
            # XML 解析逻辑
            # 1. 查找 <ParentName> (Inherits)
            parents = re.findall(r'ParentName="([^"]+)"', content)
            parents += re.findall(r'<ParentName>([^<]+)</ParentName>', content)
            for p in parents:
                edges.append({"target": f"xml:{p}", "kind": "Inherits"})
            
            # 2. 查找 <xxxClass> (BindsClass)
            classes = re.findall(r'<[\w]+Class>([\w\.]+)</[\w]+Class>', content) # 如 <thingClass>
            classes += re.findall(r'Class="([\w\.]+)"', content) # 如 <li Class="...">
            for c in classes:
                # 简单的命名空间推断
                target_cls = c
                if '.' not in c:
                    # 尝试推断，通常是 RimWorld 或 Verse
                    if f"RimWorld.{c}" in global_index.symbol_map: target_cls = f"RimWorld.{c}"
                    elif f"Verse.{c}" in global_index.symbol_map: target_cls = f"Verse.{c}"
                
                edges.append({"target": target_cls, "kind": "XmlBindsClass"})

            # 3. 查找 <defName> 引用 (References)
            potential_refs = re.findall(r'>([\w]+)</', content)
            for ref in potential_refs:
                xml_ref = f"xml:{ref}"
                if xml_ref in global_index.symbol_map and xml_ref != symbol:
                    edges.append({"target": xml_ref, "kind": "XmlReferences"})

        else:
            # C# 解析逻辑 (Regex)
            # 1. 继承 : BaseClass
            inherits = re.search(r'class\s+\w+\s*:\s*([\w\.]+)', content)
            if inherits:
                edges.append({"target": inherits.group(1), "kind": "Inherits"})
            
            # 2. 字段/方法调用 (References) - 简化版：查找所有大写开头的单词
            tokens = set(re.findall(r'\b[A-Z]\w+\b', content))
            for t in tokens:
                if t in global_index.symbol_map and t != symbol:
                    edges.append({"target": t, "kind": "References"})
                elif f"RimWorld.{t}" in global_index.symbol_map:
                    edges.append({"target": f"RimWorld.{t}", "kind": "References"})
                elif f"Verse.{t}" in global_index.symbol_map:
                    edges.append({"target": f"Verse.{t}", "kind": "References"})

        # 过滤 Kind
        if kind != 'all':
            filtered = []
            for e in edges:
                is_xml_target = e['target'].startswith('xml:')
                if kind == 'xml' and is_xml_target: filtered.append(e)
                elif kind == 'csharp' and not is_xml_target: filtered.append(e)
            return filtered
        
        return edges

    def query_used_by(self, symbol: str, kind: str = "all") -> List[Dict]:
        """
        查询上游依赖 (谁使用了 Symbol)。
        这个操作非常昂贵 (全文件扫描)，C# 使用了倒排索引。
        Python 版这里只能用 grep (glob scan) 模拟，速度会慢。
        """
        results = []
        search_term = symbol.replace('xml:', '')
        
        # 限制扫描文件数以防超时，优先扫描同类型文件
        scan_xml = kind in ['all', 'xml']
        scan_cs = kind in ['all', 'csharp']

        # 遍历所有已知文件进行文本匹配
        count = 0
        for file_path in global_index.files_cache:
            is_xml_file = file_path.endswith('.xml')
            if is_xml_file and not scan_xml: continue
            if not is_xml_file and not scan_cs: continue

            content = read_file_content(file_path)
            if search_term in content:
                # 找到了引用
                # 尝试反推该文件的 Symbol ID
                source_symbol = "Unknown"
                if is_xml_file:
                    # 尝试找 defName
                    m = re.search(r'<defName>(.*?)</defName>', content)
                    if m: source_symbol = f"xml:{m.group(1)}"
                else:
                    # C# 文件名即 Symbol
                    fname = os.path.basename(file_path)
                    source_symbol = os.path.splitext(fname)[0]

                if source_symbol != "Unknown" and source_symbol != symbol:
                    results.append({
                        "source": source_symbol,
                        "kind": "References" if not is_xml_file else "XmlReferences",
                        "distance": 1
                    })
                    count += 1
                    if count >= 50: break # 限制数量

        return results

# 5. --- MCP Server 定义 ---

mcp = FastMCP(name="rimworld-code-rag")

@mcp.tool()
def rough_search(query: str, kind: str = None, max_results: int = 20) -> Dict[str, Any]:
    """
    粗略搜索工具：使用自然语言查询 RimWorld 代码符号和 XML 定义。
    
    Args:
        query: 搜索关键词，如 "weapon gun" 或 "pawn health"
        kind: 过滤类型，可选 "csharp" (或 "cs") 或 "xml" (或 "def")
        max_results: 最大返回结果数
    """
    logging.info(f"rough_search: {query} (kind={kind})")
    searcher = RoughSearcher({"kind": kind, "max_results": max_results})
    results = searcher.search(query)
    
    return {
        "results": results,
        "totalFound": len(results)
    }

@mcp.tool()
def get_item(symbol: str, max_lines: int = 0) -> Dict[str, Any]:
    """
    精确检索工具：获取特定符号的完整源代码或 Definition。
    
    Args:
        symbol: 符号ID，例如 "RimWorld.Pawn" 或 "xml:Gun_Revolver"
        max_lines: 最大返回行数，0 表示不限制
    """
    logging.info(f"get_item: {symbol}")
    global_index.initialize()
    
    if symbol not in global_index.symbol_map:
        return {"error": f"未找到符号: {symbol}，请先使用 rough_search 确认名称。"}

    file_path = global_index.symbol_map[symbol]
    
    if symbol.startswith("xml:"):
        source_code = extract_xml_fragment(file_path, symbol.replace("xml:", ""))
    else:
        source_code = extract_csharp_fragment(file_path, symbol)

    # 行数限制
    lines = source_code.split('\n')
    if max_lines > 0 and len(lines) > max_lines:
        source_code = "\n".join(lines[:max_lines]) + f"\n... (剩余 {len(lines)-max_lines} 行已截断)"

    return {
        "symbolId": symbol,
        "path": file_path,
        "language": "xml" if symbol.startswith("xml:") else "csharp",
        "sourceCode": source_code,
        "totalLines": len(lines)
    }

@mcp.tool()
def get_uses(symbol: str, kind: str = "all", max_results: int = 50) -> Dict[str, Any]:
    """
    依赖分析：查找该符号引用了什么（下游依赖）。
    
    Args:
        symbol: 符号ID
        kind: 过滤目标类型 ("csharp", "xml", "all")
    """
    logging.info(f"get_uses: {symbol}")
    querier = GraphQuerier()
    edges = querier.query_uses(symbol, kind)
    
    return {
        "sourceSymbol": symbol,
        "edges": edges[:max_results],
        "total": len(edges)
    }

@mcp.tool()
def get_used_by(symbol: str, kind: str = "all", max_results: int = 20) -> Dict[str, Any]:
    """
    反向依赖分析：查找谁使用了该符号（上游依赖）。
    注意：由于没有预计算索引，此操作涉及文件扫描，可能较慢。
    
    Args:
        symbol: 符号ID
        kind: 过滤源类型 ("csharp", "xml", "all")
    """
    logging.info(f"get_used_by: {symbol}")
    querier = GraphQuerier()
    edges = querier.query_used_by(symbol, kind)
    
    return {
        "targetSymbol": symbol,
        "edges": edges[:max_results],
        "total": len(edges)
    }

if __name__ == "__main__":
    logging.info("RimWorld Code RAG Python Server Starting...")
    # 预热索引
    global_index.initialize()
    mcp.run()