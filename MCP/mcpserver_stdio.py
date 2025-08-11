# -*- coding: utf-8 -*-
import os
import sys
import logging
import json
import re

# 1. --- 导入库 ---
# mcp 库已通过 'pip install -e' 安装，无需修改 sys.path
from mcp.server.fastmcp import FastMCP
# 新增：阿里云模型服务和向量计算库
import dashscope
from dashscope.api_entities.dashscope_response import Role
from tenacity import retry, stop_after_attempt, wait_random_exponential
from sklearn.metrics.pairwise import cosine_similarity
import numpy as np
from dotenv import load_dotenv

# 2. --- 日志、缓存和知识库配置 ---
MCP_DIR = os.path.dirname(os.path.abspath(__file__))
LOG_FILE_PATH = os.path.join(MCP_DIR, 'mcpserver.log')
CACHE_DIR = os.path.join(MCP_DIR, 'vector_cache')
CACHE_FILE_PATH = os.path.join(CACHE_DIR, 'knowledge_cache.json')
os.makedirs(CACHE_DIR, exist_ok=True)

logging.basicConfig(filename=LOG_FILE_PATH, level=logging.INFO,
                   format='%(asctime)s - %(levelname)s - %(message)s',
                   encoding='utf-8')

# 新增: 加载 .env 文件并设置 API Key
# 指定 .env 文件的确切路径，以确保脚本在任何工作目录下都能正确加载
env_path = os.path.join(MCP_DIR, '.env')
load_dotenv(dotenv_path=env_path)

dashscope.api_key = os.getenv("DASHSCOPE_API_KEY")

if not dashscope.api_key:
   logging.error("错误：未在 .env 文件中找到或加载 DASHSCOPE_API_KEY。")
   # 如果没有Key，服务器无法工作，可以选择退出或继续运行但功能受限
   # sys.exit("错误：API Key 未配置。")
else:
   logging.info("成功加载 DASHSCOPE_API_KEY。")

# 定义知识库路径
KNOWLEDGE_BASE_PATHS = [
   r"C:\Steam\steamapps\common\RimWorld\Data"
]

# 3. --- 缓存管理 (分文件存储) ---
def load_cache_for_keyword(keyword: str):
    """为指定关键词加载缓存文件。"""
    # 清理关键词，使其适合作为文件名
    safe_filename = "".join(c for c in keyword if c.isalnum() or c in ('_', '-')).rstrip()
    cache_file = os.path.join(CACHE_DIR, f"{safe_filename}.txt")
    
    if os.path.exists(cache_file):
        try:
            with open(cache_file, 'r', encoding='utf-8') as f:
                return f.read()
        except IOError as e:
            logging.error(f"读取缓存文件 {cache_file} 失败: {e}")
            return None
    return None

def save_cache_for_keyword(keyword: str, data: str):
    """为指定关键词保存缓存到单独的文件。"""
    safe_filename = "".join(c for c in keyword if c.isalnum() or c in ('_', '-')).rstrip()
    cache_file = os.path.join(CACHE_DIR, f"{safe_filename}.txt")
    
    try:
        with open(cache_file, 'w', encoding='utf-8') as f:
            f.write(data)
    except IOError as e:
        logging.error(f"写入缓存文件 {cache_file} 失败: {e}")

# 4. --- 向量化与相似度计算 ---
@retry(wait=wait_random_exponential(min=1, max=60), stop=stop_after_attempt(6))
def get_embedding(text: str):
   """获取文本的向量嵌入"""
   try:
       # 根据用户文档，选用v4模型，更适合代码和文本
       response = dashscope.TextEmbedding.call(
           model='text-embedding-v4',
           input=text
       )
       if response.status_code == 200:
           return response.output['embeddings'][0]['embedding']
       else:
           logging.error(f"获取向量失败: {response.message}")
           return None
   except Exception as e:
       logging.error(f"调用向量API时出错: {e}", exc_info=True)
       raise

