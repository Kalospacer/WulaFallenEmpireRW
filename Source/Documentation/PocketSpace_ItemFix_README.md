# 武装穿梭机口袋空间物品消失问题修复

## 问题描述
装载到穿梭机口袋空间中的物品会神秘消失，玩家无法在穿梭机的容器界面中看到这些物品。

## 问题原因分析
1. **双重存储系统混乱**：代码中同时存在两个容器：
   - `innerContainer`（自定义容器）
   - `CompTransporter.innerContainer`（穿梭机标准容器）
   
2. **错误的容器优先级**：物品被存储到自定义的 `innerContainer` 中，但游戏界面和穿梭机系统期望物品在 `CompTransporter.innerContainer` 中。

3. **缺乏自动同步机制**：口袋空间中的物品没有定期同步到穿梭机的主容器中。

## 解决方案

### 设计理念：简单即是美
经过重新设计，我们**移除了备用容器的复杂性**，采用更简洁的单一容器策略：

**核心原则**：
- ✅ **唯一权威容器**：只使用穿梭机的 `CompTransporter.innerContainer`
- ✅ **简化存储逻辑**：物品直接存储到游戏界面可见的容器
- ✅ **透明的失败处理**：容器满了就放到地面，玩家可以看到并手动处理
- ✅ **向后兼容性**：保留 `innerContainer` 仅用于向后兼容，避免游戏崩溃

### 1. 修复物品转移逻辑
修改了 `TransferAllFromPocketToMainMap()` 方法：
- **直接使用主容器**：物品直接存储到 `CompTransporter.innerContainer`
- **透明的容量处理**：容器满了就放到地面，并显示提示消息
- **清晰的反馈**：玩家可以看到哪些物品因为容器满了被放到了地面

```csharp
// 简化后的逻辑
if (!transporter.innerContainer.TryAdd(item))
{
    // 容器满了，放到地面让玩家看到
    GenPlace.TryPlaceThing(item, dropPos, this.Map, ThingPlaceMode.Near);
    Messages.Message($"容器已满：{item.LabelShort} 被放置在穿梭机附近", MessageTypeDefOf.CautionInput);
}
```

### 2. 修复 IThingHolder 接口实现
修改了 `GetDirectlyHeldThings()` 方法：
- **优先返回主容器**：游戏界面会正确显示穿梭机主容器中的物品
- **智能容器选择**：自动选择有效的容器

```csharp
public ThingOwner GetDirectlyHeldThings()
{
    // 优先返回穿梭机的主容器
    CompTransporter transporter = this.GetComp<CompTransporter>();
    if (transporter != null && transporter.innerContainer != null)
    {
        return transporter.innerContainer;
    }
    // 备用容器
    return innerContainer;
}
```

### 3. 添加物品同步功能
新增了 `SyncPocketItemsToMainContainer()` 方法：
- **手动同步**：玩家可以通过按钮手动同步物品
- **自动同步**：每5分钟自动检查并同步物品
- **智能检测**：只同步不在主容器中的物品

### 4. 增强用户界面
- **新增同步按钮**：玩家可以手动触发物品同步
- **详细状态显示**：在穿梭机信息面板中显示物品分布情况
- **调试信息**：开发模式下显示详细的调试信息

### 5. 自动监控机制
修改了 `Tick()` 方法：
- **定期检查**：每5分钟自动检查口袋空间中的物品
- **预防性同步**：发现物品时自动同步到主容器
- **异常处理**：完善的错误处理机制

## 使用说明

### 1. 现有问题修复
如果您已经遇到物品消失问题：
1. 选中武装穿梭机
2. 点击新增的"同步物品"按钮
3. 系统会将口袋空间中的物品同步到主容器

### 2. 预防措施
- 系统现在会每5分钟自动同步一次
- 物品会优先存储到穿梭机的标准容器中
- 在开发模式下，您可以看到详细的物品分布信息

### 3. 调试功能
在开发模式下：
- 穿梭机信息面板会显示详细的容器状态
- 自动同步时会在日志中输出详细信息
- 可以追踪物品的存储位置

## 技术细节

### 容器优先级
1. **主容器**：`CompTransporter.innerContainer` - 游戏界面可见
2. **备用容器**：`innerContainer` - 用于溢出存储
3. **地面存储**：当所有容器都满时，物品会被放置在穿梭机附近

### 同步时机
- **销毁时**：穿梭机被销毁时自动转移所有物品
- **定期同步**：每5分钟检查一次（18000 ticks）
- **手动同步**：玩家点击同步按钮时
- **进入时**：从口袋空间返回时

## 兼容性
- ✅ 兼容现有存档
- ✅ 不影响其他模组
- ✅ 保持原有功能完整性
- ✅ 支持多语言界面

## 验证方法
1. 将物品放入口袋空间
2. 检查穿梭机的"内容"标签页
3. 物品应该正确显示在列表中
4. 穿梭机起飞时物品应该随行

修复后，您的物品不会再神秘消失，并且可以正常通过穿梭机界面管理。