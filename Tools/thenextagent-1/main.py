import base64
import re
import time
from openai import OpenAI
import pyautogui
from PIL import Image
import io
import json
import tkinter as tk
import subprocess
import platform
import os

class VLMAgent:
    def __init__(self, api_key, model_name="qwen3-vl-plus"):
        """
        初始化VLM代理
        """
        self.client = OpenAI(
            api_key=api_key,
            base_url="https://dashscope.aliyuncs.com/compatible-mode/v1"
        )
        self.model_name = model_name
        self.messages = []
        self.screen_width, self.screen_height = self.get_screen_resolution()
        print(f"屏幕分辨率: {self.screen_width} x {self.screen_height}")
        
        # 启用PyAutoGUI的安全机制，将鼠标移到屏幕左上角可紧急停止
        pyautogui.FAILSAFE = True
        pyautogui.PAUSE = 1  # 每次操作后暂停1秒
        
        self.tools = {
            "mouse_click": self.mouse_click,
            "type_text": self.type_text,
            "scroll_window": self.scroll_window,
            "close_window": self.close_window,
            "press_windows_key": self.press_windows_key,
            "press_enter": self.press_enter,
            "delete_text": self.delete_text,
            "mouse_drag": self.mouse_drag,
            "wait": self.wait,
            "open_terminal": self.open_terminal,
            "press_hotkey": self.press_hotkey
        }
        
    def mouse_drag(self, start_x, start_y, end_x, end_y, duration=0.5):
        """
        鼠标拖拽工具 - 从起始坐标拖拽到结束坐标
        :param start_x: 起始点比例x坐标 (0-1之间的小数)
        :param start_y: 起始点比例y坐标 (0-1之间的小数)
        :param end_x: 结束点比例x坐标 (0-1之间的小数)
        :param end_y: 结束点比例y坐标 (0-1之间的小数)
        :param duration: 拖拽过程耗时（秒），默认为0.5秒
        """
        try:
            # 将比例坐标转换为实际屏幕坐标
            actual_start_x = int(start_x * self.screen_width)
            actual_start_y = int(start_y * self.screen_height)
            actual_end_x = int(end_x * self.screen_width)
            actual_end_y = int(end_y * self.screen_height)
            
            print(f"拖拽起始坐标转换: ({start_x:.3f}, {start_y:.3f}) -> ({actual_start_x}, {actual_start_y})")
            print(f"拖拽结束坐标转换: ({end_x:.3f}, {end_y:.3f}) -> ({actual_end_x}, {actual_end_y})")
            
            # 验证起始坐标范围
            if not (0 <= actual_start_x <= self.screen_width and 0 <= actual_start_y <= self.screen_height):
                return f"起始坐标 ({actual_start_x}, {actual_start_y}) 超出屏幕范围 (0-{self.screen_width}, 0-{self.screen_height})"
            
            # 验证结束坐标范围
            if not (0 <= actual_end_x <= self.screen_width and 0 <= actual_end_y <= self.screen_height):
                return f"结束坐标 ({actual_end_x}, {actual_end_y}) 超出屏幕范围 (0-{self.screen_width}, 0-{self.screen_height})"
            
            # 执行拖拽操作
            pyautogui.moveTo(actual_start_x, actual_start_y)
            pyautogui.dragTo(actual_end_x, actual_end_y, duration=duration)
            
            return f"成功从坐标 ({actual_start_x}, {actual_start_y}) 拖拽到 ({actual_end_x}, {actual_end_y}) (比例坐标: ({start_x:.3f}, {start_y:.3f}) -> ({end_x:.3f}, {end_y:.3f}))"
        except Exception as e:
            return f"拖拽操作失败: {str(e)}"
    
    def wait(self, seconds):
        """
        等待工具 - 等待指定的秒数
        :param seconds: 等待时间（秒），可以是整数或小数
        """
        try:
            # 确保等待时间是合理的数值
            wait_time = float(seconds)
            if wait_time <= 0:
                return "等待时间必须是正数"
            
            print(f"等待 {wait_time} 秒...")
            time.sleep(wait_time)
            return f"成功等待了 {wait_time} 秒"
        except Exception as e:
            return f"等待操作失败: {str(e)}"
    
    def open_terminal(self, command=""):
        """
        打开新终端窗口的工具
        :param command: 可选，在新终端中执行的命令
        """
        try:
            system = platform.system()
            
            if system == "Windows":
                if command:
                    # 在新终端窗口中执行命令
                    cmd = f'start cmd /k "{command}"'
                    subprocess.run(cmd, shell=True)
                else:
                    # 仅打开新终端窗口
                    subprocess.run('start cmd', shell=True)
                    
            elif system == "Darwin":  # macOS
                if command:
                    # 在新终端窗口中执行命令
                    subprocess.run(['osascript', '-e', f'tell app "Terminal" to do script "{command}"'])
                    subprocess.run(['osascript', '-e', 'tell app "Terminal" to activate'])
                else:
                    # 仅打开新终端窗口
                    subprocess.run(['open', '-a', 'Terminal'])
                    
            else:  # Linux或其他Unix系统
                terminals = ['gnome-terminal', 'konsole', 'xterm']
                terminal_found = False
                
                for terminal in terminals:
                    if subprocess.run(['which', terminal], capture_output=True).returncode == 0:
                        if command:
                            if terminal == 'gnome-terminal':
                                subprocess.run([terminal, '--', 'bash', '-c', f'{command}; exec bash'])
                            elif terminal == 'konsole':
                                subprocess.run([terminal, '-e', 'bash', '-c', f'{command}; exec bash'])
                            else:  # xterm
                                subprocess.run([terminal, '-e', 'bash', '-c', f'{command}; exec bash'])
                        else:
                            subprocess.run([terminal])
                        
                        terminal_found = True
                        break
                
                if not terminal_found:
                    return f"未找到支持的终端程序，支持的终端包括: {', '.join(terminals)}"
            
            if command:
                return f"成功在新终端中执行命令: {command}"
            else:
                return "成功打开新终端窗口"
                
        except Exception as e:
            return f"打开终端失败: {str(e)}"
    
    def press_hotkey(self, x, y, hotkey):
        """
        在指定位置点击后模拟键盘快捷键的工具
        :param x: 比例x坐标 (0-1之间的小数)
        :param y: 比例y坐标 (0-1之间的小数)
        :param hotkey: 快捷键组合，例如 "ctrl+c", "ctrl+v", "alt+f4" 等
        """
        try:
            # 将比例坐标转换为实际屏幕坐标
            actual_x = int(x * self.screen_width)
            actual_y = int(y * self.screen_height)
            
            print(f"定位到坐标: ({actual_x}, {actual_y}) (比例坐标: {x:.3f}, {y:.3f})")
            
            # 验证坐标范围
            if not (0 <= actual_x <= self.screen_width and 0 <= actual_y <= self.screen_height):
                return f"坐标 ({actual_x}, {actual_y}) 超出屏幕范围 (0-{self.screen_width}, 0-{self.screen_height})"
            
            # 点击指定位置
            pyautogui.click(actual_x, actual_y)
            time.sleep(0.5)  # 等待点击生效
            
            # 解析快捷键组合
            keys = hotkey.lower().replace('+', ' ').replace('-', ' ').split()
            
            # 执行快捷键
            if len(keys) == 1:
                pyautogui.press(keys[0])
            else:
                # 使用hotkey方法处理组合键
                pyautogui.hotkey(*keys)
            
            return f"成功在坐标 ({actual_x}, {actual_y}) 处点击并执行快捷键: {hotkey}"
        except Exception as e:
            return f"执行快捷键失败: {str(e)}"
    
    def close_window(self, x, y):
        """
        关闭窗口工具 - 先点击目标窗口获取焦点，再关闭窗口
        :param x: 比例x坐标 (0-1之间的小数)
        :param y: 比例y坐标 (0-1之间的小数)
        """
        try:
            # 先点击目标窗口获取焦点
            actual_x = int(x * self.screen_width)
            actual_y = int(y * self.screen_height)
            
            print(f"点击窗口坐标: ({actual_x}, {actual_y}) (比例坐标: {x:.3f}, {y:.3f})")
            
            # 验证坐标范围
            if not (0 <= actual_x <= self.screen_width and 0 <= actual_y <= self.screen_height):
                return f"坐标 ({actual_x}, {actual_y}) 超出屏幕范围 (0-{self.screen_width}, 0-{self.screen_height})"
            
            # 点击窗口
            pyautogui.click(actual_x, actual_y)
            time.sleep(0.5)  # 等待窗口获得焦点
            
            # 关闭窗口
            pyautogui.hotkey('alt', 'f4')
            return f"成功点击窗口坐标 ({actual_x}, {actual_y}) 并关闭窗口"
        except Exception as e:
            return f"关闭窗口失败: {str(e)}"
    
    def press_windows_key(self):
        """
        按下Windows键工具
        """
        try:
            pyautogui.press('win')
            return "成功按下Windows键"
        except Exception as e:
            return f"按下Windows键失败: {str(e)}"
    
    def press_enter(self):
        """
        按下回车键工具
        """
        try:
            pyautogui.press('enter')
            return "成功按下回车键"
        except Exception as e:
            return f"按下回车键失败: {str(e)}"

    def delete_text(self, x, y, count=1):
        """
        删除输入框内文本的功能 - 点击输入框获取焦点，然后删除指定数量的字符
        :param x: 比例x坐标 (0-1之间的小数)
        :param y: 比例y坐标 (0-1之间的小数)
        :param count: 要删除的字符数量，默认为1
        """
        try:
            # 1. 将比例坐标转换为实际屏幕坐标
            actual_x = int(x * self.screen_width)
            actual_y = int(y * self.screen_height)
            
            print(f"定位到输入框坐标: ({actual_x}, {actual_y}) (比例坐标: {x:.3f}, {y:.3f})")
            
            # 2. 验证坐标范围
            if not (0 <= actual_x <= self.screen_width and 0 <= actual_y <= self.screen_height):
                return f"坐标 ({actual_x}, {actual_y}) 超出屏幕范围 (0-{self.screen_width}, 0-{self.screen_height})"
            
            # 3. 点击输入框获取焦点
            pyautogui.click(actual_x, actual_y)
            time.sleep(0.5)  # 等待点击生效
            
            # 4. 删除指定数量的字符
            for _ in range(int(count)):
                pyautogui.press('backspace')
                time.sleep(0.01)  # 每次删除之间稍作停顿
                
            return f"成功在坐标 ({actual_x}, {actual_y}) 处删除 {int(count)} 个字符"
        except Exception as e:
            return f"删除文本失败: {str(e)}"

    def get_screen_resolution(self):
        """
        获取屏幕分辨率
        """
        root = tk.Tk()
        width = root.winfo_screenwidth()
        height = root.winfo_screenheight()
        root.destroy()
        return width, height
    
    def capture_screenshot(self):
        """
        截取当前屏幕截图，并返回实际尺寸用于坐标转换
        """
        # 获取原始屏幕截图
        screenshot = pyautogui.screenshot()
        self.original_width, self.original_height = screenshot.size
        print(f"原始截图尺寸: {self.original_width} x {self.original_height}")
        
        # 缩小图片尺寸以减少API调用的数据量，但保持宽高比
        max_size = 1024
        width, height = screenshot.size
        if width > height:
            new_width = min(max_size, width)
            new_height = int(height * new_width / width)
        else:
            new_height = min(max_size, height)
            new_width = int(width * new_height / height)
            
        self.scaled_width = new_width
        self.scaled_height = new_height
        print(f"缩放后截图尺寸: {self.scaled_width} x {self.scaled_height}")
        
        screenshot = screenshot.resize((new_width, new_height))
        
        # 将截图保存到内存缓冲区
        img_buffer = io.BytesIO()
        screenshot.save(img_buffer, format='PNG')
        img_buffer.seek(0)
        
        return img_buffer

    def convert_coordinates(self, x, y):
        """
        将模型返回的坐标（基于缩放后的截图）转换为实际屏幕坐标
        """
        # 计算坐标缩放比例
        x_ratio = self.original_width / self.scaled_width
        y_ratio = self.original_height / self.scaled_height
        
        # 转换坐标
        actual_x = int(x * x_ratio)
        actual_y = int(y * y_ratio)
        
        print(f"坐标转换: ({x}, {y}) -> ({actual_x}, {actual_y}) (缩放比例: {x_ratio:.2f}, {y_ratio:.2f})")
        
        return actual_x, actual_y

    def encode_image_to_base64(self, image_buffer):
        """
        将图片编码为base64字符串
        """
        return base64.b64encode(image_buffer.read()).decode('utf-8')

    def mouse_click(self, x, y, button="left", clicks=1):
        """
        鼠标点击工具 - 使用比例坐标 (0-1之间的浮点数)
        :param x: 比例x坐标 (0-1之间的小数)
        :param y: 比例y坐标 (0-1之间的小数)
        :param button: 鼠标按键，"left"表示左键，"right"表示右键
        :param clicks: 点击次数，1表示单击，2表示双击
        """
        try:
            # 将比例坐标转换为实际屏幕坐标
            actual_x = int(x * self.screen_width)
            actual_y = int(y * self.screen_height)
            
            print(f"比例坐标转换: ({x:.3f}, {y:.3f}) -> ({actual_x}, {actual_y})")
            
            # 验证坐标范围
            if not (0 <= actual_x <= self.screen_width and 0 <= actual_y <= self.screen_height):
                return f"坐标 ({actual_x}, {actual_y}) 超出屏幕范围 (0-{self.screen_width}, 0-{self.screen_height})"
            
            # 移动并点击鼠标，确保clicks是整数类型
            pyautogui.click(actual_x, actual_y, button=button, clicks=int(clicks))
            
            button_text = "左键" if button == "left" else "右键"
            click_text = "单击" if clicks == 1 else "双击"
            return f"成功在坐标 ({actual_x}, {actual_y}) 处{button_text}{click_text} (比例坐标: {x:.3f}, {y:.3f})"
        except Exception as e:
            return f"点击失败: {str(e)}"

    def scroll_window(self, x, y, direction="up"):
        """
        滚动窗口工具：在指定坐标处滚动窗口
        :param x: 比例x坐标 (0-1之间的小数)
        :param y: 比例y坐标 (0-1之间的小数)
        :param direction: 滚动方向，"up"表示向上滚动，"down"表示向下滚动
        """
        try:
            # 固定滚动步数
            fixed_clicks = 1400
            
            # 将比例坐标转换为实际屏幕坐标
            actual_x = int(x * self.screen_width)
            actual_y = int(y * self.screen_height)
            
            print(f"滚动窗口 - 比例坐标转换: ({x:.3f}, {y:.3f}) -> ({actual_x}, {actual_y})")
            
            # 验证坐标范围
            if not (0 <= actual_x <= self.screen_width and 0 <= actual_y <= self.screen_height):
                return f"坐标 ({actual_x}, {actual_y}) 超出屏幕范围 (0-{self.screen_width}, 0-{self.screen_height})"
            
            # 根据方向确定实际滚动步数
            clicks = fixed_clicks if direction == "up" else -fixed_clicks
            
            # 移动到指定位置并滚动
            pyautogui.scroll(clicks, x=actual_x, y=actual_y)
            direction_text = "向上" if direction == "up" else "向下"
            return f"成功在坐标 ({actual_x}, {actual_y}) 处{direction_text}滚动 {fixed_clicks} 步 (比例坐标: {x:.3f}, {y:.3f})"
        except Exception as e:
            return f"滚动窗口失败: {str(e)}"
        
    def type_text(self, x, y, text):
        """
        增强的文本输入工具：先点击指定位置，再通过复制粘贴方式输入文本
        """
        try:
            import pyperclip
            
            # 1. 将比例坐标转换为实际屏幕坐标
            actual_x = int(x * self.screen_width)
            actual_y = int(y * self.screen_height)
            
            print(f"定位到坐标: ({actual_x}, {actual_y}) (比例坐标: {x:.3f}, {y:.3f})")
            
            # 2. 验证坐标范围
            if not (0 <= actual_x <= self.screen_width and 0 <= actual_y <= self.screen_height):
                return f"坐标 ({actual_x}, {actual_y}) 超出屏幕范围 (0-{self.screen_width}, 0-{self.screen_height})"
            
            # 3. 点击输入位置
            pyautogui.click(actual_x, actual_y)
            time.sleep(0.5)  # 等待点击生效
            
            # 4. 将文本复制到剪贴板
            pyperclip.copy(text)
            time.sleep(0.2)  # 等待复制完成
            
            # 5. 粘贴文本
            pyautogui.hotkey('ctrl', 'v')
            
            return f"成功在坐标 ({actual_x}, {actual_y}) 处输入文本: {text}"
        except ImportError:
            # 如果没有安装pyperclip，则回退到原来的方法
            return self._type_text_fallback(x, y, text)
        except Exception as e:
            return f"输入文本失败: {str(e)}"
    
    def _type_text_fallback(self, x, y, text):
        """
        回退的文本输入方法
        """
        try:
            # 1. 将比例坐标转换为实际屏幕坐标
            actual_x = int(x * self.screen_width)
            actual_y = int(y * self.screen_height)
            
            # 2. 验证坐标范围
            if not (0 <= actual_x <= self.screen_width and 0 <= actual_y <= self.screen_height):
                return f"坐标 ({actual_x}, {actual_y}) 超出屏幕范围 (0-{self.screen_width}, 0-{self.screen_height})"
            
            # 3. 点击输入位置
            pyautogui.click(actual_x, actual_y)
            time.sleep(0.5)  # 等待点击生效
            
            # 4. 输入文本（支持英文）
            pyautogui.write(text, interval=0.1)
            
            return f"成功在坐标 ({actual_x}, {actual_y}) 处输入文本: {text}"
        except Exception as e:
            return f"输入文本失败: {str(e)}"
        
    def parse_tool_calls(self, response_text):
        """
        解析工具调用指令
        """
        # 使用正则表达式查找工具调用
        tool_call_pattern = r'<\|tool_call\|>(.*?)<\|tool_call\|>'
        tool_calls = re.findall(tool_call_pattern, response_text, re.DOTALL)
        
        parsed_calls = []
        for call in tool_calls:
            call = call.strip()
            # 解析函数名和参数
            if '(' in call and ')' in call:
                func_name = call.split('(')[0].strip()
                args_str = call[len(func_name)+1:call.rfind(')')].strip()
                
                # 简单解析参数
                args = {}
                if args_str:
                    # 处理参数字符串，例如: x=100, y=200
                    for arg in args_str.split(','):
                        if '=' in arg:
                            key, value = arg.split('=', 1)
                            key = key.strip()
                            value = value.strip().strip('"').strip("'")
                            # 尝试转换为数字
                            try:
                                args[key] = float(value)
                            except ValueError:
                                args[key] = value
                
                parsed_calls.append({
                    "name": func_name,
                    "arguments": args
                })
        
        return parsed_calls

    def execute_tool_calls(self, tool_calls):
        """
        执行工具调用
        """
        results = []
        for call in tool_calls:
            func_name = call["name"]
            args = call["arguments"]
            
            if func_name in self.tools:
                try:
                    result = self.tools[func_name](**args)
                    results.append(f"工具 {func_name} 执行结果: {result}")
                except Exception as e:
                    results.append(f"执行工具 {func_name} 时出错: {str(e)}")
            else:
                results.append(f"未知工具: {func_name}")
        
        return "\n".join(results)

    def run_task(self, task_description, max_steps=50):
        """
        运行任务
        """
        print(f"开始执行任务: {task_description}")
        print(f"屏幕分辨率: {self.screen_width} x {self.screen_height}")
        
        # 添加系统提示词
        system_prompt = f"""
你是一个用户助理，同时拥有操控电脑的能力，你现在面对看到的图像是电脑的用户界面，请分析屏幕内容（屏幕大小是{self.screen_width}*{self.screen_height}），如果需要操作电脑，请按以下格式调用工具：

<|tool_call|>函数名(参数1=值1, 参数2=值2)<|tool_call|>

可用的工具包括：
1. mouse_click(x=比例x, y=比例y, button="left", clicks=1) - 在指定坐标点击鼠标
   - 坐标为比例（0-1之间的小数）
   - button参数可以是"left"（左键，默认）或"right"（右键）
   - clicks参数可以是1（单击，默认）或2（双击），必须是整数
   例如：mouse_click(x=0.5, y=0.5) 表示在屏幕中心点左键单击
   例如：mouse_click(x=0.3, y=0.4, button="right") 表示在坐标(0.3,0.4)处右键单击
   例如：mouse_click(x=0.6, y=0.7, clicks=2) 表示在坐标(0.6,0.7)处左键双击
   例如：mouse_click(x=0.8, y=0.9, button="right", clicks=2) 表示在坐标(0.8,0.9)处右键双击
2. type_text(x=比例x, y=比例y, text="要输入的文本") - 在指定坐标点击并输入文本，支持中英文输入
   例如：type_text(x=0.3, y=0.4, text="你好世界") 表示在坐标(0.3,0.4)处点击并输入"你好世界"
   例如：type_text(x=0.5, y=0.6, text="Hello World") 表示在坐标(0.5,0.6)处点击并输入"Hello World"
   请注意：输入文字请一次性输入一行即可，然后需要回车换行或者编辑再调用其他工具执行。不要出现“/n”工具无法识别这种换行指令
3. scroll_window(x=比例x, y=比例y, direction="up") - 在指定坐标处滚动窗口，direction参数可以是"up"或"down"，表示向上或向下滚动
   例如：scroll_window(x=0.5, y=0.5, direction="up") 表示在屏幕中心位置向上滚动
   例如：scroll_window(x=0.3, y=0.4, direction="down") 表示在坐标(0.3,0.4)处向下滚动
4. close_window(x=比例x, y=比例y) - 关闭指定坐标所在的窗口，先点击该窗口获取焦点再关闭
   例如：close_window(x=0.5, y=0.5) 表示点击屏幕中心的窗口并关闭它
5. press_windows_key() - 按下Windows键，用于打开开始菜单
   例如：press_windows_key() 表示按下Windows键
6. press_enter() - 按下回车键，可以用于换行或者确认
   例如：press_enter() 表示按下回车键
7. delete_text(x=比例x, y=比例y, count=1) - 删除指定输入框中的文本
   - 先点击输入框获取焦点，然后删除指定数量的字符
   - count参数是要删除的字符数量，默认为1（你在设置的时候请尽可能精确）
   例如：delete_text(x=0.4, y=0.5, count=5) 表示点击坐标(0.4,0.5)处的输入框并删除5个字符
   例如：delete_text(x=0.6, y=0.7) 表示点击坐标(0.6,0.7)处的输入框并删除1个字符
8. mouse_drag(start_x=起始比例x, start_y=起始比例y, end_x=结束比例x, end_y=结束比例y, duration=0.5) - 从起始坐标拖拽到结束坐标
   - 从起始点拖拽到结束点，duration参数为拖拽过程耗时（秒），默认为0.5秒
   例如：mouse_drag(start_x=0.2, start_y=0.3, end_x=0.8, end_y=0.3) 表示从屏幕水平位置20%、垂直位置30%的地方拖拽到水平位置80%、垂直位置30%的地方
   例如：mouse_drag(start_x=0.5, start_y=0.5, end_x=0.5, end_y=0.2, duration=1.0) 表示从屏幕中心向上拖拽，耗时1秒
9. wait(seconds=等待秒数) - 等待指定的时间（秒）
   - seconds参数为等待时间，可以是整数或小数
   例如：wait(seconds=3) 表示等待3秒
   例如：wait(seconds=0.5) 表示等待0.5秒（500毫秒）
   这个工具在需要等待某些操作完成或界面更新时非常有用
10. open_terminal(command="") - 打开一个新的终端窗口
    - command参数为可选，如果提供则在新终端中执行该命令
    例如：open_terminal() 表示打开一个新的空终端窗口
    例如：open_terminal(command="dir") 表示在新终端中执行dir命令（Windows）或ls命令（Unix/Linux/macOS）
    注意：终端默认指向的目录一般是软件所处目录，不一定是桌面，请你进入终端后自己判断所处位置
11. press_hotkey(x=比例x, y=比例y, hotkey="快捷键组合") - 在指定位置点击后模拟键盘快捷键
    - 先在指定坐标处点击获取焦点，然后执行快捷键操作
    - hotkey参数为快捷键组合，例如 "ctrl+c", "ctrl+v", "ctrl+a", "alt+f4" 等
    例如：press_hotkey(x=0.5, y=0.5, hotkey="ctrl+c") 表示在屏幕中心点击并执行复制操作
    例如：press_hotkey(x=0.3, y=0.4, hotkey="alt+f4") 表示在坐标(0.3,0.4)处点击并执行关闭窗口操作

请在每一步操作后给出简要说明，然后使用工具调用格式指定下一步操作。
如果你认为已经完成任务了，或者你需要用户提供更多信息，或者需要用户帮助你（比如有些输入需要用户输入，或者需要用户帮忙操作），你则不需要调用工具了，这样才可以获取到用户的输入
注意：坐标系统使用比例值，x和y的取值范围都是0到1之间的小数，其中(0,0)代表屏幕左上角，(1,1)代表屏幕右下角。
所有参数值必须是正确的数据类型，特别是clicks参数必须是整数（1或2），不能是浮点数。
如果不需要操作电脑，请你以友好的语言回复用户
请你注意，你是运行在终端中，所以无论如何，请不要关闭你存在对话的终端，你所在的终端会保持打开，请不要关闭它。一般的，你的终端上会存在历史聊天记录，或者= VLM 电脑操作工具 =字样
如果你在操作鼠标的时候，发现并没有实现预计的效果，可能是因为鼠标操作的坐标出现问题或者系统正在运行，若是鼠标操作的坐标出现问题，请你略微调整坐标值。如果是软件正在运行，请等待软件启动结束。
如果你认为用户的指令需要使用工具才能完成，请在任务的开始时，先计划好自己的操作步骤。
如果一项任务可以使用终端即可完成，请优先选择终端，如果一项操作可以只使用快捷键完成，请优先选择快捷键


        """.strip()
        
        self.messages = [
            {"role": "system", "content": system_prompt}
        ]
        
        step = 0
        while step < max_steps:
            step += 1
            print(f"\n--- 步骤 {step} ---")
            
            # 获取屏幕截图
            screenshot_buffer = self.capture_screenshot()
            base64_image = self.encode_image_to_base64(screenshot_buffer)
            
            # 构造消息
            if step == 1:
                content = [
                    {"type": "text", "text": f"请完成以下任务: {task_description}"},
                    {
                        "type": "image_url",
                        "image_url": {
                            "url": f"data:image/png;base64,{base64_image}"
                        }
                    }
                ]
            else:
                content = [
                    {"type": "text", "text": "这是当前屏幕状态，请继续完成任务"},
                    {
                        "type": "image_url",
                        "image_url": {
                            "url": f"data:image/png;base64,{base64_image}"
                        }
                    }
                ]
            
            self.messages.append({
                "role": "user",
                "content": content
            })
            
            # 调用模型
            try:
                response = self.client.chat.completions.create(
                    model=self.model_name,
                    messages=self.messages,
                    temperature=0.3,
                    max_tokens=1024
                )
                
                response_text = response.choices[0].message.content
                self.messages.append({
                    "role": "assistant",
                    "content": response_text
                })
                
                print("模型响应:")
                print(response_text)
                
                # 解析并执行工具调用
                tool_calls = self.parse_tool_calls(response_text)
                if tool_calls:
                    print("\n检测到工具调用:")
                    for call in tool_calls:
                        print(f"- {call['name']}({', '.join([f'{k}={v}' for k, v in call['arguments'].items()])})")
                    
                    tool_result = self.execute_tool_calls(tool_calls)
                    print(f"\n工具执行结果:")
                    print(tool_result)
                    
                    # 将工具执行结果添加到消息历史中
                    self.messages.append({
                        "role": "user",
                        "content": f"工具执行结果:\n{tool_result}"
                    })
                    
                    # 短暂等待，让操作生效
                    time.sleep(3)
                else:
                    print("未检测到工具调用，任务可能已完成")
                    break
                    
            except Exception as e:
                print(f"调用模型时发生错误: {e}")
                break
        
        print(f"\n任务执行完成，共执行 {step} 步")

