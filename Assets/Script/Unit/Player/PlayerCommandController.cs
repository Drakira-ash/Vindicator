using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 玩家 RTS 操作：左键框选友军、Shift 追加、右键移动/集火、数字键选择与编队。
/// 选择与命令逻辑与输入分离（SelectUnits / IssueMoveCommand / IssueAttackCommand），
/// EditMode 可直接驱动；控制器在运行期自动创建，不修改场景资产。
/// </summary>
[DisallowMultipleComponent]
public class PlayerCommandController : MonoBehaviour
{
    private const float MinBoxPixels = 12f;
    private const float GroupSnapRadius = 2f;

    private static PlayerCommandController instance;

    [SerializeField, Min(1f)] private float rayDistance = 500f;

    // 框选排查用：在 Console 打印拖拽与选中结果，便于确认输入是否到达。
    [SerializeField] private bool logSelection = true;

    private Camera cam;
    private Vector2 dragStart;
    private bool dragging;
    private bool additiveDrag;
    private Rect selectionRect;

    /// <summary>当前控制器（运行期唯一）。</summary>
    public static PlayerCommandController Instance => instance;

    /// <summary>是否正在拖拽选择框。</summary>
    public bool IsDragging => dragging;

    /// <summary>当前选择框（屏幕坐标，原点左下）。</summary>
    public Rect SelectionRect => selectionRect;

