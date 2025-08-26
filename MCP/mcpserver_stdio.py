# -*- coding: utf-8 -*-
import os
import sys
import logging
import json
import re

# 1. --- 导入库 ---
# 动态将 mcp sdk 添加到 python 路径
MCP_DIR = os.path.dirname(os.path.abspath(__file__))
SDK_PATH = os.path.join(MCP_DIR, 'python-sdk', 'src')
if SDK_PATH not in sys.path:
    sys.path.insert(0, SDK_PATH)

from mcp.server.fastmcp import FastMCP
# 新增：阿里云模型服务和向量计算库
import dashscope
from dashscope.api_entities.dashscope_response import Role
from tenacity import retry, stop_after_attempt, wait_random_exponential
from sklearn.metrics.pairwise import cosine_similarity
import numpy as np
from dotenv import load_dotenv
from openai import OpenAI

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

# 初始化OpenAI客户端用于Qwen模型
qwen_client = OpenAI(
    api_key=os.getenv("DASHSCOPE_API_KEY"),
    base_url="https://dashscope.aliyuncs.com/compatible-mode/v1",
    timeout=15.0  # 设置15秒超时，避免MCP初始化超时
)

# 3. --- 向量缓存管理 ---
def load_vector_cache():
    """加载向量缓存数据库"""
    if os.path.exists(CACHE_FILE_PATH):
        try:
            with open(CACHE_FILE_PATH, 'r', encoding='utf-8') as f:
                return json.load(f)
        except Exception as e:
            logging.error(f"读取向量缓存数据库失败: {e}")
            return {}
    return {}

def save_vector_cache(cache_data):
    """保存向量缓存数据库"""
    try:
        with open(CACHE_FILE_PATH, 'w', encoding='utf-8') as f:
            json.dump(cache_data, f, ensure_ascii=False, indent=2)
    except Exception as e:
        logging.error(f"保存向量缓存数据库失败: {e}")

def get_cache_key(keywords: list[str]) -> str:
    """生成缓存键"""
    return "-".join(sorted(keywords))

def load_cache_for_question(question: str, keywords: list[str]):
    """为指定问题和关键词加载缓存结果"""
    cache_data = load_vector_cache()
    cache_key = get_cache_key(keywords)
    
    # 检查是否有完全匹配的缓存
    if cache_key in cache_data:
        cached_entry = cache_data[cache_key]
        logging.info(f"缓存命中: 关键词 '{cache_key}'")
        return cached_entry.get("result", "")
    
    # 检查是否有相似问题的缓存（基于向量相似度）
    question_embedding = get_embedding(question)
    if not question_embedding:
        return None
        
    best_similarity = 0
    best_result = None
    
    for key, entry in cache_data.items():
        if "embedding" in entry:
            try:
                cached_embedding = entry["embedding"]
                similarity = cosine_similarity(
                    np.array(question_embedding).reshape(1, -1),
                    np.array(cached_embedding).reshape(1, -1)
                )[0][0]
                
                if similarity > best_similarity and similarity > 0.9:  # 相似度阈值
                    best_similarity = similarity
                    best_result = entry.get("result", "")
            except Exception as e:
                logging.error(f"计算缓存相似度时出错: {e}")
    
    if best_result:
        logging.info(f"相似问题缓存命中，相似度: {best_similarity:.3f}")
        return best_result
        
    return None

def save_cache_for_question(question: str, keywords: list[str], result: str):
    """为指定问题和关键词保存缓存结果"""
    try:
        cache_data = load_vector_cache()
        cache_key = get_cache_key(keywords)
        
        # 获取问题的向量嵌入
        question_embedding = get_embedding(question)
        if not question_embedding:
            return
            
        cache_data[cache_key] = {
            "keywords": keywords,
            "question": question,
            "embedding": question_embedding,
            "result": result,
            "timestamp": logging.Formatter('%(asctime)s').format(logging.LogRecord('', 0, '', 0, '', (), None))
        }
        
        save_vector_cache(cache_data)
    except Exception as e:
        logging.error(f"保存缓存时出错: {e}")

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