def find_most_similar_files(question_embedding, file_embeddings, top_n=3, min_similarity=0.5):
    """在文件向量中找到与问题向量最相似的 top_n 个文件。"""
    if not question_embedding or not file_embeddings:
        return []

    file_vectors = np.array([emb['embedding'] for emb in file_embeddings])
    question_vector = np.array(question_embedding).reshape(1, -1)

    similarities = cosine_similarity(question_vector, file_vectors)[0]

    # 获取排序后的索引
    sorted_indices = np.argsort(similarities)[::-1]

    # 筛选出最相关的结果
    results = []
    for i in sorted_indices:
        similarity_score = similarities[i]
        if similarity_score >= min_similarity and len(results) < top_n:
            results.append({
                'path': file_embeddings[i]['path'],
                'similarity': similarity_score
            })
        else:
            break
            
    return results

def extract_relevant_code(file_path, keyword):
    """从文件中智能提取包含关键词的完整代码块 (C#类 或 XML Def)。"""
    try:
        with open(file_path, 'r', encoding='utf-8') as f:
            content = f.read()
        
        lines = content.split('\n')
        keyword_lower = keyword.lower()
        
        found_line_index = -1
        for i, line in enumerate(lines):
            if keyword_lower in line.lower():
                found_line_index = i
                break
        
        if found_line_index == -1:
            return ""

        # 根据文件类型选择提取策略
        if file_path.endswith(('.cs', '.txt')):
            # C# 提取策略：寻找完整的类
            return extract_csharp_class(lines, found_line_index)
        elif file_path.endswith('.xml'):
            # XML 提取策略：寻找完整的 Def
            return extract_xml_def(lines, found_line_index)
        else:
            return "" # 不支持的文件类型
            
    except Exception as e:
        logging.error(f"提取代码时出错 {file_path}: {e}")
        return f"# Error reading file: {e}"

def extract_csharp_class(lines, start_index):
    """从C#代码行中提取完整的类定义。"""
    # 向上找到 class 声明
    class_start_index = -1
    brace_level_at_class_start = -1
    for i in range(start_index, -1, -1):
        line = lines[i]
        if 'class ' in line:
            class_start_index = i
            brace_level_at_class_start = line.count('{') - line.count('}')
            break
    
    if class_start_index == -1: return "" # 没找到类

    # 从 class 声明开始，向下找到匹配的 '}'
    brace_count = brace_level_at_class_start
    class_end_index = -1
    for i in range(class_start_index + 1, len(lines)):
        line = lines[i]
        brace_count += line.count('{')
        brace_count -= line.count('}')
        if brace_count <= 0: # 找到匹配的闭合括号
            class_end_index = i
            break
            
    if class_end_index != -1:
        return "\n".join(lines[class_start_index:class_end_index+1])
    return "" # 未找到完整的类块

def extract_xml_def(lines, start_index):
    """从XML行中提取完整的Def块。"""
    import re
    # 向上找到 <DefName> 或 <defName>
    def_start_index = -1
    def_tag = ""
    for i in range(start_index, -1, -1):
        line = lines[i].strip()
        match = re.match(r'<(\w+)\s+.*>', line) or re.match(r'<(\w+)>', line)
        if match and ('Def' in match.group(1) or 'def' in match.group(1)):
             # 这是一个简化的判断，实际中可能需要更复杂的逻辑
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

# 5. --- 核心功能函数 ---
def find_files_with_keyword(roots, keywords: list[str], extensions=['.xml', '.cs', '.txt']):
    """在指定目录中查找包含任何一个关键字的文件。"""
    found_files = set()
    keywords_lower = [k.lower() for k in keywords]
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
                            content_lower = f.read().lower()
                            # 如果任何一个关键词在内容中，就添加文件
                            if any(kw in content_lower for kw in keywords_lower):
                                found_files.add(file_path)
                    except Exception as e:
                        logging.error(f"读取文件时出错 {file_path}: {e}")
    return list(found_files)

