#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
验证MCP服务器配置和环境
"""
import os
import sys
import subprocess
import json

def test_mcp_configuration():
    """测试MCP配置是否正确"""
    print("🔍 MCP配置验证工具")
    print("=" * 50)
    
    # 1. 检查Python环境
    print(f"✓ Python解释器: {sys.executable}")
    print(f"✓ Python版本: {sys.version}")
    
    # 2. 检查工作目录
    mcp_dir = os.path.dirname(os.path.abspath(__file__))
    print(f"✓ MCP目录: {mcp_dir}")
    
    # 3. 检查MCP SDK
    sdk_path = os.path.join(mcp_dir, 'python-sdk', 'src')
    print(f"✓ SDK路径: {sdk_path}")
    print(f"✓ SDK存在: {os.path.exists(sdk_path)}")
    
    # 4. 检查必要文件
    server_script = os.path.join(mcp_dir, 'mcpserver_stdio.py')
    env_file = os.path.join(mcp_dir, '.env')
    print(f"✓ 服务器脚本: {os.path.exists(server_script)}")
    print(f"✓ 环境文件: {os.path.exists(env_file)}")
    
    # 5. 检查依赖包
    try:
        import mcp
        print("✓ MCP SDK: 已安装")
    except ImportError as e:
        print(f"❌ MCP SDK: 未安装 - {e}")
    
    try:
        import dashscope
        print("✓ DashScope: 已安装")
    except ImportError as e:
        print(f"❌ DashScope: 未安装 - {e}")
    
    try:
        import openai
        print("✓ OpenAI: 已安装")
    except ImportError as e:
        print(f"❌ OpenAI: 未安装 - {e}")
    
    # 6. 生成正确的配置
    python_exe = sys.executable.replace("\\", "\\\\")
    mcp_dir_escaped = mcp_dir.replace("\\", "\\\\")
    sdk_path_escaped = sdk_path.replace("\\", "\\\\")
    
    config = {
        "mcpServers": {
            "rimworld-knowledge-base": {
                "command": python_exe,
                "args": ["mcpserver_stdio.py"],
                "cwd": mcp_dir_escaped,
                "disabled": False,
                "alwaysAllow": [],
                "env": {
                    "PYTHONPATH": sdk_path_escaped
                }
            }
        },
        "tools": {
            "rimworld-knowledge-base": {
                "description": "从RimWorld本地知识库（包括C#源码和XML）中检索上下文。",
                "server_name": "rimworld-knowledge-base",
                "tool_name": "get_context",
                "input_schema": {
                    "type": "object",
                    "properties": {
                        "question": {
                            "type": "string",
                            "description": "关于RimWorld开发的问题，应包含代码或XML中的关键词。"
                        }
                    },
                    "required": ["question"]
                }
            }
        }
    }
    
    print("\\n📋 建议的MCP配置:")
    print("=" * 50)
    print(json.dumps(config, indent=2, ensure_ascii=False))
    
    # 7. 测试服务器启动
    print("\\n🚀 测试服务器启动:")
    print("=" * 50)
    try:
        result = subprocess.run(
            [sys.executable, server_script],
            cwd=mcp_dir,
            capture_output=True,
            text=True,
            timeout=10
        )
        if result.returncode == 0:
            print("✓ 服务器可以正常启动")
        else:
            print(f"❌ 服务器启动失败: {result.stderr}")
    except subprocess.TimeoutExpired:
        print("✓ 服务器启动正常（超时保护触发）")
    except Exception as e:
        print(f"❌ 服务器测试失败: {e}")
    
    print("\\n🎯 配置建议:")
    print("=" * 50)
    print("1. 复制上面的配置到 Qoder IDE 的 MCP 设置中")
    print("2. 确保所有依赖包都已安装")
    print("3. 检查 .env 文件中的 API Key 配置")
    print("4. 重启 Qoder IDE 并重新连接 MCP 服务器")

if __name__ == "__main__":
    test_mcp_configuration()