def main():
    """
    交互式主函数
    """
    # 获取API密钥
    print("=== VLM 电脑操作工具 ===")
    print("欢迎使用qwen3VL电脑操作工具")
    print("您需要一个阿里云API密钥才能使用此工具")
    print("获取地址: https://www.aliyun.com/")
    api_key = input("请输入您的阿里云API密钥: ").strip()
    
    if not api_key or api_key == "sk-your-api-key":
        print("错误: 请输入有效的阿里云API密钥")
        print("请访问阿里云控制台获取API密钥")
        return
    
    # 初始化代理
    agent = VLMAgent(api_key)
    
    print("\n系统已就绪，您可以输入各种任务请求")
    print("示例任务:")
    print("  - 打开记事本并输入'Hello World'")
    print("  - 在浏览器中搜索'人工智能'")
    print("  - 创建一个名为'test.txt'的文件")
    print("输入'退出'、'exit'或'quit'结束程序")
    print("-" * 50)
    
    while True:
        # 获取用户输入
        task = input("\n请输入任务: ").strip()
        
        # 检查退出条件
        if task.lower() in ['退出', 'exit', 'quit', 'q']:
            print("程序结束，再见！")
            break
            
        # 检查空输入
        if not task:
            print("请输入有效的任务")
            continue
            
        # 执行任务
        print(f"\n开始执行任务: {task}")
        agent.run_task(task)
        print(f"\n任务 '{task}' 执行完成")

if __name__ == "__main__":
    # 检查必要的依赖
    try:
        import pyautogui
        import PIL
        import tkinter
    except ImportError as e:
        print(f"缺少必要的依赖包: {e}")
        print("请安装依赖: pip install pyautogui pillow openai")
        exit(1)
    
    main()
