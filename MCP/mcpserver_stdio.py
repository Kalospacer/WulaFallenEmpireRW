# -*- coding: utf-8 -*-
import os
import sys
import logging
import json
import re
from http import HTTPStatus
import dashscope
from tenacity import retry, stop_after_attempt, wait_random_exponential
from sklearn.metrics.pairwise import cosine_similarity
import numpy as np
from dotenv import load_dotenv
import threading

# 1. --- 导入MCP SDK ---
MCP_DIR = os.path.dirname(os.path.abspath(__file__))
# SDK_PATH = os.path.join(MCP_DIR, 'python-sdk', 'src')
# if SDK_PATH not in sys.path:
#     sys.path.insert(0, SDK_PATH)
from mcp.server.fastmcp import FastMCP

# 2. --- 日志、缓存和知识库配置 ---
LOG_FILE_PATH = os.path.join(MCP_DIR, 'mcpserver_parallel.log')
RESULTS_LOG_PATH = os.path.join(MCP_DIR, 'mcp_results.log') # New log for results
logging.basicConfig(filename=LOG_FILE_PATH, level=logging.INFO,
                   format='%(asctime)s - %(levelname)s - %(message)s',
                   encoding='utf-8')

env_path = os.path.join(MCP_DIR, '.env')
load_dotenv(dotenv_path=env_path)

DASHSCOPE_API_KEY = os.getenv("DASHSCOPE_API_KEY")
DASHSCOPE_APP_ID = os.getenv("DASHSCOPE_APP_ID")
dashscope.api_key = DASHSCOPE_API_KEY

if not DASHSCOPE_API_KEY or not DASHSCOPE_APP_ID:
   error_msg = "错误：请确保 MCP/.env 文件中已正确配置 DASHSCOPE_API_KEY 和 DASHSCOPE_APP_ID。"
   logging.error(error_msg)
   sys.exit(error_msg)

KNOWLEDGE_BASE_PATHS = [r"C:\Steam\steamapps\common\RimWorld\Data"]

# 3. --- 辅助函数 ---

@retry(wait=wait_random_exponential(min=1, max=60), stop=stop_after_attempt(3))
def get_embedding(text: str):
   response = dashscope.TextEmbedding.call(model='text-embedding-v2', input=text)
   return response.output['embeddings'][0]['embedding'] if response.status_code == HTTPStatus.OK else None

def find_files_with_keyword(base_paths: list[str], keywords: list[str]) -> list[str]:
    found_files = set()
    for base_path in base_paths:
        for root, _, files in os.walk(base_path):
            for file in files:
                if any(keyword.lower() in file.lower() for keyword in keywords):
                    found_files.add(os.path.join(root, file))
    return list(found_files)

def rerank_files(question, file_paths, top_n=5):
    documents, valid_paths = [], []
    for path in file_paths:
        try:
            with open(path, 'r', encoding='utf-8') as f: documents.append(f.read(2000))
            valid_paths.append(path)
        except Exception: continue
    if not documents: return []
    response = dashscope.TextReRank.call(model='gte-rerank', query=question, documents=documents, top_n=top_n)
    return [valid_paths[r['index']] for r in response.output['results']] if response.status_code == HTTPStatus.OK else valid_paths[:top_n]

def extract_full_code_block(file_path, keyword):
    try:
        with open(file_path, 'r', encoding='utf-8') as f: lines = f.read().split('\n')
        found_line_index = -1
        for i, line in enumerate(lines):
            if re.search(r'\b' + re.escape(keyword) + r'\b', line, re.IGNORECASE):
                found_line_index = i
                break
        if found_line_index == -1: return ""
        if file_path.endswith(('.cs', '.txt')):
            start_index, brace_count = -1, 0
            for i in range(found_line_index, -1, -1):
                if 'class ' in lines[i] or 'struct ' in lines[i] or 'enum ' in lines[i]: start_index = i; break
            if start_index == -1: return ""
            end_index = -1
            for i in range(start_index, len(lines)):
                brace_count += lines[i].count('{') - lines[i].count('}')
                if brace_count == 0 and '{' in "".join(lines[start_index:i+1]): end_index = i; break
            return "\n".join(lines[start_index:end_index+1]) if end_index != -1 else ""
        elif file_path.endswith('.xml'): return "\n".join(lines)
    except Exception as e: logging.error(f"提取代码时出错 {file_path}: {e}"); return ""