# 新增：重排序函数
def rerank_files(question, file_matches, top_n=3):  # 减少默认数量
    """使用DashScope重排序API对文件进行重新排序"""
    try:
        # 限制输入数量以减少超时风险
        if len(file_matches) > 5:  # 进一步限制最大输入数量以避免超时
            file_matches = file_matches[:5]
            
        # 准备重排序输入
        documents = []
        for match in file_matches:
            # 读取文件内容
            try:
                with open(match['path'], 'r', encoding='utf-8') as f:
                    content = f.read()[:1500]  # 进一步限制内容长度以提高效率
                documents.append(content)
            except Exception as e:
                logging.error(f"读取文件 {match['path']} 失败: {e}")
                continue
        
        if not documents:
            logging.warning("重排序时未能读取任何文件内容")
            return file_matches[:top_n]
        
        # 调用重排序API，添加超时处理
        import time
        start_time = time.time()
        
        response = dashscope.TextReRank.call(
            model='gte-rerank',
            query=question,
            documents=documents
        )
        
        elapsed_time = time.time() - start_time
        logging.info(f"重排序API调用耗时: {elapsed_time:.2f}秒")
        
        if response.status_code == 200:
            # 根据重排序结果重新排序文件
            reranked_results = []
            for i, result in enumerate(response.output['results']):
                if i < len(file_matches) and i < len(documents):  # 添加边界检查
                    reranked_results.append({
                        'path': file_matches[i]['path'],
                        'similarity': result['relevance_score']
                    })
            
            # 按重排序分数排序
            reranked_results.sort(key=lambda x: x['similarity'], reverse=True)
            return reranked_results[:top_n]
        else:
            logging.error(f"重排序失败: {response.message}")
            return file_matches[:top_n]
    except Exception as e:
        logging.error(f"重排序时出错: {e}", exc_info=True)
        return file_matches[:top_n]

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

def find_keywords_in_question(question: str) -> list[str]:
    """从问题中提取所有可能的关键词 (类型名, defName等)。"""
    # 简化关键词提取逻辑，主要依赖LLM进行分析
    # 这里仅作为备用方案，用于LLM不可用时的基本关键词提取
    
    # 正则表达式优先，用于精确匹配定义
    # 匹配 C# class, struct, enum, interface 定义, 例如 "public class MyClass : Base"
    csharp_def_pattern = re.compile(r'\b(?:public|private|internal|protected|sealed|abstract|static|new)\s+(?:class|struct|enum|interface)\s+([A-Za-z_][A-Za-z0-9_]*)')
    # 匹配 XML Def, 例如 "<ThingDef Name="MyDef">" or "<MyCustomDef>"
    xml_def_pattern = re.compile(r'<([A-Za-z_][A-Za-z0-9_]*Def)\b')
    
    found_keywords = set()

    # 1. 正则匹配
    csharp_matches = csharp_def_pattern.findall(question)
    xml_matches = xml_def_pattern.findall(question)
    
    for match in csharp_matches:
        found_keywords.add(match)
    for match in xml_matches:
        found_keywords.add(match)

    # 2. 启发式单词匹配 - 简化版
    parts = re.split(r'[\s,.:;\'"`()<>]+', question)
    
    for part in parts:
        if not part:
            continue

        # 规则1: 包含下划线 (很可能是 defName)
        if '_' in part:
            found_keywords.add(part)
        # 规则2: 驼峰命名或混合大小写 (很可能是 C# 类型名)
        elif any(c.islower() for c in part) and any(c.isupper() for c in part) and len(part) > 3:
            found_keywords.add(part)
        # 规则3: 多个大写字母（例如 CompPsychicScaling）
        elif sum(1 for c in part if c.isupper()) > 1 and not part.isupper() and len(part) > 3:
            found_keywords.add(part)

    if not found_keywords:
        logging.warning(f"在 '{question}' 中未找到合适的关键词。")
        # 如果找不到关键词，尝试使用整个问题作为关键词
        return [question]
        
    logging.info(f"找到的潜在关键词: {list(found_keywords)}")
    return list(found_keywords)

