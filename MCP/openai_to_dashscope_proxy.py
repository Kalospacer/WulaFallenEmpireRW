#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
OpenAI兼容接口到阿里云百炼平台智能体应用的转发服务
"""

import os
import json
import logging
from http.server import HTTPServer, BaseHTTPRequestHandler
from urllib.parse import urlparse
import requests
import threading
import time
import uuid
from dotenv import load_dotenv

# 尝试加载.env文件
load_dotenv()

# 配置日志
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(levelname)s - %(message)s',
    handlers=[
        logging.FileHandler("proxy.log", encoding='utf-8'),
        logging.StreamHandler()
    ]
)
logger = logging.getLogger(__name__)

# 从环境变量获取配置
DASHSCOPE_API_KEY = os.getenv("DASHSCOPE_API_KEY")
APP_ID = os.getenv("DASHSCOPE_APP_ID")

# 检查必要配置
if not DASHSCOPE_API_KEY:
    raise ValueError("请设置环境变量 DASHSCOPE_API_KEY")

if not APP_ID:
    raise ValueError("请设置环境变量 DASHSCOPE_APP_ID")

# 阿里云百炼平台智能体应用的URL
DASHSCOPE_APP_URL = f"https://dashscope.aliyuncs.com/api/v1/apps/{APP_ID}/completion"

class OpenAIProxyHandler(BaseHTTPRequestHandler):
    """处理OpenAI兼容请求并转发到阿里云百炼平台"""
    
    def log_message(self, format, *args):
        """重写日志方法，使用我们的日志配置"""
        logger.info("%s - - [%s] %s" % (self.address_string(), self.log_date_time_string(), format % args))
    
    def do_POST(self):
        """处理POST请求"""
        try:
            # 解析请求路径
            parsed_path = urlparse(self.path)
            
            # 只处理聊天完成接口
            if parsed_path.path == "/v1/chat/completions":
                self.handle_chat_completions()
            else:
                self.send_error(404, "接口未找到")
                
        except Exception as e:
            logger.error(f"处理POST请求时出错: {e}")
            try:
                self.send_error(500, "内部服务器错误")
            except:
                pass  # 客户端可能已经断开连接
    
    def do_GET(self):
        """处理GET请求"""
        try:
            # 解析请求路径
            parsed_path = urlparse(self.path)
            
            # 处理模型列表请求
            if parsed_path.path == "/v1/models":
                self.handle_list_models()
            else:
                self.send_error(404, "接口未找到")
                
        except Exception as e:
            logger.error(f"处理GET请求时出错: {e}")
            try:
                self.send_error(500, "内部服务器错误")
            except:
                pass  # 客户端可能已经断开连接
    
    def handle_list_models(self):
        """处理模型列表请求"""
        try:
            # 构造OpenAI兼容的模型列表响应
            models_response = {
                "object": "list",
                "data": [
                    {
                        "id": "dashscope-app",
                        "object": "model",
                        "created": int(time.time()),
                        "owned_by": "dashscope"
                    }
                ]
            }
            
            # 发送响应
            self.send_response(200)
            self.send_header('Content-Type', 'application/json')
            self.send_header('Access-Control-Allow-Origin', '*')
            self.end_headers()
            
            response_json = json.dumps(models_response, ensure_ascii=False)
            self.wfile.write(response_json.encode('utf-8'))
            
        except Exception as e:
            logger.error(f"处理模型列表请求时出错: {e}")
            try:
                self.send_error(500, "内部服务器错误")
            except:
                pass  # 客户端可能已经断开连接
    
    def handle_chat_completions(self):
        """处理聊天完成请求"""
        try:
            # 读取请求内容
            content_length = int(self.headers.get('Content-Length', 0))
            post_data = self.rfile.read(content_length)
            
            # 解析JSON数据
            request_data = json.loads(post_data.decode('utf-8'))
            
            # 记录接收到的请求
            logger.info(f"收到OpenAI兼容请求: {json.dumps(request_data, ensure_ascii=False)}")
            
            # 提取用户消息
            messages = request_data.get('messages', [])
            if not messages:
                self.send_error(400, "缺少消息内容")
                return
                
            # 获取最后一条用户消息作为提示词
            prompt = ""
            conversation_history = []
            
            for message in messages:
                role = message.get('role')
                content = message.get('content', '')
                conversation_history.append(f"{role}: {content}")
                
                if role == 'user':
                    prompt = content
            
            if not prompt:
                self.send_error(400, "未找到用户消息")
                return
            
            # 记录提取的提示词和对话历史
            logger.info(f"提取的用户提示词: {prompt}")
            logger.info(f"完整对话历史: {' | '.join(conversation_history)}")
            
            # 构造阿里云百炼平台请求
            dashscope_request = {
                "input": {
                    "prompt": prompt
                },
                "parameters": {},
                "debug": {}
            }
            
            # 处理流式请求
            stream = request_data.get('stream', False)
            
            # 设置请求头
            headers = {
                "Authorization": f"Bearer {DASHSCOPE_API_KEY}",
                "Content-Type": "application/json"
            }
            
            if stream:
                headers["Accept"] = "text/event-stream"
                headers["X-DashScope-SSE"] = "enable"
                dashscope_request["parameters"]["stream"] = True
                dashscope_request["parameters"]["incremental_output"] = True
            
            # 记录发送给阿里云百炼平台的请求
            logger.info(f"发送到阿里云百炼平台的请求: {json.dumps(dashscope_request, ensure_ascii=False)}")
            
            # 转发请求到阿里云百炼平台
            response = requests.post(
                DASHSCOPE_APP_URL,
                headers=headers,
                json=dashscope_request,
                stream=stream
            )
            
            # 记录阿里云百炼平台的响应状态
            logger.info(f"阿里云百炼平台响应状态: {response.status_code}")
            
            # 设置响应头
            self.send_response(response.status_code)
            
            # 复制响应头
            for key, value in response.headers.items():
                # 过滤掉一些不需要的头
                if key.lower() not in ['connection', 'transfer-encoding']:
                    self.send_header(key, value)
            
            self.end_headers()
            
            # 转发响应内容
            if stream:
                logger.info("处理流式响应")
                # 流式响应
                try:
                    for chunk in response.iter_content(chunk_size=1024):
                        if chunk:
                            self.wfile.write(chunk)
                            self.wfile.flush()
                except ConnectionAbortedError:
                    logger.warning("客户端中断了连接")
            else:
                # 普通响应
                # 读取响应内容
                response_content = response.text
                
                # 记录响应内容
                logger.info(f"阿里云百炼平台响应内容: {response_content[:200]}...")  # 只记录前200个字符
                
                # 尝试解析并转换为OpenAI格式
                try:
                    dashscope_response = json.loads(response_content)
                    openai_response = self.convert_to_openai_format(dashscope_response, request_data)
                    response_json = json.dumps(openai_response, ensure_ascii=False)
                    logger.info(f"转换后的OpenAI格式响应: {response_json[:200]}...")  # 只记录前200个字符
                    self.wfile.write(response_json.encode('utf-8'))
                except json.JSONDecodeError:
                    # 如果不是JSON格式，构造一个标准的OpenAI格式响应
                    logger.warning("响应不是JSON格式，构造标准OpenAI格式响应")
                    openai_response = {
                        "id": f"chatcmpl-{uuid.uuid4().hex}",
                        "object": "chat.completion",
                        "created": int(time.time()),
                        "model": request_data.get("model", "dashscope-app"),
                        "choices": [
                            {
                                "index": 0,
                                "message": {
                                    "role": "assistant",
                                    "content": response_content,
                                },
                                "finish_reason": "stop"
                            }
                        ],
                        "usage": {
                            "prompt_tokens": -1,
                            "completion_tokens": -1,
                            "total_tokens": -1
                        }
                    }
                    response_json = json.dumps(openai_response, ensure_ascii=False)
                    self.wfile.write(response_json.encode('utf-8'))
                    
        except Exception as e:
            logger.error(f"处理聊天完成请求时出错: {e}")
            try:
                self.send_error(500, "处理请求时出错")
            except:
                pass  # 如果无法发送错误响应，则忽略
    
    def convert_to_openai_format(self, dashscope_response, request_data):
        """将阿里云百炼平台响应转换为OpenAI格式"""
        try:
            # 提取文本内容
            text_content = dashscope_response.get("output", {}).get("text", "")
            finish_reason = dashscope_response.get("output", {}).get("finish_reason", "stop")
            
            # 构造OpenAI格式响应
            openai_response = {
                "id": f"chatcmpl-{uuid.uuid4().hex}",
                "object": "chat.completion",
                "created": int(time.time()),
                "model": request_data.get("model", "dashscope-app"),
                "choices": [
                    {
                        "index": 0,
                        "message": {
                            "role": "assistant",
                            "content": text_content,
                        },
                        "finish_reason": finish_reason
                    }
                ],
                "usage": {
                    "prompt_tokens": -1,
                    "completion_tokens": -1,
                    "total_tokens": -1
                }
            }
            
            # 如果有使用情况信息，添加到响应中
            if "usage" in dashscope_response:
                openai_response["usage"] = dashscope_response["usage"]
                
            return openai_response
        except Exception as e:
            logger.error(f"转换响应格式时出错: {e}")
            # 如果转换失败，构造标准OpenAI格式响应
            openai_response = {
                "id": f"chatcmpl-{uuid.uuid4().hex}",
                "object": "chat.completion",
                "created": int(time.time()),
                "model": request_data.get("model", "dashscope-app"),
                "choices": [
                    {
                        "index": 0,
                        "message": {
                            "role": "assistant",
                            "content": str(dashscope_response),
                        },
                        "finish_reason": "stop"
                    }
                ],
                "usage": {
                    "prompt_tokens": -1,
                    "completion_tokens": -1,
                    "total_tokens": -1
                }
            }
            return openai_response
    
    def do_OPTIONS(self):
        """处理CORS预检请求"""
        self.send_response(200)
        self.send_header('Access-Control-Allow-Origin', '*')
        self.send_header('Access-Control-Allow-Methods', 'POST, GET, OPTIONS')
        self.send_header('Access-Control-Allow-Headers', '*')
        self.end_headers()

def run_server(port=8000):
    """启动HTTP服务器"""
    server_address = ('', port)
    httpd = HTTPServer(server_address, OpenAIProxyHandler)
    logger.info(f"OpenAI到阿里云百炼平台转发服务启动，监听端口 {port}")
    logger.info(f"Base URL: http://localhost:{port}/v1")
    logger.info(f"模型名称: dashscope-app")
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        logger.info("服务已停止")
        httpd.server_close()

if __name__ == '__main__':
    # 可以通过环境变量设置端口，默认为8000
    port = int(os.getenv("PROXY_PORT", 8000))
    run_server(port)