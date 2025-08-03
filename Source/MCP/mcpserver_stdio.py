# -*- coding: utf-8 -*-
import os
import sys
import logging
import json

# 1. --- 导入库 ---
# mcp 库已通过 'pip install -e' 安装，无需修改 sys.path
from mcp.server.fastmcp import FastMCP

# 2. --- 日志和知识库配置 ---
MCP_DIR = os.path.dirname(os.path.abspath(__file__))
LOG_FILE_PATH = os.path.join(MCP_DIR, 'mcpserver.log')
logging.basicConfig(filename=LOG_FILE_PATH, level=logging.INFO,
                    format='%(asctime)s - %(levelname)s - %(message)s',
                    encoding='utf-8')

# 定义知识库路径
KNOWLEDGE_BASE_PATHS = [
    r"C:\Steam\steamapps\common\RimWorld\Data"
]

# 4. --- 核心功能函数 ---
def find_files_with_keyword(roots, keyword, extensions=['.xml', '.cs', '.txt']):
    """在指定目录中查找包含关键字的文件名和内容。"""
    found_files = []
    keyword_lower = keyword.lower()
    for root_path in roots:
        if not os.path.isdir(root_path):
            logging.warning(f"知识库路径不存在或不是一个目录: {root_path}")
            continue
        for dirpath, _, filenames in os.walk(root_path):
            for filename in filenames:
                if any(filename.lower().endswith(ext) for ext in extensions):
                    file_path = os.path.join(dirpath, filename)
                    try:
                        with open(file_path, 'r', encoding='utf-8') as f:
                            content = f.read()
                            # 使用不区分大小写的子字符串搜索
                            if keyword_lower in content.lower():
                                found_files.append(file_path)
                    except Exception as e:
                        logging.error(f"读取文件时出错 {file_path}: {e}")
    return found_files

def find_keyword_in_question(question: str) -> str:
    """从问题中提取最有可能的单个关键词 (通常是类型名或defName)。"""
    # 排除常见但非特定的术语
    excluded_keywords = {"XML", "C#", "DEF", "CS"}
    
    # 使用更精确的规则来识别关键词
    # 规则1: 包含下划线 (很可能是 defName)
    # 规则2: 混合大小写 (很可能是 C# 类型名)
    # 规则3: 全大写但不在排除列表中
    
    parts = question.replace('"', ' ').replace("'", ' ').replace('`', ' ').split()
    
    potential_keywords = []
    for part in parts:
        part = part.strip(',.?;:')
        if not part:
            continue
            
        # 检查是否在排除列表中
        if part.upper() in excluded_keywords:
            continue

        # 规则1: 包含下划线
        if '_' in part:
            potential_keywords.append((part, 3)) # 最高优先级
        # 规则2: 驼峰命名或混合大小写
        elif any(c.islower() for c in part) and any(c.isupper() for c in part):
            potential_keywords.append((part, 2)) # 次高优先级
        # 规则3: 多个大写字母（例如 CompPsychicScaling，但要排除纯大写缩写词）
        elif sum(1 for c in part if c.isupper()) > 1 and not part.isupper():
             potential_keywords.append((part, 2))
        # 备用规则：如果之前的规则都没匹配上，就找一个看起来像专有名词的
        elif part[0].isupper() and len(part) > 4: # 长度大于4以避免像 'A' 'I' 这样的词
            potential_keywords.append((part, 1)) # 较低优先级

    # 如果找到了关键词，按优先级排序并返回最高优先级的那个
    if potential_keywords:
        potential_keywords.sort(key=lambda x: x[1], reverse=True)
        logging.info(f"找到的潜在关键词: {potential_keywords}")
        return potential_keywords[0][0]

    # 如果没有找到，返回空字符串
    logging.warning(f"在 '{question}' 中未找到合适的关键词。")
    return ""

# 5. --- 创建和配置 MCP 服务器 ---
# 使用 FastMCP 创建服务器实例
mcp = FastMCP(
    "rimworld-knowledge-base",
    "1.0.0-fastmcp",
)

@mcp.tool()
def get_context(question: str) -> str:
    """
    根据问题中的关键词，在RimWorld知识库中搜索相关的XML或C#文件。
    返回找到的文件路径列表。
    """
    logging.info(f"收到问题: {question}")
    keyword = find_keyword_in_question(question)
    if not keyword:
        logging.warning("无法从问题中提取关键词。")
        return "无法从问题中提取关键词，请提供更具体的信息。"

    logging.info(f"提取到关键词: {keyword}")
    
    try:
        found_files = find_files_with_keyword(KNOWLEDGE_BASE_PATHS, keyword)
        if not found_files:
            logging.info(f"未找到与 '{keyword}' 相关的文件。")
            return f"未在知识库中找到与 '{keyword}' 相关的文件定义。"
        
        logging.info(f"找到了 {len(found_files)} 个相关文件。")
        # 将文件列表格式化为字符串返回
        context = f"根据关键词 '{keyword}'，在知识库中找到了以下 {len(found_files)} 个相关文件：\n\n" + "\n".join(found_files)
        return context
    except Exception as e:
        logging.error(f"处理请求时发生意外错误: {e}", exc_info=True)
        return f"处理您的请求时发生错误: {e}"

# 6. --- 启动服务器 ---
# FastMCP 实例可以直接运行
if __name__ == "__main__":
    logging.info("RimWorld 本地知识库 (FastMCP版, v1.2 关键词修正) 正在启动...")
    # 使用 'stdio' 传输协议
    mcp.run(transport="stdio")