def find_keywords_in_question(question: str) -> list[str]:
    """从问题中提取所有可能的关键词 (类型名, defName等)。"""
    # 正则表达式优先，用于精确匹配定义
    # 匹配 C# class, struct, enum, interface 定义, 例如 "public class MyClass : Base"
    csharp_def_pattern = re.compile(r'\b(?:public|private|internal|protected|sealed|abstract|static|new)\s+(?:class|struct|enum|interface)\s+([A-Za-z_][A-Za-z0-9_]*)')
    # 匹配 XML Def, 例如 "<ThingDef Name="MyDef">" or "<MyCustomDef>"
    xml_def_pattern = re.compile(r'<([A-Za-z_][A-Za-z0-9_]*Def)\b')
    
    # 启发式规则，用于匹配独立的关键词
    # 规则1: 包含下划线 (很可能是 defName)
    # 规则2: 混合大小写 (很可能是 C# 类型名)
    # 规则3: 多个大写字母（例如 CompPsychicScaling，但要排除纯大写缩写词）
    
    # 排除常见但非特定的术语
    excluded_keywords = {"XML", "C#", "DEF", "CS", "CLASS", "PUBLIC"}

    found_keywords = set()

    # 1. 正则匹配
    csharp_matches = csharp_def_pattern.findall(question)
    xml_matches = xml_def_pattern.findall(question)
    
    for match in csharp_matches:
        found_keywords.add(match)
    for match in xml_matches:
        found_keywords.add(match)

    # 2. 启发式单词匹配
    parts = re.split(r'[\s,.:;\'"`()<>]+', question)
    
    for part in parts:
        if not part or part.upper() in excluded_keywords:
            continue

        # 规则1: 包含下划线
        if '_' in part:
            found_keywords.add(part)
        # 规则2: 驼峰命名或混合大小写
        elif any(c.islower() for c in part) and any(c.isupper() for c in part) and len(part) > 3:
            found_keywords.add(part)
        # 规则3: 多个大写字母
        elif sum(1 for c in part if c.isupper()) > 1 and not part.isupper():
            found_keywords.add(part)
        # 备用规则: 大写字母开头且较长
        elif part[0].isupper() and len(part) > 4:
            found_keywords.add(part)

    if not found_keywords:
        logging.warning(f"在 '{question}' 中未找到合适的关键词。")
        return []
        
    logging.info(f"找到的潜在关键词: {list(found_keywords)}")
    return list(found_keywords)


# 5. --- 创建和配置 MCP 服务器 ---
# 使用 FastMCP 创建服务器实例
mcp = FastMCP(
    "rimworld-knowledge-base",
    "1.0.0-fastmcp",
)