def analyze_question_with_llm(question: str) -> dict:
    """使用Qwen模型分析问题并提取关键词和意图"""
    try:
        system_prompt = """你是一个关键词提取机器人，专门用于从 RimWorld 模组开发相关问题中提取精确的搜索关键词。你的任务是识别问题中提到的核心技术术语，并将它们正确地拆分成独立的关键词。

严格按照以下格式回复，不要添加任何额外说明：
问题类型：[问题分类]
关键类/方法名：[类名或方法名]
关键概念：[关键概念]
搜索关键词：[关键词1,关键词2,关键词3]

提取规则：
1. 搜索关键词只能包含问题中明确出现的技术术语
2. 不要添加任何推测或联想的词
3. 不要添加通用词如"RimWorld"、"游戏"、"定义"、"用法"等
4. 不要添加缩写或扩展形式如"Def"、"XML"等除非问题中明确提到
5. 只提取具体的技术名词，忽略动词、形容词等
6. 当遇到用空格连接的多个技术术语时，应将它们拆分为独立的关键词
7. 关键词之间用英文逗号分隔，不要有空格

示例：
问题：ThingDef的定义和用法是什么？
问题类型：API 使用和定义说明
关键类/方法名：ThingDef
关键概念：定义, 用法
搜索关键词：ThingDef

问题：GenExplosion.DoExplosion 和 Projectile.Launch 方法如何使用？
问题类型：API 使用说明
关键类/方法名：GenExplosion.DoExplosion,Projectile.Launch
关键概念：API 使用
搜索关键词：GenExplosion.DoExplosion,Projectile.Launch

问题：RimWorld Pawn_HealthTracker PreApplyDamage
问题类型：API 使用说明
关键类/方法名：Pawn_HealthTracker,PreApplyDamage
关键概念：伤害处理
搜索关键词：Pawn_HealthTracker,PreApplyDamage

现在请分析以下问题："""

        messages = [
            {"role": "system", "content": system_prompt},
            {"role": "user", "content": question}
        ]
        
        response = qwen_client.chat.completions.create(
            model="qwen-plus",
            messages=messages,
            temperature=0.0,  # 使用最低温度确保输出稳定
            max_tokens=300,
            timeout=12.0,  # 12秒超时，避免MCP初始化超时
            stop=["\n\n"]  # 防止模型生成过多内容
        )
        
        analysis_result = response.choices[0].message.content
        logging.info(f"LLM分析结果: {analysis_result}")
        
        # 解析LLM的分析结果
        lines = analysis_result.strip().split('\n')
        result = {
            "question_type": "",
            "key_classes_methods": [],
            "key_concepts": [],
            "search_keywords": []
        }
        
        for line in lines:
            if line.startswith("问题类型："):
                result["question_type"] = line.replace("问题类型：", "").strip()
            elif line.startswith("关键类/方法名："):
                methods = line.replace("关键类/方法名：", "").strip()
                result["key_classes_methods"] = [m.strip() for m in methods.split(",") if m.strip()]
            elif line.startswith("关键概念："):
                concepts = line.replace("关键概念：", "").strip()
                result["key_concepts"] = [c.strip() for c in concepts.split(",") if c.strip()]
            elif line.startswith("搜索关键词："):
                keywords = line.replace("搜索关键词：", "").strip()
                # 直接按逗号分割，不进行额外处理
                result["search_keywords"] = [k.strip() for k in keywords.split(",") if k.strip()]
        
        # 如果LLM没有返回有效的关键词，则使用备用方案
        if not result["search_keywords"]:
            result["search_keywords"] = find_keywords_in_question(question)
        
        return result
    except Exception as e:
        logging.error(f"使用LLM分析问题时出错: {e}", exc_info=True)
        # 备用方案：使用原始关键词提取方法
        return {
            "question_type": "未知",
            "key_classes_methods": [],
            "key_concepts": [],
            "search_keywords": find_keywords_in_question(question)
        }

