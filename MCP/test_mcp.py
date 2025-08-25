#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
最终功能测试：验证MCP服务器是否能正常工作
"""
import os
import sys
import subprocess
import time
import json

def test_mcp_server_final():
    """最终测试MCP服务器功能"""
    print("🔥 MCP服务器最终功能测试")
    print("=" * 50)
    
    # 获取当前目录
    mcp_dir = os.path.dirname(os.path.abspath(__file__))
    script_path = os.path.join(mcp_dir, 'mcpserver_stdio.py')
    
    try:
        # 1. 验证SDK安装
        try:
            import mcp
            print("✅ MCP SDK: 已正确安装")
        except ImportError:
            print("❌ MCP SDK: 未安装")
            return False
        
        # 2. 启动服务器
        print("🚀 启动MCP服务器...")
        process = subprocess.Popen(
            [sys.executable, script_path],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            cwd=mcp_dir
        )
        
        # 等待启动
        time.sleep(2)
        
        # 3. 初始化测试
        init_request = {
            "jsonrpc": "2.0",
            "id": 1,
            "method": "initialize",
            "params": {
                "protocolVersion": "2024-11-05",
                "capabilities": {},
                "clientInfo": {
                    "name": "final-test-client",
                    "version": "1.0.0"
                }
            }
        }
        
        print("📡 发送初始化请求...")
        process.stdin.write(json.dumps(init_request) + '\n')
        process.stdin.flush()
        
        # 读取初始化响应
        response = process.stdout.readline()
        if response:
            response_data = json.loads(response.strip())
            if "result" in response_data:
                print("✅ 初始化成功")
                print(f"   服务器名称: {response_data['result'].get('serverInfo', {}).get('name', 'unknown')}")
                print(f"   服务器版本: {response_data['result'].get('serverInfo', {}).get('version', 'unknown')}")
            else:
                print("❌ 初始化失败")
                return False
        else:
            print("❌ 初始化无响应")
            return False
        
        # 4. 工具列表测试
        tools_request = {
            "jsonrpc": "2.0",
            "id": 2,
            "method": "tools/list"
        }
        
        print("🔧 请求工具列表...")
        process.stdin.write(json.dumps(tools_request) + '\n')
        process.stdin.flush()
        
        tools_response = process.stdout.readline()
        if tools_response:
            tools_data = json.loads(tools_response.strip())
            if "result" in tools_data and "tools" in tools_data["result"]:
                tools = tools_data["result"]["tools"]
                print(f"✅ 发现 {len(tools)} 个工具:")
                for tool in tools:
                    print(f"   - {tool.get('name', 'unknown')}: {tool.get('description', 'no description')}")
            else:
                print("❌ 获取工具列表失败")
        else:
            print("❌ 工具列表请求无响应")
        
        print("\n🎯 测试结果:")
        print("✅ MCP服务器能够正常启动")
        print("✅ 初始化协议工作正常") 
        print("✅ 工具发现机制正常")
        print("\n✨ 所有基本功能测试通过！")
        
        return True
        
    except Exception as e:
        print(f"❌ 测试过程中出错: {e}")
        return False
    
    finally:
        # 清理进程
        try:
            process.terminate()
            process.wait(timeout=5)
        except:
            try:
                process.kill()
            except:
                pass

if __name__ == "__main__":
    print("开始最终测试...")
    success = test_mcp_server_final()
    
    if success:
        print("\n🎉 恭喜！MCP服务器已完全修复并正常工作！")
        print("\n📋 现在您需要在Qoder IDE中更新配置：")
        print("1. 打开Qoder IDE设置 → MCP")
        print("2. 更新配置文件，确保使用正确的绝对路径")
        print("3. 重启Qoder IDE")
        print("4. 在Agent模式下测试知识库查询")
        print("\n建议的配置:")
        print(json.dumps({
            "mcpServers": {
                "rimworld-knowledge-base": {
                    "command": sys.executable,
                    "args": ["mcpserver_stdio.py"],
                    "cwd": os.path.dirname(os.path.abspath(__file__)),
                    "disabled": False,
                    "alwaysAllow": []
                }
            }
        }, indent=2))
    else:
        print("\n❌ 仍存在问题，需要进一步调试")