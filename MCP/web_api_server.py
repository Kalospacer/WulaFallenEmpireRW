#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
RimWorld知识库Web API服务器
提供HTTP接口，可以通过浏览器或HTTP客户端访问
"""
import os
import sys
import json
from http.server import HTTPServer, BaseHTTPRequestHandler
from urllib.parse import urlparse, parse_qs
import threading
import webbrowser

# 添加MCP路径
MCP_DIR = os.path.dirname(os.path.abspath(__file__))
SDK_PATH = os.path.join(MCP_DIR, 'python-sdk', 'src')
if SDK_PATH not in sys.path:
    sys.path.insert(0, SDK_PATH)

class RimWorldAPIHandler(BaseHTTPRequestHandler):
    """HTTP请求处理器"""
    
    def do_GET(self):
        """处理GET请求"""
        parsed_url = urlparse(self.path)
        
        if parsed_url.path == '/':
            self.serve_web_interface()
        elif parsed_url.path == '/query':
            self.handle_query_get(parsed_url)
        elif parsed_url.path == '/api/query':
            self.handle_api_query_get(parsed_url)
        else:
            self.send_error(404, "Not Found")
    
    def do_POST(self):
        """处理POST请求"""
        if self.path == '/api/query':
            self.handle_api_query_post()
        else:
            self.send_error(404, "Not Found")
    
    def serve_web_interface(self):
        """提供Web界面"""
        html = """
