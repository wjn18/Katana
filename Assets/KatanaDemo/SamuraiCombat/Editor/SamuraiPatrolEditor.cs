using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OCDesign.Samurai.EditorTools
{
    [CustomEditor(typeof(SamuraiPatrolAgent))]
    internal sealed class SamuraiPatrolAgentEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var agent = (SamuraiPatrolAgent)target;
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("patrolEnabled"), new GUIContent("启用巡逻"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("route"), new GUIContent("巡逻路线"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("startPoint"), new GUIContent("起始路点（从 0 开始）"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("speedMultiplier"), new GUIContent("行走速度倍率"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("arrivalDistance"), new GUIContent("到达距离（米）"));
                serializedObject.ApplyModifiedProperties();
                if (agent.route == null)
                {
                    if (GUILayout.Button("创建巡逻路线")) SamuraiPatrolSceneTools.CreateRoute(agent);
                }
                else
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("选择路线")) Selection.activeGameObject = agent.route.gameObject;
                        if (GUILayout.Button("定位路线")) SamuraiPatrolSceneTools.FrameRoute(agent.route);
                    }
                    SamuraiPatrolSceneTools.DrawTools(agent.route);
                }
            }
            if (Application.isPlaying)
            {
                EditorGUILayout.LabelField("当前状态", agent.Status);
                EditorGUILayout.LabelField("目标路点", (agent.CurrentPointIndex + 1).ToString());
                EditorGUILayout.LabelField("脱战返回", agent.IsReturning ? "是" : "否");
                Repaint();
            }
            EditorGUILayout.HelpBox("选中小兵或路线均可编辑。开启添加模式后点击地面；选中编号圆点，再拖动坐标手柄。关闭“编辑路点手柄”可整体移动路线或小兵。路线随场景保存，Play 时只显示运行进度。", MessageType.Info);
            if (agent.GetComponent<SamuraiTrainingEnemy>() == null)
                EditorGUILayout.HelpBox("需要同一对象上的 SamuraiTrainingEnemy 负责统一更新巡逻与战斗。", MessageType.Warning);
            if (agent.route != null && agent.route.transform.IsChildOf(agent.transform))
                EditorGUILayout.HelpBox("路线位于小兵子级，会跟随小兵移动。请将路线移到独立场景根下。", MessageType.Error);
        }
    }

    [CustomEditor(typeof(SamuraiPatrolRoute))]
    internal sealed class SamuraiPatrolRouteEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var route = (SamuraiPatrolRoute)target;
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("mode"), new GUIContent("巡逻方式", "Loop 循环；PingPong 往返。"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("routeColor"), new GUIContent("路线颜色"));
                var points = serializedObject.FindProperty("points");
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("路点（编号从 1 显示）", EditorStyles.boldLabel);
                for (int i = 0; i < points.arraySize; i++)
                {
                    var point = points.GetArrayElementAtIndex(i);
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            if (GUILayout.Toggle(SamuraiPatrolSceneTools.Selected(route) == i, "路点 " + (i + 1), "Button"))
                                SamuraiPatrolSceneTools.Select(route, i);
                            using (new EditorGUI.DisabledScope(i == 0))
                                if (GUILayout.Button("↑", GUILayout.Width(26))) { points.MoveArrayElement(i, i - 1); SamuraiPatrolSceneTools.Select(route, i - 1); }
                            using (new EditorGUI.DisabledScope(i == points.arraySize - 1))
                                if (GUILayout.Button("↓", GUILayout.Width(26))) { points.MoveArrayElement(i, i + 1); SamuraiPatrolSceneTools.Select(route, i + 1); }
                            if (GUILayout.Button("删", GUILayout.Width(30)))
                            {
                                points.DeleteArrayElementAtIndex(i);
                                SamuraiPatrolSceneTools.Select(route, Mathf.Max(0, i - 1));
                                break;
                            }
                        }
                        EditorGUILayout.PropertyField(point.FindPropertyRelative("localPosition"), new GUIContent("局部位置"));
                        var wait = point.FindPropertyRelative("waitSeconds");
                        wait.floatValue = Mathf.Max(0f, EditorGUILayout.FloatField("停留秒数", wait.floatValue));
                        var facing = point.FindPropertyRelative("useFacing");
                        EditorGUILayout.PropertyField(facing, new GUIContent("停留时指定朝向"));
                        if (facing.boolValue) EditorGUILayout.PropertyField(point.FindPropertyRelative("localYaw"), new GUIContent("朝向角度（局部 Y）"));
                    }
                }
                if (serializedObject.ApplyModifiedProperties()) SamuraiPatrolSceneTools.Changed(route);
                SamuraiPatrolSceneTools.DrawTools(route);
            }
            EditorGUILayout.HelpBox("循环会连接末点和首点；往返会沿原路返回。拐角请补路点，确保相邻点之间可直接行走。关闭“编辑路点手柄”可整体移动路线。编辑器检查是基础几何提示，需在 Play 中实际试走。", MessageType.Info);
        }
    }

    /// <summary>场景交互只在编辑器存在；不会移动演员或自动保存场景。</summary>
    [InitializeOnLoad]
    public static class SamuraiPatrolSceneTools
    {
        const string RootName = "Patrol routes - 小兵巡逻路径";
        static SamuraiPatrolRoute editedRoute;
        static int selectedPoint;
        static bool addMode, snapDragged = true, restrictFloor = true, editHandles = true;
        static bool ownsToolVisibility, previousToolsHidden;
        static GUIStyle pointLabelStyle;
        static readonly Dictionary<SamuraiPatrolRoute, CheckResult> checks = new Dictionary<SamuraiPatrolRoute, CheckResult>();

        sealed class CheckResult
        {
            public readonly List<string> messages = new List<string>();
            public readonly HashSet<int> badSegments = new HashSet<int>();
        }

        static SamuraiPatrolSceneTools()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed += OnUndo;
            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
        }
        static void BeforeReload()
        {
            RestoreToolVisibility();
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndo;
            Selection.selectionChanged -= OnSelectionChanged;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
        }
        static void OnUndo() { checks.Clear(); UpdateToolVisibility(); SceneView.RepaintAll(); }
        static void OnSelectionChanged()
        {
            addMode = false;
            UpdateToolVisibility();
            SceneView.RepaintAll();
        }
        static void OnPlayModeChanged(PlayModeStateChange state)
        { addMode = false; checks.Clear(); UpdateToolVisibility(); SceneView.RepaintAll(); }
        static void UpdateToolVisibility()
        {
            var route = SelectedRoute(out _);
            SetToolVisibility(route != null && editHandles && !EditorApplication.isPlayingOrWillChangePlaymode);
        }
        static void SetToolVisibility(bool editing)
        {
            if (!editing) { RestoreToolVisibility(); return; }
            if (!ownsToolVisibility)
            {
                previousToolsHidden = Tools.hidden;
                ownsToolVisibility = true;
            }
            Tools.hidden = true;
        }
        static void RestoreToolVisibility()
        {
            if (!ownsToolVisibility) return;
            Tools.hidden = previousToolsHidden;
            ownsToolVisibility = false;
        }

        static GUIStyle PointLabelStyle
        {
            get
            {
                if (pointLabelStyle == null)
                {
                    pointLabelStyle = new GUIStyle(EditorStyles.helpBox)
                    {
                        fontSize = 12,
                        fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.MiddleLeft,
                        padding = new RectOffset(5, 5, 2, 2)
                    };
                    pointLabelStyle.normal.textColor = EditorGUIUtility.isProSkin ? Color.white : new Color(.08f, .08f, .08f);
                }
                return pointLabelStyle;
            }
        }

        [MenuItem("Katana/Samurai/Patrol/Create Route For Selected Enemy")]
        static void CreateForSelected()
        {
            if (Application.isPlaying || Selection.activeGameObject == null) return;
            var brain = Selection.activeGameObject.GetComponentInParent<SamuraiTrainingEnemy>();
            if (brain == null || EditorUtility.IsPersistent(brain) || !brain.gameObject.scene.IsValid() || !brain.gameObject.scene.isLoaded)
            { Debug.LogWarning("请选择场景中带 SamuraiTrainingEnemy 的小兵。"); return; }
            var agent = brain.GetComponent<SamuraiPatrolAgent>();
            if (agent == null) agent = Undo.AddComponent<SamuraiPatrolAgent>(brain.gameObject);
            CreateRoute(agent);
        }

        public static SamuraiPatrolRoute CreateRoute(SamuraiPatrolAgent agent)
        {
            if (agent == null || Application.isPlaying || EditorUtility.IsPersistent(agent)
                || !agent.gameObject.scene.IsValid() || !agent.gameObject.scene.isLoaded) return null;
            if (agent.route != null) { Selection.activeGameObject = agent.route.gameObject; return agent.route; }
            var scene = agent.gameObject.scene;
            GameObject root = null;
            foreach (var go in scene.GetRootGameObjects()) if (go.name == RootName) { root = go; break; }
            if (root == null)
            {
                root = new GameObject(RootName);
                SceneManager.MoveGameObjectToScene(root, scene);
                Undo.RegisterCreatedObjectUndo(root, "创建巡逻路线分组");
            }
            var routeObject = new GameObject(agent.name + " Patrol - 巡逻路线");
            SceneManager.MoveGameObjectToScene(routeObject, scene);
            Undo.RegisterCreatedObjectUndo(routeObject, "创建巡逻路线");
            routeObject.transform.SetParent(root.transform, true);
            routeObject.transform.position = agent.transform.position;
            var route = Undo.AddComponent<SamuraiPatrolRoute>(routeObject);
            Vector3 start = agent.transform.position;
            if (TryGround(route, start, .8f, out Vector3 grounded)) start = grounded;
            route.points = new List<SamuraiPatrolPoint> { new SamuraiPatrolPoint
            {
                localPosition = route.transform.InverseTransformPoint(start), waitSeconds = 1f,
                useFacing = true, localYaw = agent.transform.eulerAngles.y - route.transform.eulerAngles.y
            }};
            Undo.RecordObject(agent, "绑定巡逻路线");
            agent.route = route;
            Record(agent);
            Changed(route);
            Select(route, 0);
            Selection.activeGameObject = routeObject;
            return route;
        }

        internal static int Selected(SamuraiPatrolRoute route) => editedRoute == route ? selectedPoint : 0;
        internal static void Select(SamuraiPatrolRoute route, int index)
        {
            if (editedRoute != route) addMode = false;
            editedRoute = route;
            selectedPoint = Mathf.Clamp(index, 0, Mathf.Max(0, route.points.Count - 1));
            SceneView.RepaintAll();
        }
        internal static void Changed(SamuraiPatrolRoute route)
        {
            checks.Remove(route);
            Record(route);
            SceneView.RepaintAll();
        }
        static void Record(Component component)
        {
            EditorUtility.SetDirty(component);
            if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            if (component.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
        }

        internal static void DrawTools(SamuraiPatrolRoute route)
        {
            if (route == null) return;
            bool nextEditHandles = EditorGUILayout.Toggle(new GUIContent("编辑路点手柄", "开启时隐藏对象的普通 Transform 手柄，避免误移动小兵；关闭后可整体移动路线或小兵。"), editHandles);
            if (nextEditHandles != editHandles)
            {
                editHandles = nextEditHandles;
                if (!editHandles) addMode = false;
                UpdateToolVisibility();
                SceneView.RepaintAll();
            }
            bool adding = editedRoute == route && addMode;
            bool nextAdd = GUILayout.Toggle(adding, adding ? "添加模式已开启：点击地面，Esc 结束" : "在场景中添加路点", "Button");
            if (nextAdd != adding)
            {
                Select(route, Selected(route));
                addMode = nextAdd;
                if (addMode) editHandles = true;
                UpdateToolVisibility();
                SceneView.RepaintAll();
            }
            snapDragged = EditorGUILayout.Toggle("拖动时贴地", snapDragged);
            restrictFloor = EditorGUILayout.Toggle(new GUIContent("添加时限制当前楼层", "限制到所选点上下 1 米。跨楼层布点时可暂时关闭。"), restrictFloor);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("在选中点后插入")) InsertPoint(route);
                using (new EditorGUI.DisabledScope(route.points.Count == 0))
                    if (GUILayout.Button("选中点贴地")) SnapSelected(route);
            }
            if (GUILayout.Button("检查路线（墙体／地面／坡度）")) ValidateRoute(route);
            if (checks.TryGetValue(route, out var result))
            {
                if (result.messages.Count == 0) EditorGUILayout.HelpBox("基础几何检查通过。请进入 Play 验证实际行走及动态门状态。", MessageType.Info);
                else foreach (var message in result.messages) EditorGUILayout.HelpBox(message, MessageType.Warning);
            }
        }

        static void InsertPoint(SamuraiPatrolRoute route)
        {
            int after = Mathf.Clamp(Selected(route), 0, Mathf.Max(0, route.points.Count - 1));
            Vector3 world = route.transform.position;
            if (route.points.Count > 0)
                world = after + 1 < route.points.Count ? (route.GetWorldPosition(after) + route.GetWorldPosition(after + 1)) * .5f
                    : route.GetWorldPosition(after) + route.transform.forward;
            if (snapDragged && TryGround(route, world, .8f, out Vector3 ground)) world = ground;
            Undo.RecordObject(route, "插入巡逻路点");
            int index = route.points.Count == 0 ? 0 : after + 1;
            route.points.Insert(index, new SamuraiPatrolPoint { localPosition = route.transform.InverseTransformPoint(world) });
            Select(route, index);
            Changed(route);
        }
        static void SnapSelected(SamuraiPatrolRoute route)
        {
            int index = Mathf.Clamp(Selected(route), 0, route.points.Count - 1);
            if (!TryGround(route, route.GetWorldPosition(index), .8f, out Vector3 ground))
            { SceneView.lastActiveSceneView?.ShowNotification(new GUIContent("当前路点附近没有可用地面")); return; }
            Undo.RecordObject(route, "巡逻路点贴地");
            route.points[index].localPosition = route.transform.InverseTransformPoint(ground);
            Changed(route);
        }

        public static void FrameRoute(SamuraiPatrolRoute route)
        {
            if (route == null || SceneView.lastActiveSceneView == null) return;
            var bounds = new Bounds(route.transform.position, Vector3.one * 2f);
            for (int i = 0; i < route.points.Count; i++) bounds.Encapsulate(route.GetWorldPosition(i));
            SceneView.lastActiveSceneView.Frame(bounds, false);
        }

        static SamuraiPatrolRoute SelectedRoute(out SamuraiPatrolAgent owner)
        {
            owner = null;
            var selected = Selection.activeGameObject;
            if (selected == null || EditorUtility.IsPersistent(selected)) return null;
            var route = selected.GetComponentInParent<SamuraiPatrolRoute>();
            if (route == null)
            {
                owner = selected.GetComponentInParent<SamuraiPatrolAgent>();
                return owner != null ? owner.route : null;
            }
            owner = FindOwner(route);
            return route;
        }
        static SamuraiPatrolAgent FindOwner(SamuraiPatrolRoute route)
        {
            foreach (var agent in Object.FindObjectsByType<SamuraiPatrolAgent>(FindObjectsInactive.Include))
                if (agent.route == route && agent.gameObject.scene == route.gameObject.scene) return agent;
            return null;
        }

        static void OnSceneGUI(SceneView view)
        {
            var route = SelectedRoute(out var owner);
            SetToolVisibility(route != null && editHandles && !EditorApplication.isPlayingOrWillChangePlaymode);
            if (route == null || route.points == null) return;
            if (editedRoute != route) { editedRoute = route; selectedPoint = 0; addMode = false; }
            selectedPoint = Mathf.Clamp(selectedPoint, 0, Mathf.Max(0, route.points.Count - 1));
            Event evt = Event.current;
            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape && addMode)
            { addMode = false; evt.Use(); view.Repaint(); }
            checks.TryGetValue(route, out var result);
            int segments = route.mode == SamuraiPatrolMode.Loop ? route.points.Count : route.points.Count - 1;
            for (int i = 0; route.points.Count > 1 && i < segments; i++)
            {
                Vector3 a = route.GetWorldPosition(i) + Vector3.up * .06f;
                Vector3 b = route.GetWorldPosition((i + 1) % route.points.Count) + Vector3.up * .06f;
                Handles.color = result != null && result.badSegments.Contains(i) ? Color.red : route.routeColor;
                Handles.DrawAAPolyLine(3f, a, b);
                Vector3 direction = b - a;
                if (direction.sqrMagnitude > .02f)
                    Handles.ConeHandleCap(0, Vector3.Lerp(a, b, .6f), Quaternion.LookRotation(direction),
                        Mathf.Min(.3f, HandleUtility.GetHandleSize((a + b) * .5f) * .08f), EventType.Repaint);
            }
            for (int i = 0; i < route.points.Count; i++)
            {
                if (route.points[i] == null) continue;
                Vector3 point = route.GetWorldPosition(i);
                bool current = Application.isPlaying && owner != null && owner.CurrentPointIndex == i;
                Handles.color = current ? Color.cyan : i == selectedPoint ? Color.yellow : route.routeColor;
                float size = HandleUtility.GetHandleSize(point) * .065f;
                if (!Application.isPlaying && editHandles && !addMode)
                {
                    if (Handles.Button(point + Vector3.up * .08f, Quaternion.identity, size, size * 1.3f, Handles.SphereHandleCap))
                    { Select(route, i); GUI.changed = true; }
                }
                else Handles.SphereHandleCap(0, point + Vector3.up * .08f, Quaternion.identity, size, EventType.Repaint);
                Handles.Label(point + Vector3.up * .3f, (i + 1) + (route.points[i].waitSeconds > 0 ? "  停 " + route.points[i].waitSeconds.ToString("0.#") + "s" : ""), PointLabelStyle);
                if (route.points[i].useFacing)
                    Handles.DrawDottedLine(point + Vector3.up * .15f, point + Vector3.up * .15f + route.GetWorldFacing(i) * .7f, 3f);
            }
            if (Application.isPlaying || !editHandles) return;
            if (addMode)
            {
                int control = GUIUtility.GetControlID(FocusType.Passive);
                if (evt.type == EventType.Layout && !evt.alt) HandleUtility.AddDefaultControl(control);
                if (evt.type == EventType.MouseDown && evt.button == 0 && !evt.alt && GUIUtility.hotControl == 0)
                {
                    if (PickGround(route, HandleUtility.GUIPointToWorldRay(evt.mousePosition), out Vector3 position))
                    {
                        Undo.RecordObject(route, "添加巡逻路点");
                        route.points.Add(new SamuraiPatrolPoint { localPosition = route.transform.InverseTransformPoint(position) });
                        selectedPoint = route.points.Count - 1;
                        Changed(route);
                    }
                    else view.ShowNotification(new GUIContent("请点击当前楼层的可走地面；跨层时可关闭楼层限制"));
                    evt.Use();
                }
                if (evt.type == EventType.MouseMove) view.Repaint();
                return;
            }
            if (route.points.Count == 0 || route.points[selectedPoint] == null) return;
            Vector3 oldPosition = route.GetWorldPosition(selectedPoint);
            EditorGUI.BeginChangeCheck();
            Vector3 newPosition = Handles.PositionHandle(oldPosition, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                if (snapDragged)
                {
                    if (!TryGround(route, newPosition, .8f, out Vector3 ground))
                    { view.ShowNotification(new GUIContent("拖动位置附近没有可用地面；可关闭拖动贴地手动调整")); return; }
                    newPosition = ground;
                }
                Undo.RecordObject(route, "移动巡逻路点");
                route.points[selectedPoint].localPosition = route.transform.InverseTransformPoint(newPosition);
                Changed(route);
            }
        }

        static bool EnvironmentHit(SamuraiPatrolRoute route, Collider collider) => collider != null && !collider.isTrigger
            && collider.gameObject.scene == route.gameObject.scene && collider.GetComponentInParent<SamuraiCombatant>() == null;

        static bool PickGround(SamuraiPatrolRoute route, Ray ray, out Vector3 position)
        {
            position = default;
            RaycastHit best = default;
            float nearest = float.PositiveInfinity;
            foreach (var hit in Physics.RaycastAll(ray, 2000f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!EnvironmentHit(route, hit.collider) || hit.distance >= nearest) continue;
                best = hit; nearest = hit.distance;
            }
            if (float.IsPositiveInfinity(nearest) || best.normal.y < .55f) return false;
            if (restrictFloor && route.points.Count > 0
                && Mathf.Abs(best.point.y - route.GetWorldPosition(selectedPoint).y) > 1f) return false;
            position = best.point + Vector3.up * .01f;
            return true;
        }
        static bool TryGround(SamuraiPatrolRoute route, Vector3 point, float range, out Vector3 ground)
        {
            ground = point;
            float nearest = float.PositiveInfinity;
            foreach (var hit in Physics.RaycastAll(point + Vector3.up * range, Vector3.down, range * 2f, ~0, QueryTriggerInteraction.Ignore))
            {
                float delta = Mathf.Abs(hit.point.y - point.y);
                if (!EnvironmentHit(route, hit.collider) || hit.normal.y < .55f || delta >= nearest) continue;
                nearest = delta; ground = hit.point + Vector3.up * .01f;
            }
            return !float.IsPositiveInfinity(nearest);
        }

        /// <summary>基础几何提示。不会移动角色、模拟物理或宣称完整寻路可达。</summary>
        public static string[] ValidateRoute(SamuraiPatrolRoute route)
        {
            var result = new CheckResult();
            if (route == null) return new[] { "没有巡逻路线。" };
            if (route.points == null || route.points.Count == 0)
            { result.messages.Add("路线没有路点。请开启添加模式，点击地面布点。"); checks[route] = result; return result.messages.ToArray(); }
            Physics.SyncTransforms();
            var owner = FindOwner(route);
            var actor = owner != null ? owner.GetComponent<SamuraiCombatant>() : null;
            var motor = actor != null ? actor.Motor : null;
            float scaleXZ = motor != null ? Mathf.Max(Mathf.Abs(motor.transform.lossyScale.x), Mathf.Abs(motor.transform.lossyScale.z)) : 1f;
            float radius = motor != null ? Mathf.Max(.1f, motor.radius * scaleXZ * .9f) : .25f;
            float height = motor != null ? Mathf.Max(radius * 2f + .05f, motor.height * Mathf.Abs(motor.transform.lossyScale.y)) : 1.6f;
            float maxSlope = motor != null ? motor.slopeLimit : 45f;
            for (int i = 0; i < route.points.Count; i++)
                if (route.points[i] == null || !TryGround(route, route.GetWorldPosition(i), .45f, out _))
                    result.messages.Add("路点 " + (i + 1) + " 附近缺少当前楼层地面。");
            int segments = route.mode == SamuraiPatrolMode.Loop ? route.points.Count : route.points.Count - 1;
            for (int segment = 0; route.points.Count > 1 && segment < segments; segment++)
            {
                int next = (segment + 1) % route.points.Count;
                if (route.points[segment] == null || route.points[next] == null) continue;
                Vector3 a = route.GetWorldPosition(segment), b = route.GetWorldPosition(next);
                int samples = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(a, b) / .3f), 1, 300);
                string problem = null;
                Vector3 previous = a;
                for (int s = 0; s <= samples && problem == null; s++)
                {
                    Vector3 expected = Vector3.Lerp(a, b, s / (float)samples);
                    if (!TryGround(route, expected, .45f, out Vector3 ground))
                    { problem = "缺地面或高度跨层，请在台阶／转折处补点"; break; }
                    if (s > 0)
                    {
                        Vector3 delta = ground - previous;
                        float planar = new Vector2(delta.x, delta.z).magnitude;
                        if (planar > .01f && Mathf.Atan2(Mathf.Abs(delta.y), planar) * Mathf.Rad2Deg > maxSlope + 3f)
                        { problem = "坡度或高度突变较大"; break; }
                        float distance = delta.magnitude;
                        if (distance > .001f)
                        {
                            Vector3 lower = previous + Vector3.up * (radius + .12f);
                            Vector3 upper = previous + Vector3.up * (height - radius);
                            foreach (var hit in Physics.CapsuleCastAll(lower, upper, radius, delta / distance, distance, ~0, QueryTriggerInteraction.Ignore))
                                if (EnvironmentHit(route, hit.collider) && hit.normal.y < .55f)
                                { problem = "身体通道被墙、门或其他碰撞体阻挡"; break; }
                        }
                    }
                    previous = ground;
                }
                if (problem == null) continue;
                result.badSegments.Add(segment);
                result.messages.Add("路段 " + (segment + 1) + " → " + (next + 1) + "：" + problem + "。");
            }
            checks[route] = result;
            SceneView.RepaintAll();
            return result.messages.ToArray();
        }
    }
}
