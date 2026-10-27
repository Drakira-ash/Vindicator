# 能力系统（Ability）调研

## 1. EX-GAS（Unreal GAS 的 Unity 移植）

- 仓库：<https://github.com/Smilelifer/gameplay-ability-system-for-unity>
- 概念模型：**WHO DO WHAT**
  - Who = `AbilitySystemComponent`（ASC，运行时单位）
  - Do = `Ability`（动作/技能）
  - What = `GameplayEffect`（属性变化）
- 核心类：
  - `AbilitySystemComponent`：持有 gameplay effects / tags / abilities / attribute sets；负责授权、激活、结束、取消、冷却查询
  - `AbilityAsset`（SO 配置）→ `AbstractAbility`（运行时数据）→ `AbilitySpec`（运行时实例与逻辑）
  - `AttributeSet` / `AttributeValue`（基础值 + 当前值 + 变更前后回调）
  - `GameplayEffectAsset` → `GameplayEffect` → `GameplayEffectSpec`；时长策略 Instant / Duration / Infinite；支持修正器、标签、叠加、周期执行
  - `GameplayTag` 层级标签（如 `State.Debuff.Stun`）用于激活门控
- 作者工作流：策划配 `AbilityAsset`，程序实现 `AbilitySpec` 逻辑；标签/属性改动后需重新生成代码库
- 已知限制（官方 README 自述）：未充分测试、可能含大量 bug 与性能问题；**依赖付费 Odin Inspector**；TimelineAbility / AbilityTask 为 WIP

## 2. MaiKuraki 的 Unity Gameplay Ability System 示例

- 仓库：<https://github.com/MaiKuraki/UnityGameplayAbilitySystemSample>
- 架构文档：<https://deepwiki.com/MaiKuraki/UnityGameplayAbilitySystemSample/1.2-high-level-architecture>
- 模式要点：
  - SO 只做定义，提供工厂方法 `CreateAbility()` / `CreateGameplayEffect()` 生成运行时 spec
  - 两阶段初始化：构造函数传具体参数，`Initialize()` 处理基类配置
  - 能力实例持有可变状态（等级、激活次数、冷却、激活标志）
  - 消耗与冷却用 GameplayEffect 表达；`CanActivate()` 检查标签/消耗/冷却
  - 事件通道（SO Event Channel）+ `ScriptableVariable<T>` + `RuntimeSet<T>` 解耦，替代 `FindObjectsOfType`

## 3. 通用最佳实践

1. **SO = 定义，运行时 = 实例**；不要在 SO 上存可变状态
2. **组件组合优于继承**：`Health`、`Mover`、`AttackAbility`、`Targeting` 各自独立
3. **集中调度 + 分散实现**：单位上一个 `AbilityContainer`/调度器管理顺序与冷却，具体行为由各能力组件实现
4. **标签/状态门控**：眩晕、沉默、冷却等用状态标记控制 `CanActivate`
5. **对象池与零 GC**：高频能力实例走池化；热路径避免分配
6. **可观测性**：能力激活/结束/取消事件埋点，便于调试

## 对本项目的借鉴

- 不引入完整 GAS：依赖重、体量大，当前只需「攻击 + 特殊能力」
- 采用精简版：
  - `UnitDefinition`（SO）→ 运行时 `Unit`（属性实例）
  - `UnitAbility` 抽象基类（MonoBehaviour，实现 `CanActivate` / `Execute` / `Tick`）
  - `AttackAbility` 作为首个实现（冷却 + 范围/溅射）
  - 冷却与消耗放在能力组件自身，由 `Unit` 每帧驱动

## 来源

- [EX-GAS for Unity (GitHub)](https://github.com/Smilelifer/gameplay-ability-system-for-unity)
- [MaiKuraki/UnityGameplayAbilitySystemSample - ScriptableObject Workflow](https://deepwiki.com/MaiKuraki/UnityGameplayAbilitySystemSample/9.1-scriptableobject-workflow)
- [MaiKuraki/UnityGameplayAbilitySystemSample - High-Level Architecture](https://deepwiki.com/MaiKuraki/UnityGameplayAbilitySystemSample/1.2-high-level-architecture)
- [MaiKuraki/UnityStarter - AbilitySystemComponent and Core Architecture](https://deepwiki.com/MaiKuraki/UnityStarter/6.1-abilitysystemcomponent-and-core-architecture)
- [Data-Driven Ability and Combat Systems](https://beefed.ai/en/data-driven-ability-combat-system)
