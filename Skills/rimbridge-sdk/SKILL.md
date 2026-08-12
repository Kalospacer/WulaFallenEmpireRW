---
name: rimbridge-sdk
description: 如何通过 RimBridgeServer 进程内 SDK 操作 RimWorld——先用 bridge_list_tools 发现游戏工具（rimworld/*、rimbridge/*），再用 bridge_call 调用。当需要操作游戏（镜头/UI/点击/存档/截图/推进时间/调试动作）时，先读本 skill（read_skill）。
---

# 通过 RimBridgeServer 操作 RimWorld

Wula 是游戏进程内的 mod，操作游戏不用走外部 MCP/GABS，直接进程内调 RimBridgeServer 的 SDK。两个工具：

- `bridge_list_tools` —— 列出 RimBridgeServer 的全部游戏工具（id、别名、标题、摘要、分类、参数）。只读。
- `bridge_call` —— 真正调用，参数 `{ tool:"<id或别名>", arguments:{...}, timeout:<可选秒> }`。

## 标准流程

1. `bridge_list_tools` —— 拿到工具清单（~125 个 `rimworld/*`、`rimbridge/*`）。
2. 挑只读工具验证：`bridge_call { tool:"rimbridge/get_bridge_status" }`，或 `rimworld/get_ui_layout`、`rimworld/list_saves`。
3. 需要动手时，用改动类工具（下面常用清单），参数严格照 `bridge_list_tools` 返回的 Parameters 填。

## 常用工具

只读（优先、安全）：
- `rimbridge/get_bridge_status` —— 桥状态/版本/companion 诊断
- `rimworld/get_ui_layout` —— 当前 UI 布局（surface 结构）
- `rimworld/list_saves` —— 存档列表
- `rimworld/get_selection` / `rimworld/list_main_tabs` / `rimworld/list_inspect_tabs` —— 选中/主标签/检视标签

改动类（确认后再做）：
- `rimworld/click_cell`、`rimworld/right_click_cell`、`rimworld/drag_cell` —— 地图点击
- `rimworld/click_ui_target`、`rimworld/scroll_ui_target` —— UI 点击/滚动
- `rimworld/take_screenshot`、`rimworld/screenshot_cell_rect`、`rimworld/frame_cell_rect` —— 截图
- `rimworld/load_game`、`rimworld/load_game_ready`、`rimworld/start_debug_game_ready` —— 读档/开局
- `rimworld/step_game_ticks`、`rimworld/play_for`、`rimworld/play_until_letter` —— 推进游戏时间
- `rimworld/open_main_tab`、`rimworld/close_main_tab`、`rimworld/open_inspect_tab` —— 切换标签页
- `rimbridge/run_script`、`rimbridge/run_lua` —— 跑 JSON/Lua 自动化脚本

## 注意

- 工具名和参数都从 `bridge_list_tools` 拿，别猜。
- 参数是对象：`bridge_call { tool:"rimworld/click_cell", arguments:{ cell:"12,34" } }` 这类形状按清单填。
- 只读工具先跑通再碰改动类；点击/读档/推进时间会真实影响游戏，先向玩家说明意图。
- `bridge_list_tools` 返回「未检测到 RimBridgeServer」时，说明玩家没启用 RimBridgeServer 这个 mod，告诉玩家去启用，而不是重试。
