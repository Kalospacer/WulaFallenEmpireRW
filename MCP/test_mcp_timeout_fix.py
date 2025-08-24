#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
测试MCP服务器超时修复
"""
import os
import sys
import subprocess
import time
import json

def test_mcp_server_timeout_fix():
    """测试MCP服务器是否能快速启动并响应"""
    print("开始测试MCP服务器超时修复...")
    
    # 获取当前目录
    mcp_dir = os.path.dirname(os.path.abspath(__file__))
    script_path = os.path.join(mcp_dir, 'mcpserver_stdio.py')
    
    try:
        # 启动MCP服务器进程
        print("启动MCP服务器...")
        start_time = time.time()
        
        process = subprocess.Popen(
            [sys.executable, script_path],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            cwd=mcp_dir
        )
        
        # 等待服务器启动（减少等待时间）
        time.sleep(2)  # 从3秒减少到2秒
        
        startup_time = time.time() - start_time
        print(f"服务器启动耗时: {startup_time:.2f}秒")
        
        # 发送初始化请求
        init_request = {
            "jsonrpc": "2.0",
            "id": 1,
            "method": "initialize",
            "params": {
                "protocolVersion": "2024-11-05",
                "capabilities": {},
                "clientInfo": {
                    "name": "test-client",
                    "version": "1.0.0"
                }
            }
        }
        
        print("发送初始化请求...")
        request_start = time.time()
        process.stdin.write(json.dumps(init_request) + '\n')
        process.stdin.flush()
        
        # 读取响应
        response_line = process.stdout.readline()
        init_time = time.time() - request_start
        
        if response_line:
            print(f"✅ 初始化成功，耗时: {init_time:.2f}秒")
            print(f"收到响应: {response_line.strip()}")
        else:
            print("❌ 初始化失败：无响应")
            return False
        
        # 发送简单的工具调用请求
        tool_request = {
            "jsonrpc": "2.0",
            "id": 2,
            "method": "tools/call",
            "params": {
                "name": "get_context",
                "arguments": {
                    "question": "ThingDef"  # 简单的测试查询
                }
            }
        }
        
        print("发送工具调用请求...")
        tool_start = time.time()
        process.stdin.write(json.dumps(tool_request) + '\n')
        process.stdin.flush()
        
        # 等待响应（减少超时时间）
        timeout = 20  # 从30秒减少到20秒
        response_received = False
        
        while time.time() - tool_start < timeout:
            if process.poll() is not None:
                print("服务器进程已退出")
                break
                
            response_line = process.stdout.readline()
            if response_line:
                tool_time = time.time() - tool_start
                print(f"✅ 工具调用成功，耗时: {tool_time:.2f}秒")
                print(f"工具调用响应: {response_line.strip()[:200]}...")  # 只显示前200个字符
                response_received = True
                break
                
            time.sleep(0.1)
        
        total_time = time.time() - start_time
        
        if response_received:
            print(f"✅ 测试成功：MCP服务器能够正常处理请求")
            print(f"总耗时: {total_time:.2f}秒")
            
            # 性能评估
            if total_time < 15:
                print("🚀 性能优秀：服务器响应速度很快")
            elif total_time < 25:
                print("✅ 性能良好：服务器响应速度可接受")
            else:
                print("⚠️ 性能一般：服务器响应较慢，可能仍有超时风险")
                
        else:
            print("❌ 测试失败：超时未收到响应")
            return False
            
    except Exception as e:
        print(f"❌ 测试出错: {e}")
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
        print("测试完成")
        
    return True

if __name__ == "__main__":
    success = test_mcp_server_timeout_fix()
    if success:
        print("\n🎉 MCP服务器超时问题已修复！")
        print("现在可以在Qoder IDE中重新连接MCP服务器了。")
    else:
        print("\n❌ MCP服务器仍存在问题，需要进一步调试。")