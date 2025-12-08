import json
import requests
from flask import Flask, request, Response
import re

# ================= 配置区域 =================

AUTH_TOKEN = "Bearer 在此处粘贴您的最新Authorization令牌"
TARGET_URL = "https://biz-discoveryengine.googleapis.com/v1alpha/locations/global/widgetStreamAssist" 
SESSION_ID = "collections/default_collection/engines/agentspace-engine/sessions/16190456916940072265"
CONFIG_ID = "36cc1840-8078-4a90-ad0a-6f995a52af32"

# ===============================================================

app = Flask(__name__)

def parse_gemini_response(text):
    """
    最终版精确解析器。
    """
    try:
        full_response_parts = []
        lines = text.strip().split('\n')
        for line in lines:
            try:
                if line.strip().startswith('{') and line.strip().endswith('}'):
                    data = json.loads(line)
                    replies = data.get("streamAssistResponse", {}).get("answer", {}).get("replies", [])
                    
                    for reply in replies:
                        content = reply.get("groundedContent", {}).get("content", {})
                        
                        if isinstance(content, dict) and "text" in content:
                            full_response_parts.append(content["text"])
                        
                        if isinstance(content, dict) and "file" in content and "fileId" in content.get("file", {}):
                            file_id = content["file"]["fileId"]
                            image_markdown = f"\n![Generated Image](https://business.gemini.google.com/file/{file_id})\n"
                            full_response_parts.append(image_markdown)

            except (json.JSONDecodeError):
                continue
        
        if full_response_parts:
            return "".join(full_response_parts)
            
        return "未能从响应流中提取有效内容。"
    except Exception as e:
        return f"解析响应时发生未知错误: {e}"

@app.route('/v1/chat/completions', methods=['POST'])
def proxy():
    if "在此处粘贴" in AUTH_TOKEN:
        return Response('{"error": "请在 gemini_proxy.py 脚本中配置 AUTH_TOKEN"}', status=500, mimetype='application/json')

    openai_request = request.json
    messages = openai_request.get('messages', [])
    
    # 恢复到原始状态：简单拼接所有消息内容
    prompt = ""
    for msg in messages:
        role = msg.get('role', 'user')
        content = msg.get('content', '')
        prompt += f"{role}: {str(content)}\n"

    print(f"收到请求: {prompt[:100]}...")

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
        resp = requests.post(TARGET_URL, headers=headers, json=payload)
        
        if resp.status_code != 200:
            return Response(f'{{"error": "Gemini 请求失败", "status_code": {resp.status_code}, "details": "{resp.text}"}}', status=500, mimetype='application/json')
            
        gemini_text = parse_gemini_response(resp.text)
        
        openai_resp = {
            "id": "chatcmpl-gemini-enterprise-proxy",
            "object": "chat.completion",
            "created": 0,
            "choices": [{
                "index": 0,
                "message": {
                    "role": "assistant",
                    "content": gemini_text
                },
                "finish_reason": "stop"
            }],
            "usage": {"prompt_tokens": 0, "completion_tokens": 0, "total_tokens": 0}
        }
        
        return Response(json.dumps(openai_resp), mimetype='application/json')

    except Exception as e:
        return Response(f'{{"error": "代理服务器内部错误", "details": "{str(e)}"}}', status=500, mimetype='application/json')

if __name__ == '__main__':
    print("Gemini Enterprise to OpenAI Proxy")
    print("服务已启动: http://localhost:5000/v1/chat/completions")
    print("请注意：每次启动前，您可能都需要更新脚本中的 AUTH_TOKEN。")
    app.run(port=5000, debug=False)