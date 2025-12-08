import json
import requests
import re

# ================= 配置区域 =================

# 1. 认证令牌 (Authorization Token)
# 每次运行前，请确保从浏览器开发者工具中获取一个新的令牌
AUTH_TOKEN = "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCIsImtpZCI6IldBQS5BY2c4ZXVIWHo0NkI4SkctTFNCb1BwaEwzWEVCclczQXoxUWtSRDhXb1FITzV1aVY5a3lySHFUTldyWHE3Z3FGQkFNWDlCUXc3RWd3RnBXRXJFY3BER3NTSU43T1M3c3ppTHd5YXNUTXVfd0l1S0s4VGlFc2tGcE5haTZzcW1mX0RFQlJWN2VCSFZ3ZHhqR2hqMjN4WlVXeHhEbUZ0b3R4dkFwT2ZQNTlvMHplUzRTek1XeVNuRWFwUWJtNmVJZHdtRUZwZF9iTi1tU01IQWNaU0RjTHhrR1k3bVJmSXNVejhsT1MxR3NrNzZ4N0pfZnZyRGUzRm95X1VHSWdmcmcyYXlJNDBFVlpfZ2t2Y1pKbk5nMHdDbG95d1pjYWVNUno0UVZjcnZJR0d0Mm05RlgxdEJwNTBZa3ZMdFNBQ1NzX0E5N2pPTTFGQkROcHc5VHFWWmY2eUlwRW1xNnVDSVlsekxyQy13ZDJ5QTNZbjRiSW5sV0FrNnFyMXRLY1RWU0FTZ3dOYjV1Wl94YmFTRXFCdy15RkhRQm1zOUgwcF8xZnJqS1R3bG53Wkl3UjlXU1pUSkMxY3gwWVFfeTBDTlBzeWxDR3JNWEZEbzBmRFFHV3hzd21lT2xqZWd3SzBjdlRPX3hUVk04NzZWRVk0R3o2b0pDTmFMSSJ9.eyJpc3MiOiJodHRwczovL2J1c2luZXNzLmdlbWluaS5nb29nbGUiLCJhdWQiOiJodHRwczovL2Jpei1kaXNjb3ZlcnllbmdpbmUuZ29vZ2xlYXBpcy5jb20iLCJzdWIiOiJjc2VzaWR4LzMyNjAzOTY4OCIsImlhdCI6MTc2NTE2NzM4NiwiZXhwIjoxNzY1MTY3Njg2LCJuYmYiOjE3NjUxNjczODZ9.urQLDoUDAEjQhX8c42hJfKU28WoKn8-0tGjgX5RqhtY"

# 2. 接口地址
TARGET_URL = "https://biz-discoveryengine.googleapis.com/v1alpha/locations/global/widgetStreamAssist" 

# 3. 会话 ID 和 Config ID (使用图片生成时的固定值)
SESSION_ID = "collections/default_collection/engines/agentspace-engine/sessions/16190456916940072265"
CONFIG_ID = "36cc1840-8078-4a90-ad0a-6f995a52af32"

# ===============================================================

def parse_gemini_response(text):
    """
    根据真实的流式响应，精确解析并拼接 Gemini 的文本和图片文件回复。
    """
    print("------ 原始响应 ------")
    print(text)
    print("--------------------")
    try:
        full_response_parts = []
        # 响应体是由换行符分隔的一系列JSON对象
        lines = text.strip().split('\n')
        for line in lines:
            try:
                data = json.loads(line)
                replies = data.get("streamAssistResponse", {}).get("answer", {}).get("replies", [])
                
                for reply in replies:
                    content = reply.get("groundedContent", {}).get("content", {})
                    
                    # 检查并提取文本内容
                    if "text" in content:
                        full_response_parts.append(content["text"])
                    
                    # 检查并提取文件（图片）内容
                    if "file" in content and "fileId" in content["file"]:
                        file_id = content["file"]["fileId"]
                        # 将 fileId 构造成一个 Markdown 图片链接。
                        image_markdown = f"\n![Generated Image](https://business.gemini.google.com/file/{file_id})\n"
                        full_response_parts.append(image_markdown)

            except (json.JSONDecodeError):
                # 忽略无法解析为JSON的行
                continue
        
        if full_response_parts:
            return "".join(full_response_parts)
            
        return "未能从响应流中提取有效内容。"
    except Exception as e:
        return f"解析响应时发生未知错误: {e}"

def send_test_request(prompt: str):
    """
    发送一次测试请求并返回结果。
    """
    if "在此处粘贴" in AUTH_TOKEN:
        return "错误: 请在脚本中填入真实的 AUTH_TOKEN"

    payload = {
        "configId": CONFIG_ID,
        "additionalParams": {"token": "-"},
        "streamAssistRequest": {
            "session": SESSION_ID,
            "query": {"parts": [{"text": "\n" + prompt}]},
            "answerGenerationMode": "NORMAL",
            "assistGenerationConfig": {"modelId": "gemini-3-pro-preview"},
            "assistSkippingMode": "REQUEST_ASSIST",
            "languageCode": "zh-CN",
            "userMetadata": {"timeZone": "Asia/Shanghai"}
        }
    }

    headers = {
        "accept": "*/*",
        "authorization": AUTH_TOKEN,
        "content-type": "application/json",
        "origin": "https://business.gemini.google",
        "referer": "https://business.gemini.google/",
        "user-agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36 Edg/143.0.0.0",
    }

    try:
        print(f"正在发送图片生成测试请求: {prompt[:100]}...")
        resp = requests.post(TARGET_URL, headers=headers, json=payload)
        
        if resp.status_code != 200:
            return f"请求失败! 状态码: {resp.status_code}\n响应内容:\n{resp.text}"
            
        return parse_gemini_response(resp.text)

    except Exception as e:
        return f"发生异常: {e}"

if __name__ == '__main__':
    test_prompt = "画一只戴着宇航员头盔的猫"
    result = send_test_request(test_prompt)
    print("\n====== 图片生成测试结果 ======")
    print(result)
    print("==============================")