def find_keywords_in_question(question: str) -> list[str]:
    return list(set(re.findall(r'\b[A-Z][a-zA-Z0-9_]*\b', question))) or [question.split(" ")[-1]]

# 4. --- 并行任务定义 ---

def get_cloud_response(question, result_container):
    logging.info("开始请求云端智能体...")
    try:
        response = dashscope.Application.call(app_id=DASHSCOPE_APP_ID, api_key=DASHSCOPE_API_KEY, prompt=question)
        if response.status_code == HTTPStatus.OK:
            result_container['cloud'] = response.output.text
            logging.info("成功获取云端智能体回复。")
        else:
            result_container['cloud'] = f"云端智能体请求失败: {response.message}"
            logging.error(f"云端智能体请求失败: {response.message}")
    except Exception as e:
        result_container['cloud'] = f"云端智能体请求异常: {e}"
        logging.error(f"云端智能体请求异常: {e}", exc_info=True)

def get_local_rag_response(question, result_container):
    logging.info("开始本地RAG流程...")
    try:
        keywords = find_keywords_in_question(question)
        found_files = find_files_with_keyword(KNOWLEDGE_BASE_PATHS, keywords)
        if not found_files:
            result_container['local'] = "本地RAG：未找到相关文件。"
            return
        
        reranked_files = rerank_files(question, found_files)
        
        context_blocks = []
        for file_path in reranked_files:
            for keyword in keywords:
                if keyword.lower() in file_path.lower():
                    code = extract_full_code_block(file_path, keyword)
                    if code:
                        header = f"\n--- 本地文件: {os.path.basename(file_path)} ---\n"
                        context_blocks.append(header + code)
                        break
        result_container['local'] = "\n".join(context_blocks) if context_blocks else "本地RAG：找到文件但未能提取代码。"
        logging.info("本地RAG流程完成。")
    except Exception as e:
        result_container['local'] = f"本地RAG流程异常: {e}"
        logging.error(f"本地RAG流程异常: {e}", exc_info=True)

# 5. --- MCP服务器 ---
mcp = FastMCP(name="rimworld-knowledge-base")

@mcp.tool()
def get_context(question: str) -> str:
    """并行获取云端分析和本地代码，并组合输出。"""
    results = {}
    
    # 创建并启动线程
    cloud_thread = threading.Thread(target=get_cloud_response, args=(question, results))
    local_thread = threading.Thread(target=get_local_rag_response, args=(question, results))
    
    cloud_thread.start()
    local_thread.start()
    
    # 等待两个线程完成
    cloud_thread.join()
    local_thread.join()
    
    # 组合结果
    cloud_result = results.get('cloud', "未能获取云端回复。")
    local_result = results.get('local', "未能获取本地代码。")
    
    final_response = (
        f"--- 云端智能体分析 ---\n\n{cloud_result}\n\n"
        f"====================\n\n"
        f"--- 本地完整代码参考 ---\n{local_result}"
    )
    
    # Save the result to a separate log file
    try:
        with open(RESULTS_LOG_PATH, 'a', encoding='utf-8') as f:
            import datetime
            f.write(f"--- Query at {datetime.datetime.now()} ---\n")
            f.write(f"Question: {question}\n")
            f.write(f"--- Response ---\n{final_response}\n\n")
    except Exception as e:
        logging.error(f"无法将结果写入到 mcp_results.log: {e}")

    return final_response

# 6. --- 启动服务器 ---
if __name__ == "__main__":
    logging.info("启动并行模式MCP服务器...")
    mcp.run()
    logging.info("并行模式MCP服务器已停止。")