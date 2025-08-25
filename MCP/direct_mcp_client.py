#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
直接调用MCP服务器的Python接口
绕过Qoder IDE，直接使用RimWorld知识库
"""
import os
import sys

# 添加MCP路径
MCP_DIR = os.path.dirname(os.path.abspath(__file__))
SDK_PATH = os.path.join(MCP_DIR, 'python-sdk', 'src')
if SDK_PATH not in sys.path:
    sys.path.insert(0, SDK_PATH)

# 导入MCP服务器
from mcpserver_stdio import get_context

class DirectMCPClient:
    """直接调用MCP服务器的客户端"""
    
    def __init__(self):
        print("🚀 直接MCP客户端已启动")
        print("📚 RimWorld知识库已加载")
        
    def query(self, question: str) -> str:
        """查询RimWorld知识库"""
        try:
            print(f"🔍 正在查询: {question}")
            result = get_context(question)
            return result
        except Exception as e:
            return f"查询出错: {e}"
    
    def interactive_mode(self):
        """交互模式"""
        print("\n" + "="*60)
        print("🎯 RimWorld知识库 - 交互模式")
        print("输入问题查询知识库，输入 'quit' 或 'exit' 退出")
        print("="*60)
        
        while True:
            try:
                question = input("\n❓ 请输入您的问题: ").strip()
                
                if question.lower() in ['quit', 'exit', '退出', 'q']:
                    print("👋 再见！")
                    break
                
                if not question:
                    print("⚠️ 请输入有效的问题")
                    continue
                
                print("\n🔄 正在搜索...")
                result = self.query(question)
                
                print("\n📖 查询结果:")
                print("-" * 50)
                print(result)
                print("-" * 50)
                
            except KeyboardInterrupt:
                print("\n\n👋 用户中断，退出程序")
                break
            except Exception as e:
                print(f"\n❌ 出现错误: {e}")

def main():
    """主函数"""
    import argparse
    
    parser = argparse.ArgumentParser(description='直接调用RimWorld MCP知识库')
    parser.add_argument('--query', '-q', type=str, help='直接查询问题')
    parser.add_argument('--interactive', '-i', action='store_true', help='进入交互模式')
    
    args = parser.parse_args()
    
    client = DirectMCPClient()
    
    if args.query:
        # 直接查询模式
        result = client.query(args.query)
        print("\n📖 查询结果:")
        print("="*60)
        print(result)
        print("="*60)
    elif args.interactive:
        # 交互模式
        client.interactive_mode()
    else:
        # 默认显示帮助
        print("\n🔧 使用方法:")
        print("1. 直接查询: python direct_mcp_client.py -q \"ThingDef是什么\"")
        print("2. 交互模式: python direct_mcp_client.py -i")
        print("3. 查看帮助: python direct_mcp_client.py -h")
        
        # 演示查询
        print("\n🎬 演示查询:")
        demo_result = client.query("ThingDef")
        print(demo_result[:500] + "..." if len(demo_result) > 500 else demo_result)

if __name__ == "__main__":
    main()