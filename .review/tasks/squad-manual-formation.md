# 编队改为手动 + 感知球重叠修复

## 验收标准

**A. 编队改为手动**

- [ ] 【A1】选择（框选 / 点选 / F1 / 数字键）只做选择，不产生任何小队。
  > 原话：“应该是只选取操纵，默认是不编队的”
- [ ] 【A2】未编队时右键移动：选中单位各自移动并保持相对站位；右键攻击：每个选中单位各自锁定目标。两者都不自动建队。
- [ ] 【A3】当前选择恰好是某个小队的全部存活成员时：移动走 `Squad.MoveTo` 队形，攻击走 `Squad.Attack` 集火。
- [ ] 【A4】`G` 把当前选择编成新小队，编号由 `SquadRegistry.NextId()` 自增（1、2、3…）；`Ctrl+1-9` 指定编号；`1-9` 选队；面板「组建编队」与 `G` 同路径。
  > 原话：“编队要增加热键，并且要有自增的序号”
- [ ] 【A5】部分选中某小队成员并下个体命令后，小队行为树的自动接敌**不得**在一帧内改写该命令（`Squad.ManualControl` 暂停自动接敌），直到收到整队移动/攻击命令。
- [ ] 【A6】整队改编（全部成员移到新小队）或全员阵亡后，小队自动解散，面板不出现 0 人空壳。
- [ ] 【A7】原有 `EnsureSelectionSquad()`（命令时自动建队）已删除，无残留引用。

**B. 感知球重叠变深**

- [ ] 【B1】多个半透明感知球重叠时透明度不累加（同一像素只画一层）。
  > 原话：“观察范围的半透明球体重叠后颜色会变深，导致什么也看不见”
- [ ] 【B2】`Shader.Find("Vindicator/PerceptionVolume")` 失败时回退旧 URP Unlit 半透明路径，不报错、不消失。
- [ ] 【B3】单层仍可辨认：淡填充（0.10）+ 更亮的 Fresnel 边缘。
- [ ] 【B4】材质按阵营共享，不随单位数量增长。
- [ ] 【B5】shader 在编辑器内编译 0 error（`ShaderUtil.GetShaderMessages` 证据，不只是源码字符串检查）。

**C. 工程验证**

- [ ] 【C1】`python .agent/scripts/agent.py index` + `validate`：0 error。
- [ ] 【C2】Unity 编译验证 pass（编辑器证据 + `unity-verify --adopt-editor`）。
- [ ] 【C3】EditMode 测试 pass，新增语义测试会推进 `SquadBrain` 一帧确认命令未被改写。
- [ ] 【C4】新增 `.cs` / `.shader` 均有配对 `.meta`。
- [ ] 【C5】`Assets/Scenes/MVP.unity` 用户未提交改动（`groundMask.m_Bits = 51`）保留。

## 审核范围

- `Assets/Script/Unit/Player/PlayerCommandController.cs`
- `Assets/Script/Unit/Player/SquadPanel.cs`
- `Assets/Script/Unit/AI/Squad.cs`、`Assets/Script/Unit/AI/SquadBrain.cs`
- `Assets/Script/Unit/DeveloperModeController.cs`
- `Assets/Resources/Shaders/PerceptionVolume.shader`
- `Assets/Tests/EditMode/PlayerCommandTests.cs`、`SquadBrainTests.cs`、`DeveloperModeTests.cs`
- 文档：`YC/小队与编队设计_2026_11_08.md`、`YC/公民与废墟设计_2026_11_10.md`、`.agent/memory/architecture.md`

不审：`ProjectSettings/*`、`Assets/Scenes/MVP.unity`、与本计划无关的既有改动。

## 需求来源

- 用户会话原话（2026-11-15）：编队手动化 / `G` 热键 / 自增编号；感知球重叠变深
- `.agent/tasks/backlog.json` 的 `squad-manual-formation`、`perception-volume-overlap`
