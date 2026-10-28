# 经验与踩坑

> 只记录可复用的经验。每条注明适用条件与来源；代码修复细节以源码为准。

## 单位与移动

1. **SphereCast 起点不能与斜坡重叠**
   条件：用球体向下探测贴地。现象：球心离脚底太近、斜坡从斜下方穿过球体时，Unity 会忽略该碰撞体，探测落到下层路面，单位“贴不住坡/从坡下穿过”。当前做法：从脚底上方 2m 起投，并用 `[脚底 - snapDistance, 脚底 + stepHeight]` 高度带过滤。
   来源：[YC/Unit实现记录_2026_10_28.md](../../YC/Unit实现记录_2026_10_28.md)。

2. **路线必须从斜坡低端正面进入**
   条件：斜穿斜坡侧面会形成约 2m 垂直落差，`UnitMover` 不会移动。旧 22 点路线在 x≈-5.4 处有此问题。
   来源：同上。

3. **停步余量只有 4cm**
   条件：敌人停在塔包围盒 2.06m 处，有效射程 2.1m。任何塔碰撞体/射程/停步阈值调整都可能让“能打到”变成“够不到”，必须重跑验收。
   来源：同上。

4. **暂停移动会冻结朝向**
   条件：`EnemyBrain` 进入射程后 `SetPaused(true)`，`UnitMover.Update` 提前返回。表现：攻击时单位不面向目标。修法方向见 backlog `unit-face-target`。
   来源：同上。

5. **目标切换只在目标死亡/丢失后发生**
   条件：当前没有动态重规划；转火只改变“何时停下”，不改变路线。若需要沿路网重规划，见 backlog `road-network-pathfinding`。
   来源：同上。

## 数据与架构

6. **绝不在运行时修改 ScriptableObject 资产**
   条件：Play Mode 下对 SO 的修改会持久化到资产。运行时数值应写入 `StatBlock` 等运行时容器。
   来源：[YC/Research/README.md](../../YC/Research/README.md) 结论 1。

7. **不要每帧 `FindGameObjectsWithTag`**
   条件：单位数量增长时。项目用 `UnitRegistry` 注册/注销；规模上千再考虑网格/四叉树。
   来源：同上结论 5；[architecture.md](architecture.md)。

8. **固定路线 TD 优先路点/样条，不用 NavMesh**
   条件：路线固定、无动态障碍。NavMesh 适合开放区域。该结论已用于当前实现；改变前提时重新评估。
   来源：[YC/Research/README.md](../../YC/Research/README.md) 结论 4。

## Unity 观测

9. **编辑器失焦会暂停游戏循环**
   条件：后台/脚本观察 PlayMode。先设置 `Application.runInBackground = true`；首帧读取可能早于 `Awake`，需等初始化完成。否则看起来像“单位不动/属性为 0”。
   来源：[YC/ToDo_2026_10_28.md](../../YC/ToDo_2026_10_28.md) 踩坑记录；项目工具级记忆。

## Harness 与工具

10. **“无引用资产”不等于可删除**
    条件：静态 GUID 扫描。动态加载、反射、运行时生成引用、编辑器 AddComponent 都不会留下 YAML GUID。索引只给低置信候选，删除前人工确认。
    来源：harness 设计；`.agent/indexes/README.md`。

11. **Harness 生成物不提交，元数据提交**
    条件：多人共享仓库。`.agent/indexes/*.json` 随场景频繁变化，提交会造成 churn/冲突；索引可从工作树重建。`.agent/` 的配置、任务、记忆、事件保持跟踪。
    来源：[decisions/0001-harness-location-and-scope.md](decisions/0001-harness-location-and-scope.md)。

12. **项目 Python 是 3.9**
    条件：编写 harness 脚本。避免 3.10+ API（如 `Path.write_text(newline=...)`）；使用 `open(..., newline="\n")` 或兼容写法。
    来源：bootstrap 实测（`python --version` = 3.9.21）。

13. **Git 状态要关闭路径转义**
    条件：中文文件名/目录。`git -c core.quotepath=false status --porcelain` 才能显示可读路径；harness 已内置。
    来源：harness 实现。

14. **`CompilationPipeline.RequestScriptCompilation()` 对未变程序集是空操作**
    条件：编辑器内做“强制编译验证”。无参调用在代码未变时约 67ms 返回，`assemblies_compiled` 为空，看起来像验证通过，其实没编译。
    做法：用 `RequestScriptCompilation(RequestScriptCompilationOptions.CleanBuildCache)`；本次实测 108.25s，Editor.log 出现 `Csc ... Assembly-CSharp.dll` 与 “Reloading assemblies after forced synchronous recompile.”。
    来源：harness-unity-verify 实测（2026-10-28）。

15. **编译事件里的 `assemblies_compiled` / `not_required` 不能当唯一证据**
    条件：解析 Unity 编译结果。强制重编译时 `assemblyCompilationNotRequired` 仍可能列出全部程序集，`assemblies_compiled` 为空，但编译确实发生了。
    做法：以 `EditorUtility.scriptCompilationFailed`、`CompilerMessage[]` 和 `Editor.log` 的 `Csc`/`Assembly-CSharp` 行交叉验证。
    来源：同上。

16. **Windows 上不要用 `os.kill(pid, 0)` 探活**
    条件：检测 Unity 编辑器是否占锁。Windows 下 `os.kill` 使用 TerminateProcess，会直接杀掉目标进程。
    做法：`tasklist /FI "PID eq N" /NH /FO CSV`，检查输出是否含 `"N"`。在 Git Bash 中直接写 `//FI` 会被 MSYS 改写，应从 Python `subprocess.run([...])` 调用。
    来源：harness-unity-verify 实测。

17. **asmdef 测试程序集不能引用 `Assembly-CSharp`**
    条件：项目运行时代码没有 asmdef 时写 EditMode 测试。Unity 只允许预定义程序集自动引用 asmdef 程序集，反向不成立；测试程序集里 `using` 项目类型会编译失败。
    做法：要么测试只用 `AssetDatabase`/`UnityEngine` 做冒烟检查（当前做法），要么先把运行时代码拆进自己的 asmdef（改动面大，需单独任务）。
    来源：harness-editmode-tests 实现。

18. **`TestRunnerApi` 的结果在 `RunFinished` 回调里取，别用轮询**
    条件：脚本化触发 EditMode 测试。`Execute` 立即返回；测试异步跑完后 `RunFinished(ITestResultAdaptor)` 才带完整结果。根节点有 `Children`，叶子节点才是具体用例。
    做法：`ScriptableObject.CreateInstance<TestRunnerApi>()` + `RegisterCallbacks`；在 `RunFinished` 递归统计叶子；结束后 `UnregisterCallbacks`。
    来源：harness-editmode-tests 实测（5/5，2.89s）。
