# -*- coding: utf-8 -*-
import os
import sys
import logging
import re
from http import HTTPStatus
import dashscope

# 1. --- 导入MCP SDK ---
MCP_DIR = os.path.dirname(os.path.abspath(__file__))
SDK_PATH = os.path.join(MCP_DIR, 'python-sdk', 'src')
if SDK_PATH not in sys.path:
    sys.path.insert(0, SDK_PATH)

from mcp.server.fastmcp import FastMCP
from dotenv import load_dotenv

# 2. --- 日志和环境配置 ---
LOG_FILE_PATH = os.path.join(MCP_DIR, 'mcpserver_hybrid.log')
logging.basicConfig(filename=LOG_FILE_PATH, level=logging.INFO,
                   format='%(asctime)s - %(levelname)s - %(message)s',
                   encoding='utf-8')

# 加载 .env 文件 (API_KEY 和 APP_ID)
env_path = os.path.join(MCP_DIR, '.env')
load_dotenv(dotenv_path=env_path)

DASHSCOPE_API_KEY = os.getenv("DASHSCOPE_API_KEY")
DASHSCOPE_APP_ID = os.getenv("DASHSCOPE_APP_ID")

if not DASHSCOPE_API_KEY or not DASHSCOPE_APP_ID:
    error_msg = "错误：请确保 MCP/.env 文件中已正确配置 DASHSCOPE_API_KEY 和 DASHSCOPE_APP_ID。"
    logging.error(error_msg)
    sys.exit(error_msg)

# 定义本地知识库路径
KNOWLEDGE_BASE_PATHS = [
   r"C:\Steam\steamapps\common\RimWorld\Data"
]

# 3. --- 本地代码搜索与提取函数 (从旧版移植) ---

def find_files_with_keyword(base_paths: list[str], keywords: list[str]) -> list[str]:
    """在基础路径中递归搜索包含任意一个关键词的文件。"""
    found_files = set()
    # 完全匹配，区分大小写
    for base_path in base_paths:
        for root, _, files in os.walk(base_path):
            for file in files:
                if any(keyword in file for keyword in keywords):
                    found_files.add(os.path.join(root, file))
    logging.info(f"通过文件名关键词找到 {len(found_files)} 个文件: {found_files}")
    return list(found_files)

def extract_csharp_class(lines, start_index):
    """从C#代码行中提取完整的类定义。"""
    class_start_index = -1
    brace_level_at_class_start = -1
    # 向上找到 class, struct, or enum 声明
    for i in range(start_index, -1, -1):
        line = lines[i]
        if 'class ' in line or 'struct ' in line or 'enum ' in line:
            class_start_index = i
            # 计算声明行的初始大括号层级
            brace_level_at_class_start = lines[i].count('{')
            # 如果声明行没有'{', 则从下一行开始计算
            if '{' not in lines[i]:
                 brace_level_at_class_start = 0
                 # 寻找第一个'{'
                 for j in range(i + 1, len(lines)):
                     if '{' in lines[j]:
                         class_start_index = j
                         brace_level_at_class_start = lines[j].count('{')
                         break
            break
    
    if class_start_index == -1: return ""

    brace_count = 0
    # 从class声明之后的第一行或包含`{`的行开始计数
    start_line_for_brace_count = class_start_index
    if '{' in lines[class_start_index]:
        brace_count = lines[class_start_index].count('{') - lines[class_start_index].count('}')
        start_line_for_brace_count += 1

    class_end_index = -1
    for i in range(start_line_for_brace_count, len(lines)):
        line = lines[i]
        brace_count += line.count('{')
        brace_count -= line.count('}')
        if brace_count <= 0:
            class_end_index = i
            break
            
    if class_end_index != -1:
        # 实际截取时，从包含 class 声明的那一行开始
        original_start_index = -1
        for i in range(start_index, -1, -1):
             if 'class ' in lines[i] or 'struct ' in lines[i] or 'enum ' in lines[i]:
                 original_start_index = i
                 break
        if original_start_index != -1:
            return "\n".join(lines[original_start_index:class_end_index+1])
    return ""

def extract_xml_def(lines, start_index):
    """从XML行中提取完整的Def块。"""
    def_start_index = -1
    def_tag = ""
    # 向上找到Def的起始标签
    for i in range(start_index, -1, -1):
        line = lines[i].strip()
        match = re.search(r'<([A-Za-z_][A-Za-z0-9_]*Def)\s*.*>', line)
        if match:
             def_start_index = i
             def_tag = match.group(1)
             break
    
    if def_start_index == -1: return ""

    # 向下找到匹配的 </DefName>
    def_end_index = -1
    for i in range(def_start_index + 1, len(lines)):
        if f'</{def_tag}>' in lines[i]:
            def_end_index = i
            break
            
    if def_end_index != -1:
        return "\n".join(lines[def_start_index:def_end_index+1])
    return ""

