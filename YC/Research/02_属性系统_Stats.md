# 属性系统（Stats / Attributes）调研

## 1. 推荐架构

```
ScriptableObject 定义  →  运行时容器（每单位独立）  →  修正器列表  →  固定管线计算  →  变更事件
```

1. **定义与值分离**
   - SO 只存：ID、类型、基础值、上下限、分类、显示名、图标
   - 类型安全：用枚举/强类型 ID，不用字符串键
   - 多单位共享同一份定义，各自持有独立运行时值
2. **每单位一个运行时属性容器**（`StatBlock` / `CharacterStats`）
   - 持有当前值 + 生效中的修正器
   - 负责聚合、重算、触发变更事件
   - 区分「基础数值」与「资源型数值」（Health/Mana：有 min/max 边界）
3. **修正器显式建模**
   - 类型：加固定值 / 加百分比 / 乘百分比 / 覆盖
   - 按来源打标签（装备、增益、减益、升级），便于整体移除
   - 支持优先级、叠加规则、持续时间、刷新策略
4. **固定计算管线（顺序决定结果）**
   ```
   base → +flat → +additive% → ×multiplicative% → override → clamp
   ```
   与施加顺序无关，结果可复现
5. **事件驱动 + 惰性重算**
   - 值变化才广播 `OnStatChanged`
   - 脏标记 + 读取时才重算，避免每帧遍历修正器
   - 派生属性（如 MaxHealth 由体质推导）登记依赖，注册时拒绝循环依赖
6. **测试与调试**
   - 计算核心不依赖 Unity，可纯 C# 单测
   - clamp 阶段处理 NaN
   - 提供 breakdown（每阶段贡献明细）

## 2. 参考实现

| 项目 | 特点 | 链接 |
|---|---|---|
| yudinikita/stat-master | `ModifiableValue<T>` / `BoundedValue<T>`；静态修正 `Modifier.Plus`；派生属性用 `Select`/`Zip`；优先级排序 | <https://github.com/yudinikita/stat-master> |
| ashblue/fluid-stats | 支持加减乘除修正的动态属性 | <https://github.com/ashblue/fluid-stats> |
| SeawispHunter.RolePlay.Attributes | `ModifiableValue`/`Modifier`、LINQ 风格投影、`LerpOnChange` 数值动画 | <https://github.com/shanecelis/SeawispHunter.RolePlay.Attributes> |
| Smart Stats System | 固定管线 base→flat→add%→mul%→override→clamp；`GetBreakdown()`；纯 C# 核心 | <https://realcut-studio.github.io/product.html?id=smart-stats> |
| RPG Stat & Attribute System（商店） | 数据驱动修正器 | <https://assetstore.unity.com/packages/tools/game-toolkits/rpg-stat-attribute-system-data-driven-modifiers-358802> |

## 3. 常见坑

- 直接改共享 SO 的数值 → 进入 Play Mode 后修改会写回资产
- 每帧遍历所有修正器 → 用脏标记/缓存
- 计算顺序不固定 → 同一套 buff 在不同施加顺序下结果不同
- 用字符串作属性键 → 拼写错误难发现、重构困难
- 修正器没有来源标签 → 无法整体移除某件装备/某个 buff

## 对本项目的取舍

当前数值规模很小（血量/攻击/移速/攻速/范围/溅射半径），采用精简方案：

- `StatType` 枚举作为键（类型安全、可扩展）
- `StatBlock`：基础值字典 + 修正器列表 + 脏标记 + 变更事件
- 修正器支持 `Flat / AddPercent / Multiply / Override`，带来源标记
- Health 作为资源型属性，单独提供 `Current/Max` 与死亡事件
- 暂不实现派生属性与循环依赖检测（需求未涉及，避免过度设计）
