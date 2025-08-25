#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
RimWorld知识库命令行工具
快速查询工具，无需Qoder IDE
"""
import os
import sys
import argparse
import json

# 添加MCP路径
MCP_DIR = os.path.dirname(os.path.abspath(__file__))
SDK_PATH = os.path.join(MCP_DIR, 'python-sdk', 'src')
if SDK_PATH not in sys.path:
    sys.path.insert(0, SDK_PATH)

def quick_query(question: str, format_output: bool = True) -> str:
    """快速查询函数"""
    try:
        # 动态导入避免启动时的依赖检查
        from mcpserver_stdio import get_context
        result = get_context(question)
        
        if format_output:
            # 格式化输出
            lines = result.split('\n')
            formatted_lines = []
            current_section = ""
            
            for line in lines:
                if line.startswith('--- 结果'):
                    current_section = f"\n🔍 {line}"
                    formatted_lines.append(current_section)
                elif line.startswith('文件路径:'):
                    formatted_lines.append(f"📄 {line}")
                elif line.strip() and not line.startswith('---'):
                    formatted_lines.append(line)
            
            return '\n'.join(formatted_lines)
        else:
            return result
            
    except Exception as e:
        return f"❌ 查询失败: {e}"

def main():
    parser = argparse.ArgumentParser(
        description='RimWorld知识库命令行查询工具',
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
使用示例:
  %(prog)s "ThingDef是什么"
  %(prog)s "如何创建新的Pawn" --raw
  %(prog)s "建筑物定义" --output result.txt
  %(prog)s --list-examples
        """
    )
    
    parser.add_argument('question', nargs='?', help='要查询的问题')
    parser.add_argument('--raw', action='store_true', help='输出原始结果，不格式化')
    parser.add_argument('--output', '-o', help='将结果保存到文件')
    parser.add_argument('--list-examples', action='store_true', help='显示查询示例')
    
    args = parser.parse_args()
    
    if args.list_examples:
        print("📚 RimWorld知识库查询示例:")
        examples = [
            "ThingDef的定义和用法",
            "如何创建新的Building",
            "Pawn类的主要方法",
            "CompPower的使用方法",
            "XML中的defName规则",
            "GenConstruct.CanPlaceBlueprintAt",
            "Building_Door的开关逻辑"
        ]
        for i, example in enumerate(examples, 1):
            print(f"  {i}. {example}")
        return
    
    if not args.question:
        parser.print_help()
        return
    
    print(f"🔍 正在查询: {args.question}")
    
    result = quick_query(args.question, not args.raw)
    
    if args.output:
        try:
            with open(args.output, 'w', encoding='utf-8') as f:
                f.write(result)
            print(f"✅ 结果已保存到: {args.output}")
        except Exception as e:
            print(f"❌ 保存文件失败: {e}")
    else:
        print("\n" + "="*60)
        print("📖 查询结果:")
        print("="*60)
        print(result)
        print("="*60)

if __name__ == "__main__":
    main()