def extract_relevant_code(file_path, keyword):
    """从文件中智能提取包含关键词的完整代码块 (C#类 或 XML Def)。"""
    try:
        with open(file_path, 'r', encoding='utf-8') as f:
            content = f.read()
        
        lines = content.split('\n')
        
        found_line_index = -1
        for i, line in enumerate(lines):
            # 使用更精确的匹配，例如匹配 "class Keyword" 或 "<defName>Keyword</defName>"
            if re.search(r'\b' + re.escape(keyword) + r'\b', line):
                found_line_index = i
                break
        
        if found_line_index == -1:
            return ""

        if file_path.endswith(('.cs', '.txt')):
            return extract_csharp_class(lines, found_line_index)
        elif file_path.endswith('.xml'):
            return extract_xml_def(lines, found_line_index)
        else:
            return ""
            
    except Exception as e:
        logging.error(f"提取代码时出错 {file_path}: {e}")
        return f"# Error reading file: {e}"

def extract_keywords_from_response(text: str) -> list[str]:
    """从LLM的回复中提取所有看起来像C#类或XML Def的关键词。"""
    # 匹配驼峰命名且以大写字母开头的单词，可能是C#类
    csharp_pattern = r'\b([A-Z][a-zA-Z0-9_]*)\b'
    # 匹配XML DefName的通用模式
    xml_pattern = r'\b([a-zA-Z0-9_]+?Def)\b'
    
    keywords = set()
    
    # 先找精确的XML Def
    for match in re.finditer(xml_pattern, text):
        keywords.add(match.group(1))

    # 再找可能是C#的类
    for match in re.finditer(csharp_pattern, text):
        word = match.group(1)
        # 过滤掉一些常见非类名词和全大写的缩写
        if word.upper() != word and len(word) > 2 and not word.endswith('Def'):
             # 检查是否包含小写字母，以排除全大写的缩写词
             if any(c.islower() for c in word):
                keywords.add(word)

    # 从 `// 来自文档X (ClassName 类)` 中提取
    doc_pattern = r'\(([\w_]+)\s+类\)'
    for match in re.finditer(doc_pattern, text):
        keywords.add(match.group(1))

    logging.info(f"从LLM回复中提取的关键词: {list(keywords)}")
    return list(keywords)


# 4. --- 创建MCP服务器实例 ---
mcp = FastMCP(
    name="rimworld-knowledge-base"
)

# 5. --- 定义核心工具 ---
@mcp.tool()
def get_context(question: str) -> str:
    """
    接收一个问题，调用云端智能体获取分析，然后用本地文件增强代码的完整性。
    """
    # --- 第一阶段：调用云端智能体 ---
    logging.info(f"收到问题: {question}")
    enhanced_prompt = (
        f"{question}\n\n"
        f"--- \n"
        f"请注意：如果回答中包含代码，必须提供完整的类或Def定义，不要省略任何部分。"
    )

    try:
        response = dashscope.Application.call(
            app_id=DASHSCOPE_APP_ID,
            api_key=DASHSCOPE_API_KEY,
            prompt=enhanced_prompt
        )

        if response.status_code != HTTPStatus.OK:
            error_info = (f'请求失败: request_id={response.request_id}, '
                          f'code={response.status_code}, message={response.message}')
            logging.error(error_info)
            return f"Error: 调用AI智能体失败。{error_info}"
        
        llm_response_text = response.output.text
        logging.info(f"收到智能体回复: {llm_response_text[:300]}...")

    except Exception as e:
        logging.error(f"调用智能体时发生未知异常: {e}", exc_info=True)
        return f"Error: 调用AI智能体时发生未知异常 - {e}"

    # --- 第二阶段：本地增强 ---
    logging.info("开始本地增强流程...")
    keywords = extract_keywords_from_response(llm_response_text)
    if not keywords:
        logging.info("未从回复中提取到关键词，直接返回云端结果。")
        return llm_response_text

    found_code_blocks = []
    processed_files = set()

    # 优先根据文件名搜索
    found_files = find_files_with_keyword(KNOWLEDGE_BASE_PATHS, keywords)
    for file_path in found_files:
        if file_path in processed_files:
            continue
        # 从文件名中找出是哪个关键词匹配的
        matching_keyword = next((k for k in keywords if k in os.path.basename(file_path)), None)
        if matching_keyword:
            logging.info(f"在文件 {file_path} 中为关键词 '{matching_keyword}' 提取代码...")
            code = extract_relevant_code(file_path, matching_keyword)
            if code:
                header = f"\n\n--- 完整代码定义: {matching_keyword} (来自 {os.path.basename(file_path)}) ---\n"
                found_code_blocks.append(header + code)
                processed_files.add(file_path)

    # --- 组合最终结果 ---
    final_response = llm_response_text
    if found_code_blocks:
        final_response += "\n\n" + "="*40
        final_response += "\n本地知识库补充的完整代码定义:\n" + "="*40
        final_response += "".join(found_code_blocks)

    return final_response

# 6. --- 启动服务器 ---
if __name__ == "__main__":
    logging.info("启动混合模式MCP服务器...")
    logging.info(f"将使用 App ID: {DASHSCOPE_APP_ID}")
    logging.info(f"Python Executable: {sys.executable}")
    mcp.run()
    logging.info("混合模式MCP服务器已停止。")