@mcp.tool()
def get_context(question: str) -> str:
   """
   根据问题中的关键词和向量相似度，在RimWorld知识库中搜索最相关的多个代码片段，
   并将其整合后返回。
   """
   logging.info(f"收到问题: {question}")
   keywords = find_keywords_in_question(question)
   if not keywords:
       logging.warning("无法从问题中提取关键词。")
       return "无法从问题中提取关键词，请提供更具体的信息。"

   logging.info(f"提取到关键词: {keywords}")
   
   # 基于所有关键词创建缓存键
   cache_key = "-".join(sorted(keywords))

   # 1. 检查缓存
   cached_result = load_cache_for_keyword(cache_key)
   if cached_result:
       logging.info(f"缓存命中: 关键词 '{cache_key}'")
       return cached_result

   logging.info(f"缓存未命中，开始实时搜索: {cache_key}")

   # 2. 关键词文件搜索 (分层智能筛选)
   try:
       # 优先使用最长的（通常最具体）的关键词进行搜索
       specific_keywords = sorted(keywords, key=len, reverse=True)
       candidate_files = find_files_with_keyword(KNOWLEDGE_BASE_PATHS, [specific_keywords[0]])

       # 如果最具体的关键词找不到文件，再尝试所有关键词
       if not candidate_files and len(keywords) > 1:
           logging.info(f"使用最具体的关键词 '{specific_keywords[0]}' 未找到文件，尝试所有关键词...")
           candidate_files = find_files_with_keyword(KNOWLEDGE_BASE_PATHS, keywords)

       if not candidate_files:
           logging.info(f"未找到与 '{keywords}' 相关的文件。")
           return f"未在知识库中找到与 '{keywords}' 相关的文件定义。"
       
       logging.info(f"找到 {len(candidate_files)} 个候选文件，开始向量化处理...")

       # 新增：文件名精确匹配优先
       priority_results = []
       remaining_files = []
       for file_path in candidate_files:
           filename_no_ext = os.path.splitext(os.path.basename(file_path))[0]
           is_priority = False
           for keyword in keywords:
               if filename_no_ext.lower() == keyword.lower():
                   logging.info(f"文件名精确匹配: {file_path}")
                   code_block = extract_relevant_code(file_path, keyword)
                   if code_block:
                       lang = "csharp" if file_path.endswith(('.cs', '.txt')) else "xml"
                       priority_results.append(
                           f"---\n"
                           f"**文件路径 (精确匹配):** `{file_path}`\n\n"
                           f"```{lang}\n"
                           f"{code_block}\n"
                           f"```"
                       )
                   is_priority = True
                   break # 已处理该文件，跳出内层循环
           if not is_priority:
               remaining_files.append(file_path)
       
       candidate_files = remaining_files # 更新候选文件列表，排除已优先处理的文件

       # 3. 向量化和相似度计算 (精准筛选)
       # 增加超时保护：限制向量化的文件数量
       MAX_FILES_TO_VECTORIZE = 25
       if len(candidate_files) > MAX_FILES_TO_VECTORIZE:
           logging.warning(f"候选文件过多 ({len(candidate_files)})，仅处理前 {MAX_FILES_TO_VECTORIZE} 个。")
           candidate_files = candidate_files[:MAX_FILES_TO_VECTORIZE]

       question_embedding = get_embedding(question)
       if not question_embedding:
           return "无法生成问题向量，请检查API连接或问题内容。"

       file_embeddings = []
       for file_path in candidate_files:
           try:
               with open(file_path, 'r', encoding='utf-8') as f:
                   content = f.read()
                   file_embedding = get_embedding(content[:8000]) # 限制内容长度以提高效率
                   if file_embedding:
                       file_embeddings.append({'path': file_path, 'embedding': file_embedding})
           except Exception as e:
               logging.error(f"处理文件 {file_path} 时出错: {e}")
       
       if not file_embeddings:
           return "无法为任何候选文件生成向量。"

       # 找到最相似的多个文件
       best_matches = find_most_similar_files(question_embedding, file_embeddings, top_n=5) # 增加返回数量
       
       if not best_matches:
           return "计算向量相似度失败或没有找到足够相似的文件。"

       # 4. 提取代码并格式化输出
       output_parts = [f"根据向量相似度分析，与 '{', '.join(keywords)}' 最相关的代码定义如下：\n"]
       output_parts.extend(priority_results) # 将优先结果放在最前面
       
       extracted_blocks = set() # 用于防止重复提取相同的代码块

       for match in best_matches:
           file_path = match['path']
           similarity = match['similarity']
           
           # 对每个关键词都尝试提取代码
           for keyword in keywords:
               code_block = extract_relevant_code(file_path, keyword)
               
               if code_block and code_block not in extracted_blocks:
                   extracted_blocks.add(code_block)
                   lang = "csharp" if file_path.endswith(('.cs', '.txt')) else "xml"
                   output_parts.append(
                       f"---\n"
                       f"**文件路径:** `{file_path}`\n"
                       f"**相似度:** {similarity:.4f}\n\n"
                       f"```{lang}\n"
                       f"{code_block}\n"
                       f"```"
                   )
       
       if len(output_parts) <= 1:
           return f"虽然找到了相似的文件，但无法在其中提取到关于 '{', '.join(keywords)}' 的完整代码块。"

       final_output = "\n".join(output_parts)
       
       # 5. 更新缓存并返回结果
       logging.info(f"向量搜索完成。找到了 {len(best_matches)} 个匹配项并成功提取了代码。")
       save_cache_for_keyword(cache_key, final_output)
       
       return final_output

   except Exception as e:
       logging.error(f"处理请求时发生意外错误: {e}", exc_info=True)
       return f"处理您的请求时发生错误: {e}"

# 6. --- 启动服务器 ---
# FastMCP 实例可以直接运行
if __name__ == "__main__":
   logging.info("RimWorld 向量知识库 (FastMCP版, v2.1-v4-model) 正在启动...")
   # 使用 'stdio' 传输协议
   mcp.run(transport="stdio")