<!DOCTYPE html>
<html lang="zh-CN">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>RimWorld 知识库</title>
    <style>
        body { font-family: Arial, sans-serif; max-width: 1200px; margin: 0 auto; padding: 20px; }
        .header { text-align: center; margin-bottom: 30px; }
        .query-box { margin-bottom: 20px; }
        input[type="text"] { width: 70%; padding: 10px; font-size: 16px; }
        button { padding: 10px 20px; font-size: 16px; background: #007cba; color: white; border: none; cursor: pointer; }
        button:hover { background: #005a8b; }
        .result { margin-top: 20px; padding: 15px; background: #f5f5f5; border-radius: 5px; white-space: pre-wrap; }
        .loading { color: #666; font-style: italic; }
        .examples { margin-top: 20px; }
        .example { cursor: pointer; color: #007cba; margin: 5px 0; }
        .example:hover { text-decoration: underline; }
    </style>
</head>
<body>
    <div class="header">
        <h1>🎮 RimWorld 知识库</h1>
        <p>直接查询RimWorld游戏的C#源码和XML定义</p>
    </div>
    
    <div class="query-box">
        <input type="text" id="queryInput" placeholder="输入您的问题，例如：ThingDef是什么？" onkeypress="handleKeyPress(event)">
        <button onclick="performQuery()">🔍 查询</button>
    </div>
    
    <div class="examples">
        <h3>💡 查询示例：</h3>
        <div class="example" onclick="setQuery('ThingDef的定义和用法')">• ThingDef的定义和用法</div>
        <div class="example" onclick="setQuery('如何创建Building')">• 如何创建Building</div>
        <div class="example" onclick="setQuery('Pawn类的主要方法')">• Pawn类的主要方法</div>
        <div class="example" onclick="setQuery('CompPower的使用')">• CompPower的使用</div>
    </div>
    
    <div id="result" class="result" style="display: none;"></div>
    
    <script>
        async function performQuery() {
            const input = document.getElementById('queryInput');
            const result = document.getElementById('result');
            const query = input.value.trim();
            
            if (!query) {
                alert('请输入查询问题');
                return;
            }
            
            result.style.display = 'block';
            result.textContent = '🔄 正在查询，请稍候...';
            result.className = 'result loading';
            
            try {
                const response = await fetch('/api/query', {
                    method: 'POST',
                    headers: {'Content-Type': 'application/json'},
                    body: JSON.stringify({question: query})
                });
                
                const data = await response.json();
                
                if (data.success) {
                    result.textContent = data.result;
                    result.className = 'result';
                } else {
                    result.textContent = '❌ 查询失败: ' + data.error;
                    result.className = 'result';
                }
            } catch (error) {
                result.textContent = '❌ 网络错误: ' + error.message;
                result.className = 'result';
            }
        }
        
        function setQuery(query) {
            document.getElementById('queryInput').value = query;
        }
        
        function handleKeyPress(event) {
            if (event.key === 'Enter') {
                performQuery();
            }
        }
    </script>
</body>
</html>
        """
        
        self.send_response(200)
        self.send_header('Content-Type', 'text/html; charset=utf-8')
        self.end_headers()
        self.wfile.write(html.encode('utf-8'))
    
    def handle_query_get(self, parsed_url):
        """处理GET查询请求"""
        params = parse_qs(parsed_url.query)
        question = params.get('q', [''])[0]
        
        if not question:
            self.send_error(400, "Missing 'q' parameter")
            return
        
        try:
            from mcpserver_stdio import get_context
            result = get_context(question)
            
            self.send_response(200)
            self.send_header('Content-Type', 'text/plain; charset=utf-8')
            self.end_headers()
            self.wfile.write(result.encode('utf-8'))
        except Exception as e:
            self.send_error(500, f"Query failed: {e}")
    
    def handle_api_query_get(self, parsed_url):
        """处理API GET查询"""
        params = parse_qs(parsed_url.query)
        question = params.get('q', [''])[0]
        
        if not question:
            response = {"success": False, "error": "Missing 'q' parameter"}
        else:
            try:
                from mcpserver_stdio import get_context
                result = get_context(question)
                response = {"success": True, "result": result}
            except Exception as e:
                response = {"success": False, "error": str(e)}
        
        self.send_response(200)
        self.send_header('Content-Type', 'application/json; charset=utf-8')
        self.end_headers()
        self.wfile.write(json.dumps(response, ensure_ascii=False).encode('utf-8'))
    
    def handle_api_query_post(self):
        """处理API POST查询"""
        content_length = int(self.headers['Content-Length'])
        post_data = self.rfile.read(content_length)
        
        try:
            data = json.loads(post_data.decode('utf-8'))
            question = data.get('question', '')
            
            if not question:
                response = {"success": False, "error": "Missing 'question' field"}
            else:
                from mcpserver_stdio import get_context
                result = get_context(question)
                response = {"success": True, "result": result}
        except Exception as e:
            response = {"success": False, "error": str(e)}
        
        self.send_response(200)
        self.send_header('Content-Type', 'application/json; charset=utf-8')
        self.send_header('Access-Control-Allow-Origin', '*')
        self.end_headers()
        self.wfile.write(json.dumps(response, ensure_ascii=False).encode('utf-8'))
    
    def log_message(self, format, *args):
        """自定义日志输出"""
        print(f"[{self.address_string()}] {format % args}")

def start_server(port=8080, open_browser=True):
    """启动Web服务器"""
    server_address = ('', port)
    httpd = HTTPServer(server_address, RimWorldAPIHandler)
    
    print(f"🌐 RimWorld知识库Web服务器启动")
    print(f"📍 服务地址: http://localhost:{port}")
    print(f"🔍 查询API: http://localhost:{port}/api/query?q=您的问题")
    print(f"💻 Web界面: http://localhost:{port}")
    print("按 Ctrl+C 停止服务器")
    
    if open_browser:
        # 延迟打开浏览器
        def open_browser_delayed():
            import time
            time.sleep(1)
            webbrowser.open(f'http://localhost:{port}')
        
        thread = threading.Thread(target=open_browser_delayed)
        thread.daemon = True
        thread.start()
    
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        print("\n🛑 服务器已停止")
        httpd.shutdown()

if __name__ == "__main__":
    import argparse
    
    parser = argparse.ArgumentParser(description='RimWorld知识库Web API服务器')
    parser.add_argument('--port', '-p', type=int, default=8080, help='服务器端口 (默认: 8080)')
    parser.add_argument('--no-browser', action='store_true', help='不自动打开浏览器')
    
    args = parser.parse_args()
    
    start_server(args.port, not args.no_browser)