    /// <summary>
    /// 旧的选择输入开关。UI_Test 里由 <c>SelectionCommandBridge</c> 关闭，
    /// 交给新 UI 的 SelectionInputReader 处理框选/点选；右键命令与热键保留。
    /// </summary>
    public bool LegacySelectionEnabled { get; set; } = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindFirstObjectByType<PlayerCommandController>() != null)
        {
            return;
        }

        var go = new GameObject("PlayerCommands");
        go.AddComponent<PlayerCommandController>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            DestroyNow(gameObject);
            return;
        }

        instance = this;
        cam = Camera.main;
        DeveloperMode.Changed += OnDeveloperModeChanged;

        if (Application.isPlaying)
        {
            DontDestroyOnLoad(gameObject);
        }
    }

    private void OnDestroy()
    {
        DeveloperMode.Changed -= OnDeveloperModeChanged;

        if (instance == this)
        {
            instance = null;
        }
    }

    /// <summary>
    /// 关闭开发者模式时把“仅因编辑而选中的敌人”移出选择：
    /// 否则敌人会继续带着选择框，但 K 和右键命令都已不可用。
    /// </summary>
    private static void OnDeveloperModeChanged(bool enabled)
    {
        if (enabled)
        {
            return;
        }

        var selected = UnitSelection.All;
        var keep = new List<Unit>(selected.Count);
        bool dropped = false;

        for (int i = 0; i < selected.Count; i++)
        {
            var unit = selected[i];
            if (IsCommandable(unit))
            {
                keep.Add(unit);
            }
            else
            {
                dropped = true;
            }
        }

        if (dropped)
        {
            UnitSelection.Set(keep);
        }
    }

    private void Update()
    {
        if (cam == null)
        {
            cam = Camera.main;
        }

#if ENABLE_LEGACY_INPUT_MANAGER
        if (LegacySelectionEnabled)
        {
            HandleSelectionInput();
        }

        HandleCommandInput();
        HandleHotkeys();
#endif
    }

    /// <summary>
    /// 替换/追加选择；普通模式下只接受存活的可指挥友军，
    /// 开发者模式下任意存活单位（含敌人）都可选中以便编辑。
    /// </summary>
    public void SelectUnits(IEnumerable<Unit> units, bool additive = false)
    {
        var filtered = new List<Unit>();
        if (units != null)
        {
            foreach (var unit in units)
            {
                if (IsSelectable(unit))
                {
                    filtered.Add(unit);
                }
            }
        }

        if (additive)
        {
            UnitSelection.Add(filtered);
        }
        else
        {
            UnitSelection.Set(filtered);
        }
    }

    /// <summary>
    /// 右键移动。选择恰好是某个小队的全部成员时按该小队队形整队移动；
    /// 否则只给选中单位下个体移动命令（保持相对位置），不会自动编队。
    /// </summary>
    public bool IssueMoveCommand(Vector3 point, Vector3 forward)
    {
        UnitSelection.Prune();
        var units = AliveCommandableSelection();
        if (units.Count == 0)
        {
            return false;
        }

        var squad = SelectionSquad();
        if (squad != null)
        {
            squad.MoveTo(point, FormationLayout.Flatten(forward));
            MoveDestinationFlag.Show(point, UnitTeam.Player);
            Log(string.Format("[Order] {0} 按 {1} 队形移动到 {2}", squad.SquadName, squad.Formation, point));
            return true;
        }

        DropSquadOrders(units);
        bool issued = IssueGroupMove(units, point);
        if (issued)
        {
            MoveDestinationFlag.Show(point, UnitTeam.Player);
        }

        return issued;
    }

    /// <summary>循环切换当前整队阵型：横队 → 纵队 → 方阵 → 横队。</summary>
    public bool CycleSelectedSquadFormation()
    {
        var squad = SelectionSquad();
        if (squad == null)
        {
            Log("[Formation] 需要完整选中一个小队");
            return false;
        }

        FormationType next = squad.Formation == FormationType.Line
            ? FormationType.Column
            : (squad.Formation == FormationType.Column ? FormationType.Grid : FormationType.Line);
        squad.SetFormation(next);
        Log(string.Format("[Formation] {0} -> {1}", squad.SquadName, next));
        return true;
    }

    /// <summary>切换当前整队姿态：驻守 ↔ 戒备。</summary>
    public bool ToggleSelectedSquadPosture()
    {
        var squad = SelectionSquad();
        if (squad == null)
        {
            Log("[Posture] 需要完整选中一个小队");
            return false;
        }

        SquadPosture next = squad.Posture == SquadPosture.Garrison
            ? SquadPosture.Alert
            : SquadPosture.Garrison;
        squad.SetPosture(next);
        Log(string.Format("[Posture] {0} -> {1}", squad.SquadName, next));
        return true;
    }

    /// <summary>
    /// 右键攻击。选择恰好是某个小队的全部成员时由小队集火；
    /// 否则选中单位各自攻击该目标，不会自动编队。
    /// </summary>
    public bool IssueAttackCommand(UnitHealth target)
    {
        UnitSelection.Prune();
        if (target == null || !target.IsAlive)
        {
            return false;
        }

        var units = AliveCommandableSelection();
        if (units.Count == 0)
        {
            return false;
        }

        var squad = SelectionSquad();
        if (squad != null)
        {
            squad.Attack(target);
            Log(string.Format("[Order] {0} 集火 {1}", squad.SquadName, target.name));
            return true;
        }

        DropSquadOrders(units);
        for (int i = 0; i < units.Count; i++)
        {
            UnitCommander.Ensure(units[i]).IssueAttack(target);
        }

        Log(string.Format("[Order] {0} 名单位攻击 {1}（未编队）", units.Count, target.name));
        return true;
    }

    /// <summary>
    /// 只有当前选择恰好覆盖某个小队的全部存活成员时才返回该小队；
    /// 混编、跨队或只选中部分成员都返回 null（按个体命令处理）。
    /// </summary>
    public Squad SelectionSquad()
    {
        var units = AliveCommandableSelection();
        if (units.Count == 0)
        {
            return null;
        }

        Squad common = null;
        for (int i = 0; i < units.Count; i++)
        {
            var commander = units[i].GetComponent<UnitCommander>();
            if (commander == null || commander.SquadId < 0)
            {
                return null;
            }

            var squad = SquadRegistry.Find(commander.SquadId);
            if (squad == null || (common != null && common != squad))
            {
                return null;
            }

            common = squad;
        }

        // 只比较存活成员：阵亡成员要等 Squad.Tick 才剔除，
        // 若把尸体也算进去，死亡当帧框选幸存者会被误判成“没选全”。
        return common != null && AliveMemberCount(common) == units.Count ? common : null;
    }

    private static int AliveMemberCount(Squad squad)
    {
        int count = 0;
        var members = squad.MemberUnits();
        for (int i = 0; i < members.Count; i++)
        {
            var member = members[i];
            if (member != null && member.Health != null && member.Health.IsAlive)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>把当前选择编成一个新小队，编号由注册表自增（1、2、3…）。</summary>
    public bool CreateSquadFromSelection()
    {
        return CreateSquadFromSelection(0);
    }

    /// <summary>把当前选择编为指定编号的小队；id ≤ 0 表示自动分配下一个编号。</summary>
    public bool CreateSquadFromSelection(int squadId)
    {
        UnitSelection.Prune();
        var units = AliveCommandableSelection();
        if (units.Count == 0)
        {
            Log("[Squad] 没有可编队的单位（先框选士兵）");
            return false;
        }

        var squad = squadId > 0 ? SquadRegistry.Create(units, squadId) : SquadRegistry.Create(units);
        UnitSelection.Set(units);
        Log(string.Format("[Squad] 已编为 {0}（编号 {1}，{2} 人）", squad.SquadName, squad.SquadId, units.Count));
        return true;
    }

    /// <summary>选择指定编号的小队。</summary>
    public bool SelectSquad(int squadId)
    {
        var squad = SquadRegistry.Find(squadId);
        if (squad == null)
        {
            return false;
        }

        UnitSelection.Set(squad.MemberUnits());
        return true;
    }

    /// <summary>把当前选择编为指定编号的小队（已有同编号小队会被替换）。</summary>
    public bool AssignSquad(int squadId)
    {
        return squadId > 0 && CreateSquadFromSelection(squadId);
    }

    /// <summary>按当前状态给选中单位下发“保持相对位置”的群体移动（不建小队）。</summary>
    private bool IssueGroupMove(List<Unit> units, Vector3 point)
    {
        Vector3 centroid = Vector3.zero;
        for (int i = 0; i < units.Count; i++)
        {
            centroid += units[i].transform.position;
        }

        centroid /= units.Count;
        centroid.y = 0f;

        for (int i = 0; i < units.Count; i++)
        {
            Vector3 position = units[i].transform.position;
            Vector3 destination = point + new Vector3(position.x - centroid.x, 0f, position.z - centroid.z);
            destination.y = point.y;

            if (FormationLayout.TrySnapToNavMesh(destination, GroupSnapRadius, out var snapped))
            {
                destination = snapped;
            }

            UnitCommander.Ensure(units[i]).IssueMove(destination);
        }

        Log(string.Format("[Order] {0} 名单位移动到 {1}（未编队，保持相对位置）", units.Count, point));
        return true;
    }

    /// <summary>
    /// 给不在“整队选中”范围里的单位下个体命令前，先让它们所属小队放弃编队命令，
    /// 并进入手动接管：否则小队行为树会一边把旧的编队槽位盖回来，
    /// 一边在感知到敌人时直接用小队集火覆盖刚下的个体命令。
    /// 手动接管在下一次整队移动/攻击时解除。
    /// </summary>
    private static void DropSquadOrders(List<Unit> units)
    {
        for (int i = 0; i < units.Count; i++)
        {
            var commander = units[i] != null ? units[i].GetComponent<UnitCommander>() : null;
            if (commander == null || commander.SquadId < 0)
            {
                continue;
            }

            var squad = SquadRegistry.Find(commander.SquadId);
            if (squad != null)
            {
                squad.CompleteOrders();
                squad.SetManualControl(true);
            }
        }
    }

    /// <summary>
    /// 当前选择中可接受玩家命令的单位快照。
    /// 开发者模式下敌人也能被选中，但命令只作用于友军战士。
    /// </summary>
    private static List<Unit> AliveCommandableSelection()
    {
        var result = new List<Unit>(UnitSelection.Count);
        var selected = UnitSelection.All;
        for (int i = 0; i < selected.Count; i++)
        {
            var unit = selected[i];
            if (IsCommandable(unit))
            {
                result.Add(unit);
            }
        }

        return result;
    }

    /// <summary>选中全部存活的可指挥友军（F1）。返回选中数量。</summary>
    public int SelectAllPlayerWarriors()
    {
        var available = new List<Unit>();
        var all = UnitRegistry.All;
        for (int i = 0; i < all.Count; i++)
        {
            var health = all[i];
            if (health == null || !health.IsAlive)
            {
                continue;
            }

            var unit = health.GetComponent<Unit>();
            if (IsCommandable(unit))
            {
                available.Add(unit);
            }
        }

        SelectUnits(available);
        Log(available.Count > 0
            ? string.Format("[Select] 全选：当前选择 {0} 人", UnitSelection.Count)
            : "[Select] 全选：场上没有存活的可指挥单位");
        return UnitSelection.Count;
    }

    /// <summary>
    /// 判断单位是否可被玩家选中：普通模式下为存活、Player 阵营、带 WarriorBrain；
    /// 开发者模式下任意存活单位（含敌人）都可选中，便于查看与按 K 销毁。
    /// </summary>
    public static bool IsSelectable(Unit unit)
    {
        if (IsCommandable(unit))
        {
            return true;
        }

        return DeveloperMode.Enabled &&
               unit != null &&
               unit.Health != null &&
               unit.Health.IsAlive;
    }

    /// <summary>判断单位是否可接受玩家命令：存活、Player 阵营、带 WarriorBrain。</summary>
    public static bool IsCommandable(Unit unit)
    {
        return unit != null &&
               unit.Health != null &&
               unit.Health.IsAlive &&
               unit.Team == UnitTeam.Player &&
               unit.GetComponent<WarriorBrain>() != null;
    }

    /// <summary>屏幕矩形内的可指挥单位。抽成静态方法便于无输入环境测试。</summary>
    public static List<Unit> UnitsInScreenRect(Rect rect, Camera camera)
    {
        var result = new List<Unit>();
        if (camera == null)
        {
            return result;
        }

        var all = UnitRegistry.All;
        for (int i = 0; i < all.Count; i++)
        {
            var health = all[i];
            if (health == null || !health.IsAlive)
            {
                continue;
            }

            var unit = health.GetComponent<Unit>();
            if (!IsSelectable(unit))
            {
                continue;
            }

            var unitRect = ScreenRectOf(unit, camera);
            if (unitRect.width < 0f)
            {
                continue;
            }

            if (rect.Overlaps(unitRect))
            {
                result.Add(unit);
            }
        }

        return result;
    }

    /// <summary>
    /// 单位在屏幕上的包围盒（原点左下，像素）。有 Renderer 时用渲染包围盒投影，
    /// 单位比枢轴更“胖”也能选到；没有 Renderer 时按枢轴留 8px 容差。
    /// 完全位于相机背后时返回宽度 -1 的矩形表示无效。
    /// </summary>
    private static Rect ScreenRectOf(Unit unit, Camera camera)
    {
        var renderers = unit.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            Vector3 point = camera.WorldToScreenPoint(unit.transform.position);
            if (point.z <= 0f)
            {
                return new Rect(0f, 0f, -1f, -1f);
            }

            const float tolerance = 8f;
            return new Rect(point.x - tolerance, point.y - tolerance, tolerance * 2f, tolerance * 2f);
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        bool projected = false;
        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;

        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) == 0 ? min.x : max.x,
                (i & 2) == 0 ? min.y : max.y,
                (i & 4) == 0 ? min.z : max.z);
            Vector3 screen = camera.WorldToScreenPoint(corner);
            if (screen.z <= 0f)
            {
                continue;
            }

            projected = true;
            minX = Mathf.Min(minX, screen.x);
            minY = Mathf.Min(minY, screen.y);
            maxX = Mathf.Max(maxX, screen.x);
            maxY = Mathf.Max(maxY, screen.y);
        }

        if (!projected)
        {
            return new Rect(0f, 0f, -1f, -1f);
        }

        // 极小的包围盒给 1px 下限，避免 Rect.Overlaps 对零尺寸直接返回 false。
        if (maxX - minX < 1f)
        {
            minX -= 0.5f;
            maxX += 0.5f;
        }

        if (maxY - minY < 1f)
        {
            minY -= 0.5f;
            maxY += 0.5f;
        }

        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    private void HandleSelectionInput()
    {
        if (Input.GetMouseButtonDown(0))
        {
            if (PointerOverUi())
            {
                Log("[Select] 鼠标在 UI / 开发者放置模式上，已忽略框选");
                return;
            }

            dragging = true;
            dragStart = Input.mousePosition;
            additiveDrag = ShiftHeld();
            selectionRect = new Rect(dragStart, Vector2.zero);
            Log(string.Format("[Select] 开始框选 {0}", dragStart));
        }

        if (dragging && Input.GetMouseButton(0))
        {
            Vector2 current = Input.mousePosition;
            selectionRect = Rect.MinMaxRect(
                Mathf.Min(dragStart.x, current.x),
                Mathf.Min(dragStart.y, current.y),
                Mathf.Max(dragStart.x, current.x),
                Mathf.Max(dragStart.y, current.y));
        }

        if (dragging && Input.GetMouseButtonUp(0))
        {
            dragging = false;

            if (selectionRect.width < MinBoxPixels && selectionRect.height < MinBoxPixels)
            {
                ClickSelect(Input.mousePosition, additiveDrag);
                Log(string.Format("[Select] 点选 {0}，当前选择 {1} 人", Input.mousePosition, UnitSelection.Count));
            }
            else
            {
                SelectUnits(UnitsInScreenRect(selectionRect, cam), additiveDrag);
                Log(string.Format("[Select] 框选 {0}，当前选择 {1} 人", selectionRect, UnitSelection.Count));
            }
        }
    }

    private void OnGUI()
    {
        if (!LegacySelectionEnabled)
        {
            return;
        }

        if (dragging && (selectionRect.width >= 2f || selectionRect.height >= 2f))
        {
            DrawSelectionRect();
        }

        DrawSelectionMarkers();
    }

    /// <summary>屏幕空间选择框：半透明填充 + 亮色边框。OnGUI 原点在左上，需要翻转 Y。</summary>
    private void DrawSelectionRect()
    {
        var guiRect = new Rect(
            selectionRect.x,
            Screen.height - selectionRect.yMax,
            selectionRect.width,
            selectionRect.height);

        Color previous = GUI.color;

        GUI.color = new Color(0.3f, 0.9f, 0.45f, 0.15f);
        GUI.DrawTexture(guiRect, Texture2D.whiteTexture);

        GUI.color = new Color(0.4f, 1f, 0.55f, 0.95f);
        const float thickness = 1.5f;
        GUI.DrawTexture(new Rect(guiRect.x, guiRect.y, guiRect.width, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(guiRect.x, guiRect.yMax - thickness, guiRect.width, thickness), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(guiRect.x, guiRect.y, thickness, guiRect.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(guiRect.xMax - thickness, guiRect.y, thickness, guiRect.height), Texture2D.whiteTexture);

        GUI.color = previous;
    }

    /// <summary>给当前选中的单位画一个小方框，确认选择结果。</summary>
    private void DrawSelectionMarkers()
    {
        if (cam == null || UnitSelection.Count == 0)
        {
            return;
        }

        Color previous = GUI.color;
        GUI.color = new Color(0.35f, 1f, 0.5f, 0.9f);

        var selected = UnitSelection.All;
        for (int i = 0; i < selected.Count; i++)
        {
            var unit = selected[i];
            if (unit == null || unit.Health == null || !unit.Health.IsAlive)
            {
                continue;
            }

            Vector3 screen = cam.WorldToScreenPoint(unit.transform.position);
            if (screen.z <= 0f)
            {
                continue;
            }

            float x = screen.x;
            float y = Screen.height - screen.y;
            const float half = 7f;
            const float thickness = 1.5f;
            GUI.DrawTexture(new Rect(x - half, y - half, half * 2f, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x - half, y + half - thickness, half * 2f, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x - half, y - half, thickness, half * 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x + half - thickness, y - half, thickness, half * 2f), Texture2D.whiteTexture);
        }

        GUI.color = previous;
    }

    private void Log(string message)
    {
        if (logSelection)
        {
            Debug.Log(message, this);
        }
    }

    private void ClickSelect(Vector2 screenPosition, bool additive)
    {
        var picked = PickUnit(screenPosition);
        if (picked != null)
        {
            if (additive)
            {
                UnitSelection.Toggle(picked);
            }
            else
            {
                SelectUnits(new[] { picked });
            }

            return;
        }

        if (!additive)
        {
            UnitSelection.Clear();
        }
    }

    private void HandleCommandInput()
    {
        if (!Input.GetMouseButtonDown(1) || UnitSelection.Count == 0 || PointerOverUi())
        {
            return;
        }

        if (cam == null)
        {
            return;
        }

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out var hit, rayDistance, ~0, QueryTriggerInteraction.Ignore))
        {
            return;
        }

        var hitUnit = hit.collider.GetComponentInParent<Unit>();
        if (hitUnit != null && hitUnit.Health != null && hitUnit.Health.IsAlive &&
            UnitTeamUtility.IsHostile(UnitTeam.Player, hitUnit.Team))
        {
            IssueAttackCommand(hitUnit.Health);
            return;
        }

        Vector3 forward = cam != null ? cam.transform.forward : Vector3.forward;
        IssueMoveCommand(hit.point, forward);
    }

    private void HandleHotkeys()
    {
        if (Input.GetKeyDown(KeyCode.F1))
        {
            SelectAllPlayerWarriors();
            return;
        }

        bool control = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

        // G：把当前选择编成一个新小队，编号自增（Ctrl+G 同效）。
        if (Input.GetKeyDown(KeyCode.G))
        {
            CreateSquadFromSelection();
            return;
        }

        // Z：循环阵型；X：切换姿态（只对完整选中的小队生效）。
        if (Input.GetKeyDown(KeyCode.Z))
        {
            CycleSelectedSquadFormation();
            return;
        }

        if (Input.GetKeyDown(KeyCode.X))
        {
            ToggleSelectedSquadPosture();
            return;
        }

        for (int i = 1; i <= 9; i++)
        {
            if (!Input.GetKeyDown(KeyCode.Alpha0 + i))
            {
                continue;
            }

            if (control)
            {
                AssignSquad(i);
            }
            else
            {
                SelectSquad(i);
            }
        }
    }

    private Unit PickUnit(Vector2 screenPosition)
    {
        if (cam == null)
        {
            return null;
        }

        Ray ray = cam.ScreenPointToRay(screenPosition);
        var hits = Physics.RaycastAll(ray, rayDistance, ~0, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            var unit = hits[i].collider.GetComponentInParent<Unit>();
            if (IsSelectable(unit))
            {
                return unit;
            }
        }

        return null;
    }

    private static bool ShiftHeld()
    {
        return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
    }

    private static bool PointerOverUi()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            return true;
        }

        // 开发者面板是 IMGUI，不经过 EventSystem；放置模式下左键也要留给放置而不是框选。
        return DeveloperSpawnController.BlocksWorldInput(Input.mousePosition);
    }

    private static void DestroyNow(UnityEngine.Object target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(target);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
