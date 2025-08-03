# -*- coding: utf-8 -*-
import os
import sys
import logging
import json

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

# 3. --- 缓存管理 ---
def load_cache():
   """加载缓存文件"""
   if os.path.exists(CACHE_FILE_PATH):
       try:
           with open(CACHE_FILE_PATH, 'r', encoding='utf-8') as f:
               return json.load(f)
       except (json.JSONDecodeError, IOError) as e:
           logging.error(f"读取缓存文件失败: {e}")
           return {}
   return {}

def save_cache(cache_data):
   """保存缓存到文件"""
   try:
       with open(CACHE_FILE_PATH, 'w', encoding='utf-8') as f:
           json.dump(cache_data, f, ensure_ascii=False, indent=4)
   except IOError as e:
       logging.error(f"写入缓存文件失败: {e}")

# 加载初始缓存
knowledge_cache = load_cache()

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

def find_most_similar_file(question_embedding, file_embeddings):
   """在文件向量中找到与问题向量最相似的一个"""
   if not question_embedding or not file_embeddings:
       return None
   
   # 将文件嵌入列表转换为NumPy数组
   file_vectors = np.array([emb['embedding'] for emb in file_embeddings])
   question_vector = np.array(question_embedding).reshape(1, -1)
   
   # 计算余弦相似度
   similarities = cosine_similarity(question_vector, file_vectors)[0]
   
   # 找到最相似的文件的索引
   most_similar_index = np.argmax(similarities)
   
   # 返回最相似的文件路径
   return file_embeddings[most_similar_index]['path']

# 5. --- 核心功能函数 ---
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
   根据问题中的关键词和向量相似度，在RimWorld知识库中搜索最相关的XML或C#文件。
   返回最匹配的文件路径。
   """
   logging.info(f"收到问题: {question}")
   keyword = find_keyword_in_question(question)
   if not keyword:
       logging.warning("无法从问题中提取关键词。")
       return "无法从问题中提取关键词，请提供更具体的信息。"

   logging.info(f"提取到关键词: {keyword}")

   # 1. 检查缓存
   if keyword in knowledge_cache:
       cached_path = knowledge_cache[keyword]
       logging.info(f"缓存命中: 关键词 '{keyword}' -> {cached_path}")
       return f"根据知识库缓存，与 '{keyword}' 最相关的定义文件是:\n{cached_path}"

   logging.info(f"缓存未命中，开始实时搜索: {keyword}")

   # 2. 关键词文件搜索 (初步筛选)
   try:
       candidate_files = find_files_with_keyword(KNOWLEDGE_BASE_PATHS, keyword)
       if not candidate_files:
           logging.info(f"未找到与 '{keyword}' 相关的文件。")
           return f"未在知识库中找到与 '{keyword}' 相关的文件定义。"
       
       logging.info(f"找到 {len(candidate_files)} 个候选文件，开始向量化处理...")

       # 3. 向量化和相似度计算 (精准筛选)
       question_embedding = get_embedding(question)
       if not question_embedding:
           return "无法生成问题向量，请检查API连接或问题内容。"

       file_embeddings = []
       for file_path in candidate_files:
           try:
               with open(file_path, 'r', encoding='utf-8') as f:
                   content = f.read()
                   # v4模型支持更长的输入
                   file_embedding = get_embedding(content[:8000])
                   if file_embedding:
                       file_embeddings.append({'path': file_path, 'embedding': file_embedding})
           except Exception as e:
               logging.error(f"处理文件 {file_path} 时出错: {e}")
       
       if not file_embeddings:
           return "无法为任何候选文件生成向量。"

       # 找到最相似的文件
       best_match_path = find_most_similar_file(question_embedding, file_embeddings)
       
       if not best_match_path:
           return "计算向量相似度失败。"

       # 4. 更新缓存并返回结果
       logging.info(f"向量搜索完成。最匹配的文件是: {best_match_path}")
       knowledge_cache[keyword] = best_match_path
       save_cache(knowledge_cache)
       
       return f"根据向量相似度分析，与 '{keyword}' 最相关的定义文件是:\n{best_match_path}"

   except Exception as e:
       logging.error(f"处理请求时发生意外错误: {e}", exc_info=True)
       return f"处理您的请求时发生错误: {e}"

# 6. --- 启动服务器 ---
# FastMCP 实例可以直接运行
if __name__ == "__main__":
   logging.info("RimWorld 向量知识库 (FastMCP版, v2.1-v4-model) 正在启动...")
   # 使用 'stdio' 传输协议
   mcp.run(transport="stdio")