def find_files_with_keyword(base_paths: list[str], keywords: list[str]) -> list[str]:
    """
    在基础路径中递归搜索包含任意一个关键词的文件。
    搜索范围包括文件名。
    """
    found_files = set()
    keywords_lower = [k.lower() for k in keywords]

    for base_path in base_paths:
        for root, _, files in os.walk(base_path):
            for file in files:
                file_lower = file.lower()
                if any(keyword in file_lower for keyword in keywords_lower):
                    found_files.add(os.path.join(root, file))

    logging.info(f"通过关键词找到 {len(found_files)} 个文件。")
    return list(found_files)

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
   
   try:
       # 使用LLM分析问题，添加超时保护
       analysis = analyze_question_with_llm(question)
       keywords = analysis["search_keywords"]
       
       # 确保关键词被正确拆分
       split_keywords = []
       for keyword in keywords:
           # 如果关键词中包含空格，将其拆分为多个关键词
           if ' ' in keyword:
               split_keywords.extend(keyword.split())
           else:
               split_keywords.append(keyword)
       keywords = split_keywords
       
       if not keywords:
           logging.warning("无法从问题中提取关键词。")
           return "无法从问题中提取关键词，请提供更具体的信息。"
   except Exception as e:
       logging.error(f"LLM分析失败，使用备用方案: {e}")
       # 备用方案：使用简单的关键词提取
       keywords = find_keywords_in_question(question)
       if not keywords:
           return "无法分析问题，请检查网络连接或稍后重试。"

   logging.info(f"提取到关键词: {keywords}")
   
   # 基于所有关键词创建缓存键
   cache_key = "-".join(sorted(keywords))

   # 1. 检查缓存
   cached_result = load_cache_for_question(question, keywords)
   if cached_result:
       return cached_result

   logging.info(f"缓存未命中，开始实时搜索: {cache_key}")

   # 2. 对每个关键词分别执行搜索过程，然后合并结果
   try:
       all_results = []
       processed_files = set()  # 避免重复处理相同文件
       
       for keyword in keywords:
           logging.info(f"开始搜索关键词: {keyword}")
           
           # 为当前关键词搜索文件
           candidate_files = find_files_with_keyword(KNOWLEDGE_BASE_PATHS, [keyword])

           if not candidate_files:
               logging.info(f"未找到与 '{keyword}' 相关的文件。")
               continue
           
           logging.info(f"找到 {len(candidate_files)} 个候选文件用于关键词 '{keyword}'，开始向量化处理...")

           # 文件名精确匹配优先
           priority_results = []
           remaining_files = []
           for file_path in candidate_files:
               # 避免重复处理相同文件
               if file_path in processed_files:
                   continue
                   
               filename_no_ext = os.path.splitext(os.path.basename(file_path))[0]
               if filename_no_ext.lower() == keyword.lower():
                   logging.info(f"文件名精确匹配: {file_path}")
                   code_block = extract_relevant_code(file_path, keyword)
                   if code_block:
                       priority_results.append({
                           'path': file_path,
                           'similarity': 1.0,  # 精确匹配给予最高分
                           'code': code_block
                       })
                   processed_files.add(file_path)
               else:
                   remaining_files.append(file_path)
           
           # 更新候选文件列表，排除已优先处理的文件
           candidate_files = [f for f in remaining_files if f not in processed_files]
           
           # 限制向量化的文件数量以避免超时
           MAX_FILES_TO_VECTORIZE = 5
           if len(candidate_files) > MAX_FILES_TO_VECTORIZE:
               logging.warning(f"候选文件过多 ({len(candidate_files)})，仅处理前 {MAX_FILES_TO_VECTORIZE} 个。")
               candidate_files = candidate_files[:MAX_FILES_TO_VECTORIZE]

           # 为剩余文件生成向量
           question_embedding = get_embedding(keyword)  # 使用关键词而不是整个问题
           if not question_embedding:
               logging.warning(f"无法为关键词 '{keyword}' 生成向量。")
               # 将优先结果添加到总结果中
               all_results.extend(priority_results)
               continue

           file_embeddings = []
           for i, file_path in enumerate(candidate_files):
               try:
                   # 避免重复处理相同文件
                   if file_path in processed_files:
                       continue
                       
                   with open(file_path, 'r', encoding='utf-8') as f:
                       content = f.read()
                       # 添加处理进度日志
                       if i % 5 == 0:  # 每5个文件记录一次进度
                           logging.info(f"正在处理第 {i+1}/{len(candidate_files)} 个文件: {os.path.basename(file_path)}")
                       
                       file_embedding = get_embedding(content[:8000]) # 限制内容长度以提高效率
                       if file_embedding:
                           file_embeddings.append({'path': file_path, 'embedding': file_embedding})
               except Exception as e:
                   logging.error(f"处理文件 {file_path} 时出错: {e}")
                   continue # 继续处理下一个文件，而不是完全失败
           
           if not file_embeddings and not priority_results:
               logging.warning(f"未能为关键词 '{keyword}' 的任何候选文件生成向量。")
               continue

           # 找到最相似的多个文件
           best_matches = find_most_similar_files(question_embedding, file_embeddings, top_n=3)
           
           # 重排序处理
           if len(best_matches) > 1:
               reranked_matches = rerank_files(keyword, best_matches, top_n=2)  # 减少重排序数量
           else:
               reranked_matches = best_matches
           
           # 提取代码内容
           results_with_code = []
           for match in reranked_matches:
               # 避免重复处理相同文件
               if match['path'] in processed_files:
                   continue
                   
               code_block = extract_relevant_code(match['path'], "")
               if code_block:
                   match['code'] = code_block
                   results_with_code.append(match)
                   processed_files.add(match['path'])
           
           # 将优先结果和相似度结果合并
           results_with_code = priority_results + results_with_code
           
           # 将当前关键词的结果添加到总结果中
           all_results.extend(results_with_code)
       
       # 检查是否有任何结果
       if len(all_results) <= 0:
           return f"未在知识库中找到与 '{keywords}' 相关的文件定义。"
       
       # 整理最终输出
       final_output = ""
       for i, result in enumerate(all_results, 1):
           final_output += f"--- 结果 {i} (相似度: {result['similarity']:.3f}) ---\n"
           final_output += f"文件路径: {result['path']}\n\n"
           final_output += f"{result['code']}\n\n"
       
       # 5. 更新缓存并返回结果
       logging.info(f"向量搜索完成。找到了 {len(all_results)} 个匹配项并成功提取了代码。")
       save_cache_for_question(question, keywords, final_output)
       
       return final_output

   except Exception as e:
       logging.error(f"处理请求时发生意外错误: {e}", exc_info=True)
       return f"处理您的请求时发生错误: {e}"

# 6. --- 启动服务器 ---
# FastMCP 实例可以直接运行
if __name__ == "__main__":
   logging.info(f"Python Executable: {sys.executable}")
   logging.info("RimWorld 向量知识库 (FastMCP版, v2.1-v4-model) 正在启动...")
   
   # 快速启动：延迟初始化重量级组件
   try:
       # 验证基本配置
       if not dashscope.api_key:
           logging.warning("警告：DASHSCOPE_API_KEY 未配置，部分功能可能受限。")
       
       # 创建必要目录
       os.makedirs(CACHE_DIR, exist_ok=True)
       
       logging.info("MCP服务器快速启动完成，等待客户端连接...")
   except Exception as e:
       logging.error(f"服务器启动时出错: {e}")
   
   # 使用 'stdio' 传输协议
   mcp.run(transport="stdio")