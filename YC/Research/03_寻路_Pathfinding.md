# 寻路（Pathfinding）调研

## 1. 方案对比

| 方案 | 适用场景 | 成本 | 本项目适配 |
|---|---|---|---|
| 路点 / 样条（Waypoints / Spline） | **固定路线**（经典塔防） | 最低，无需寻路 | ✅ 采用 |
| NavMeshAgent | 开放区域、敌人自由游走、障碍动态变化 | 中，需烘焙 | ❌ 路线不可控，斜坡烘焙成本高 |
| 网格 A* | 玩家自建迷宫、可破坏地形 | 最高，变化时重算 | ❌ 超出需求 |
| Flow Field | 大量单位共享同一目标（塔防成波敌人） | 一次 Dijkstra，O(1)/单位 | 规模变大后再考虑 |

来源普遍结论：**固定路线塔防不要用 NavMesh**，这是最常见的初学者错误；waypoints 更简单、更便宜、更易平衡。

## 2. 路点实现要点

1. 路线用有序 Transform 列表（`Waypoints` 父对象下按顺序排列）
2. 每帧朝当前路点移动：`dir.normalized * speed * Time.deltaTime`
3. **到达阈值略大于单帧最大位移**（如 0.2m），否则高速单位会越过路点来回抖动
4. **多条路线时每个敌人持有自己的路线引用**，不要共享静态数组
5. 折线路线可用样条平滑（Unity Splines / 三次贝塞尔）；注意样条本身不包含环境安全信息，平滑后可能贴近墙体
6. 多单位并排：给同批单位沿路线方向的横向偏移 / 编队
7. 分叉：在决策路点携带分支信息，让敌人选择后续路线

## 3. 斜坡 / 地形贴合

- 路点只提供 XZ 平面路径时，Y 用**向下射线**贴地（`Physics.Raycast` 命中地面/斜坡/道路层）
- 斜坡上导航需限制坡度（`NavMesh` 有 slope 参数；自实现时可用射线法线夹角判断）
- 本项目白盒：`GroundBase`、`Ramp_01`（高 3.5m）、`Ramp_01 (1)` 都有 MeshCollider，可直接射线

## 4. 目标搜索与仇恨

- 小规模可用 `FindGameObjectsWithTag`，**规模化必须改为注册表**：生成时注册、死亡时注销，从共享列表选择
- 选择规则：最近 / 最先到达终点 / 最强 / 最靠近基地
- 塔防常用「沿路径最靠前」作为默认目标
- 目标死亡后需要重新选择目标（事件驱动，而非每帧轮询）

## 5. 性能与常见错误

- 大量同类敌人追同一目标 → 考虑 flow field
- 每帧全局搜索敌人 → 热点
- 频繁 Instantiate/Destroy → 用 `UnityEngine.Pool.ObjectPool<T>`，取用时重置状态
- 路线在运行时被修改 → 需重建路线并重映射所有敌人的路点索引（固定路线 TD 一般不需要）

## 来源

- [Unity Tower Defense Tutorial (Unity 6) — 系统架构与 waypoint 实现](https://generalistprogrammer.com/tutorials/unity-tower-defense-tutorial)
- [A*算法、导航网格、路径点寻路对比](https://www.e-com-net.com/article/1176666077212520448.htm)
- [Stack Overflow: 2D pathfinding in Unity tower defense](https://stackoverflow.com/questions/35988584/2d-pathfinding-in-unity-tower-defense)
- [Stack Overflow: enemies follow waypoints with two direction options](https://stackoverflow.com/posts/77333143/revisions)
- [gamedev-pathing skill notes](https://github.com/ericrisco/rsc-harness/blob/a4046c1b5123e030a333e26defc186e287652dfa/skills/gamedev-pathing/SKILL.md)
- [TowerDefencer Issue #10: NavMesh Setup and Enemy Pathfinding](https://github.com/cristi71000/TowerDefencer/issues/10)

## 对本项目的取舍

- 采用**场景路点路线 + 三次/线性插值跟随 + 向下射线贴地**
- 敌人沿路线前进；目标（塔/Home）在路线旁时，按目标位置在路线上的投影点停靠并进入攻击距离
- 目标死亡事件触发重新选目标；转目标后继续沿路线前进
- 保留后续接入 `TDRoadNetwork` 图寻路（BFS）的接口，但本次验收不需要
