import json
import requests

# ================= 配置区域 =================

# 1. 认证令牌 (Authorization Token)
# 从 widgetStreamAssist 请求的 "Request Headers" -> "Authorization" 复制
AUTH_TOKEN = "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCIsImtpZCI6IldBQS5BY2c4ZXVIa0xsOXFiN1BobzBtRS04YzJpV0FqSlY1dHFuZGdhTE9LNmpyeGRadmFvVG9tZjN2aG1vRlBjVVFtN1N5T1NwYm1GVUNKZU5qRkdqNG50c3dxeXphVGNEZHR1UEpwY3EzQUpidV9LV0I1N1Zmb1phWWQ2ay1VMHNUX3lPNWs4T1hIUHZaSGRpWm9xSmlIVWNNM2ZpbFNnYU9UWVJWT2dBNGFSeExMckNCTHRPNjV4WERaTGVzeFJKNmpFM3FISTBNQjYxeG1hTG5WYTNOUEhGYkI4WnhXcHA3M212SXdnQVhYU1k2RlU1SnQ1LWdKSUlxTXV4WVZ5dXlxVTNodV9iREF3enBvaVBaSU1nVi02ZE5LdWc3SlJfMWNJUTdhZE91YnlpdVA0Q0hqLV9za0haUzgweGd2aXA4S3Z1Ui1lRnppRWlPXzRmMGxoYjlrQk5tYzQ0RkxRQ2ZXVC1YTi1JTG1uNC1LcjgxMFNreDc0a3dVMUZjRWw2dkZJYnI1VmlrRnpIdlNJUkNxakFyX0x3TzFUN3ZMbDd5LXNiSHlJWWN4OUdNd3dhSnZIYy11YlhnUkl4cm1vdG1IUTlYLXd6UEpRTnpxQlVrOHRHSVdiNGxaQ2ZsZjh4Rk5IQ3R3YVFNdVNtZGZzc1ByT0NUN2d2YyJ9.eyJpc3MiOiJodHRwczovL2J1c2luZXNzLmdlbWluaS5nb29nbGUiLCJhdWQiOiJodHRwczovL2Jpei1kaXNjb3ZlcnllbmdpbmUuZ29vZ2xlYXBpcy5jb20iLCJzdWIiOiJjc2VzaWR4LzMyNjAzOTY4OCIsImlhdCI6MTc2NTE2NjUwMSwiZXhwIjoxNzY1MTY2ODAxLCJuYmYiOjE3NjUxNjY1MDF9.g_pzR-3_OaL8eXHh_5alAuJeMKL56O5rS1JcX63Twmw"

# 2. 接口地址
TARGET_URL = "https://biz-discoveryengine.googleapis.com/v1alpha/locations/global/widgetStreamAssist" 

# 3. 会话 ID 和 Config ID (从真实 Payload 复制)
SESSION_ID = "collections/default_collection/engines/agentspace-engine/sessions/16611680310063068731"
CONFIG_ID = "36cc1840-8078-4a90-ad0a-6f995a52af32"

# ===============================================================

def parse_gemini_response(text):
    """
    根据您提供的真实响应体，更新解析器。
    """
    print("------ 原始响应 ------")
    print(text)
    print("--------------------")
    try:
        full_response = ""
        # 响应体可能是一系列由换行符分隔的JSON对象
        # 我们需要找到包含有效文本块的部分
        # 注意：真实的流式响应可能更复杂，这里是一个简化版解析
        lines = text.strip().split('\n')
        for line in lines:
            try:
                data = json.loads(line)
                # 根据您提供的响应体，路径是 streamAssistResponse -> answer -> replies -> [0] -> groundedContent -> content -> text
                text_chunk = data["streamAssistResponse"]["answer"]["replies"][0]["groundedContent"]["content"]["text"]
                full_response += text_chunk
            except (KeyError, IndexError, TypeError, json.JSONDecodeError):
                continue
        if full_response:
            return full_response
        return "未能从响应流中提取有效文本。"
    except Exception as e:
        return f"解析响应失败: {e}"

def send_test_request(prompt: str):
    """
    发送一次测试请求并返回结果。
    """
    if "在此处粘贴" in AUTH_TOKEN:
        return "错误: 请在脚本中填入真实的 AUTH_TOKEN"

    # 使用您提供的真实Payload结构，并动态插入prompt
    # 关键修正：在prompt前添加 '\n' 以完全模拟浏览器行为
    payload = {
        "configId": CONFIG_ID,
        "additionalParams": {"token": "-"},
        "streamAssistRequest": {
            "session": SESSION_ID,
            "query": {"parts": [{"text": "\n" + prompt}]}, # <--- 关键修正
            "filter": "",
            "fileIds": [],
            "answerGenerationMode": "NORMAL",
            "toolsSpec": {
                "webGroundingSpec": {},
                "toolRegistry": "default_tool_registry",
                "imageGenerationSpec": {},
                "videoGenerationSpec": {}
            },
            "languageCode": "zh-CN",
            "userMetadata": {"timeZone": "Asia/Shanghai"},
            "assistSkippingMode": "REQUEST_ASSIST",
            "assistGenerationConfig": {"modelId": "gemini-3-pro-preview"}
        }
    }

    # 使用您提供的完整请求头
    headers = {
        "accept": "*/*",
        "accept-language": "zh-CN,zh;q=0.9,en;q=0.8,en-GB;q=0.7,en-US;q=0.6",
        "authorization": AUTH_TOKEN,
        "content-type": "application/json",
        "origin": "https://business.gemini.google",
        "priority": "u=1, i",
        "referer": "https://business.gemini.google/",
        "sec-ch-ua": '"Microsoft Edge";v="143", "Chromium";v="143", "Not A(Brand";v="24"',
        "sec-ch-ua-mobile": "?0",
        "sec-ch-ua-platform": '"Windows"',
        "sec-fetch-dest": "empty",
        "sec-fetch-mode": "cors",
        "sec-fetch-site": "cross-site",
        "user-agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36 Edg/143.0.0.0",
        "x-server-timeout": "1800",
    }

    try:
        print(f"正在发送测试请求: {prompt[:100]}...")
        resp = requests.post(TARGET_URL, headers=headers, json=payload)
        
        if resp.status_code != 200:
            return f"请求失败! 状态码: {resp.status_code}\n响应内容:\n{resp.text}"
            
        return parse_gemini_response(resp.text)

    except Exception as e:
        return f"发生异常: {e}"

if __name__ == '__main__':
    test_prompt = "你好世界"
    result = send_test_request(test_prompt)
    print("\n====== 测试结果 ======")
    print(result)
    print("======================")