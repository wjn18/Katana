using System;
using System.IO;
using OCDesign.Combat;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OCDesign.EditorTools
{
    /// <summary>
    /// 薙刀六招装配入口。定向更新完整角色的攻击绑定和配套资产，保留移动配置与原场景。
    /// 使用 SaveAssetIfDirty / SaveScene 定向保存，避免保存其它已加载但属于用户的资产。
    /// </summary>
    public static class NaginataCombatSetup
    {
        public const string Root = "Assets/KatanaDemo/NaginataCombat";
        public const string PrefabPath = Root + "/Prefabs/NaginataCombatPlayer.prefab";
        public const string ScenePath = Root + "/Scenes/NaginataThirdPersonCombat.unity";
        public const string ControllerPath = Root + "/Animations/NaginataCombat.controller";
        private const string SourceAnimations = "Assets/KatanaDemo/Characters/Douli/Animations/Humanoid/";
        private const string ActiveHeavyState = "NaginataHeavyAttack03Fix";
        private const string LegacyHeavyState = "NaginataHeavyAttack03";
        private const int AttackCount = 6;

        [MenuItem("Katana/Naginata/Create Or Update Naginata Demo")]
        public static void CreateDemo()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请在 Edit 模式装配角色。");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("当前场景有未保存编辑，请先保存；装配器不会覆盖或丢弃它们。");
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath))
                throw new InvalidOperationException("正式薙刀角色 Prefab 不存在：" + PrefabPath
                    + "。请先恢复正式资源；装配器不会重新创建已合并的独立视觉 Prefab。");

            EnsureFolder(Root + "/Config");
            EnsureFolder(Root + "/Animations");
            EnsureFolder(Root + "/Prefabs");
            EnsureFolder(Root + "/Scenes");
            EnsureFolder(Root + "/Materials");
            // 先确认所有 Blender 适配动作都已就绪，避免只装配了一部分招式。
            AnimationClip[] clips = LoadNaginataAttacks();
            var movement = LoadOrCreate<CombatMovementSettings>(Root + "/Config/NaginataMovement.asset");
            var attacks = new CombatAttackDefinition[AttackCount];
            for (int i = 0; i < attacks.Length; i++)
                attacks[i] = CreateAttack(AttackStateName(i),
                    i == 2 ? "Naginata Heavy Attack 03 Fix" : "Naginata Attack " + (i + 1).ToString("00"));
            var input = CreateInputs();
            var controller = CreateController(clips);
            CreatePrefab(movement, attacks, input, controller);
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath)) CreateScene();
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Debug.Log("薙刀六招第三人称操控已就绪：" + ScenePath);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/'));
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            T value = AssetDatabase.LoadAssetAtPath<T>(path);
            if (value) return value;
            value = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(value, path);
            return value;
        }

        private static CombatAttackDefinition CreateAttack(string state, string label)
        {
            string path = Root + "/Config/" + state + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<CombatAttackDefinition>(path);
            var result = existing ? existing : LoadOrCreate<CombatAttackDefinition>(path);
            bool inheritedHeavy = false;
            if (!existing && state == ActiveHeavyState)
            {
                var previous = AssetDatabase.LoadAssetAtPath<CombatAttackDefinition>(
                    Root + "/Config/" + LegacyHeavyState + ".asset");
                if (previous)
                {
                    EditorUtility.CopySerialized(previous, result);
                    result.name = state;
                    inheritedHeavy = true;
                }
            }
            var serialized = new SerializedObject(result);
            if (!existing)
            {
                serialized.FindProperty("displayName").stringValue = label;
                serialized.FindProperty("statePath").stringValue = "Base Layer." + state;
                if (!inheritedHeavy)
                {
                    serialized.FindProperty("playbackSpeed").floatValue = 1f;
                    serialized.FindProperty("blendInSeconds").floatValue = 0.08f;
                }
            }
            // 只迁移一次默认连段窗口，后续装配保留用户调节值和重击高度曲线。
            if (serialized.FindProperty("comboConfigurationVersion").intValue < 1)
            {
                serialized.FindProperty("comboLinkNormalized").floatValue =
                    state.EndsWith("02") || state.EndsWith("05") ? 0.62f : 0.6f;
                serialized.FindProperty("comboInputCloseNormalized").floatValue = 0.9f;
                serialized.FindProperty("comboConfigurationVersion").intValue = 1;
            }
            SerializedProperty height = serialized.FindProperty("airborneHeight");
            if (!existing && state == ActiveHeavyState)
            {
                AnimationCurve previousHeight = height.animationCurveValue;
                height.animationCurveValue = CreateUserFixHeightCurve(
                    previousHeight.length > 1 ? previousHeight : CreateHeavyHeightCurve());
            }
            if (serialized.FindProperty("attackTimingConfigurationVersion").intValue < 1)
                ConfigureAttackTiming(serialized, state);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(result);
            return result;
        }

        private static void ConfigureAttackTiming(SerializedObject serialized, string state)
        {
            float duration, start, peakTime, damageEnd, end, peakSpeed;
            Vector2[] windows;
            switch (state)
            {
                case "NaginataAttack01":
                    duration = 1.35f; start = .34f; peakTime = .45f; damageEnd = .56f; end = .63f; peakSpeed = 4.5f;
                    windows = new[] { new Vector2(.42f, .56f) }; break;
                case "NaginataAttack02":
                    duration = 1.4166667f; start = .28f; peakTime = .40f; damageEnd = .52f; end = .59f; peakSpeed = 4f;
                    windows = new[] { new Vector2(.36f, .52f) }; break;
                case "NaginataAttack04":
                    duration = 1.2166667f; start = .20f; peakTime = .31f; damageEnd = .44f; end = .51f; peakSpeed = 3.8f;
                    windows = new[] { new Vector2(.27f, .44f) }; break;
                case "NaginataAttack05":
                    duration = 1.2666667f; start = .25f; peakTime = .38f; damageEnd = .53f; end = .60f; peakSpeed = 4.2f;
                    windows = new[] { new Vector2(.33f, .53f) }; break;
                case "NaginataAttack06":
                    duration = 1.6166667f; start = .19f; peakTime = .30f; damageEnd = .41f; end = .48f; peakSpeed = 4f;
                    // 第六招两次挥击，但按用户要求只在第一击跨一步；回斩原地完成。
                    windows = new[] { new Vector2(.26f, .41f), new Vector2(.94f, 1.16f) }; break;
                case ActiveHeavyState:
                    duration = 2.75f; start = 1.26f; peakTime = 1.35f; damageEnd = 1.46f; end = 1.51f; peakSpeed = 6.2f;
                    windows = new[] { new Vector2(1.34f, 1.46f) }; break;
                default: return; // 保留旧重击配置，不给历史版本强行迁移。
            }

            // 加速段目标为平方增长：起步轻、出招迅速。命中段保持高速度，尾部70ms内急停。
            float rise = (peakTime - start) / duration;
            float coast = (damageEnd - peakTime) / duration;
            float brake = (end - damageEnd) / duration;
            float coastSlope = -.15f * peakSpeed / coast;
            float brakeSlope = -.85f * peakSpeed / brake;
            var curve = new AnimationCurve(
                new Keyframe(start / duration, 0f, 0f, 0f),
                new Keyframe(peakTime / duration, peakSpeed, 2f * peakSpeed / rise, coastSlope),
                new Keyframe(damageEnd / duration, .85f * peakSpeed, coastSlope, brakeSlope),
                new Keyframe(end / duration, 0f, brakeSlope, 0f));
            serialized.FindProperty("stepSpeed").animationCurveValue = curve;
            serialized.FindProperty("stepBaseAcceleration").floatValue = 18f;
            serialized.FindProperty("stepSpeedAccelerationGain").floatValue = 10f;
            serialized.FindProperty("stepBraking").floatValue = state == ActiveHeavyState ? 130f : 90f;
            var damage = serialized.FindProperty("damageWindows");
            damage.arraySize = windows.Length;
            for (int i = 0; i < windows.Length; i++)
                damage.GetArrayElementAtIndex(i).vector2Value = windows[i] / duration;
            serialized.FindProperty("attackTimingConfigurationVersion").intValue = 1;
        }

        private static AnimationCurve CreateHeavyHeightCurve()
        {
            const float originalDuration = 2.55f;
            const float duration = 2.25f;
            float[] oldSeconds = { 0f, 0.22f, 0.5f, 0.95f, 1.25f, 1.39f, 1.47f, 1.64f, 1.75f };
            float[] heights = { 0f, 0f, 0.48f, 0.65f, 0.65f, 0.5f, 0.34f, 0.04f, 0f };
            var keys = new Keyframe[oldSeconds.Length];
            for (int i = 0; i < keys.Length; i++)
                keys[i] = new Keyframe(oldSeconds[i] / originalDuration, heights[i]);
            var curve = new AnimationCurve(keys);
            for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
            // 先还原原曲线；只对原时间轴平滑，避免压缩落斩时改变前段起跳/滞空形状。
            foreach (int index in new[] { 0, 1, 3, 4, 8 })
            {
                Keyframe key = curve[index];
                key.inTangent = key.outTangent = 0f;
                curve.MoveKey(index, key);
            }

            // 1.30 秒位于两键之间，以 Hermite 值和解析导数拆分，保持原三次曲线。
            float splitTime = 1.30f / originalDuration;
            Keyframe left = curve[4];
            Keyframe right = curve[5];
            float span = right.time - left.time;
            float u = (splitTime - left.time) / span;
            float derivative = ((6f * u * u - 6f * u) * left.value
                + (-6f * u * u + 6f * u) * right.value) / span
                + (3f * u * u - 4f * u + 1f) * left.outTangent
                + (3f * u * u - 2f * u) * right.inTangent;
            curve.AddKey(new Keyframe(splitTime, curve.Evaluate(splitTime), derivative, derivative));

            keys = curve.keys;
            const float epsilon = 0.00001f;
            for (int i = 0; i < keys.Length; i++)
            {
                Keyframe key = keys[i];
                float oldTime = key.time * originalDuration;
                float time = oldTime <= 1.30f ? oldTime
                    : oldTime <= 1.75f ? 1.30f + (oldTime - 1.30f) / 3f : oldTime - 0.30f;
                // 时间映射在 1.30/1.75 秒处分段；边界键的左右切线分别应用链式法则。
                float inSlope = oldTime > 1.30f + epsilon && oldTime <= 1.75f + epsilon ? 1f / 3f : 1f;
                float outSlope = oldTime >= 1.30f - epsilon && oldTime < 1.75f - epsilon ? 1f / 3f : 1f;
                key.time = time / duration;
                key.value *= 2f;
                key.inTangent *= 2f * duration / (originalDuration * inSlope);
                key.outTangent *= 2f * duration / (originalDuration * outSlope);
                keys[i] = key;
            }
            return new AnimationCurve(keys) { preWrapMode = curve.preWrapMode, postWrapMode = curve.postWrapMode };
        }

        private static AnimationCurve CreateUserFixHeightCurve(AnimationCurve original)
        {
            const float oldDuration = 2.25f;
            const float newDuration = 2.75f;
            // 用户在 Blender 中保持前 85 帧，重排 85–100 帧，100 帧后整体后移 30 帧。
            // 原时间键从 85 起每半帧一个；只在输入高度曲线覆盖的边界插入保形键。
            // 当前高度于旧帧 88 结束，因此实际新增边界仅到新帧 91（1.50 秒落地）。
            float[] newFrames = { 85f, 86.5f, 87f, 88.5f, 89f, 90.5f, 91f, 92.5f,
                93f, 94.5f, 96f, 98.5f, 100f, 101.5f, 104f, 108.5f, 113f, 116.5f,
                117f, 118.5f, 119f, 120.5f, 121f, 122.5f, 123f, 124.5f, 125f, 126.5f,
                127f, 128.5f, 130f };
            var curve = new AnimationCurve(original.keys)
            { preWrapMode = original.preWrapMode, postWrapMode = original.postWrapMode };
            for (int i = 0; i < newFrames.Length; i++)
                InsertHeightCurveKey(curve, (84f + i * 0.5f) / 60f / oldDuration);

            Keyframe[] keys = curve.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                Keyframe key = keys[i];
                float oldFrame = key.time * oldDuration * 60f + 1f;
                // 归一化 float 往返可能略偏离边界，先吸附回精确半帧再区分左右切线。
                for (int boundary = 0; boundary < newFrames.Length; boundary++)
                {
                    float frame = 85f + boundary * 0.5f;
                    if (Mathf.Abs(oldFrame - frame) < 0.0001f) { oldFrame = frame; break; }
                }
                float mappedFrame = oldFrame >= 100f ? oldFrame + 30f : oldFrame;
                float inSlope = 1f, outSlope = 1f;
                for (int segment = 0; segment < newFrames.Length - 1; segment++)
                {
                    float start = 85f + segment * 0.5f;
                    float end = start + 0.5f;
                    float slope = (newFrames[segment + 1] - newFrames[segment]) / 0.5f;
                    if (oldFrame >= start && oldFrame <= end)
                        mappedFrame = newFrames[segment] + (oldFrame - start) * slope;
                    if (oldFrame > start && oldFrame <= end) inSlope = slope;
                    if (oldFrame >= start && oldFrame < end) outSlope = slope;
                }
                key.time = (mappedFrame - 1f) / 60f / newDuration;
                key.inTangent *= newDuration / (oldDuration * inSlope);
                key.outTangent *= newDuration / (oldDuration * outSlope);
                keys[i] = key;
            }
            return new AnimationCurve(keys)
            { preWrapMode = original.preWrapMode, postWrapMode = original.postWrapMode };
        }

        private static void InsertHeightCurveKey(AnimationCurve curve, float time)
        {
            for (int i = 0; i < curve.length; i++)
                if (Mathf.Abs(curve[i].time - time) < 0.000001f) return;
            for (int i = 1; i < curve.length; i++)
            {
                Keyframe left = curve[i - 1], right = curve[i];
                if (time <= left.time || time >= right.time) continue;
                float span = right.time - left.time;
                float u = (time - left.time) / span;
                float derivative = ((6f * u * u - 6f * u) * left.value
                    + (-6f * u * u + 6f * u) * right.value) / span
                    + (3f * u * u - 4f * u + 1f) * left.outTangent
                    + (3f * u * u - 2f * u) * right.inTangent;
                curve.AddKey(new Keyframe(time, curve.Evaluate(time), derivative, derivative));
                return;
            }
        }

        private static InputActionAsset CreateInputs()
        {
            string path = Root + "/Config/CombatControls.inputactions";
            var existing = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
            // 在副本上增补数字键，保留已有动作 GUID、用户绑定和共享资产的启用状态。
            var asset = existing ? UnityEngine.Object.Instantiate(existing)
                : ScriptableObject.CreateInstance<InputActionAsset>();
            asset.name = existing ? existing.name : "CombatControls";
            var map = asset.FindActionMap("Gameplay", false);
            if (map == null)
            {
                map = new InputActionMap("Gameplay");
                asset.AddActionMap(map);
                var move = map.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");
                move.AddCompositeBinding("2DVector")
                    .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                    .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
                move.AddCompositeBinding("2DVector")
                    .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                    .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
                map.AddAction("Look", InputActionType.Value, "<Mouse>/delta", expectedControlLayout: "Vector2");
                map.AddAction("Sprint", InputActionType.Button, "<Keyboard>/leftShift").AddBinding("<Keyboard>/rightShift");
                map.AddAction("PrimaryAttack", InputActionType.Button, "<Mouse>/leftButton");
                map.AddAction("SecondaryAttack", InputActionType.Button, "<Mouse>/rightButton");
                map.AddAction("ReleaseCursor", InputActionType.Button, "<Keyboard>/escape");
                map.AddAction("CaptureCursor", InputActionType.Button, "<Mouse>/leftButton");
            }
            bool changed = !existing;
            for (int i = 1; i <= AttackCount; i++)
            {
                string name = "Attack" + i.ToString("00");
                string binding = "<Keyboard>/" + i;
                InputAction action = map.FindAction(name, false);
                if (action == null)
                { action = map.AddAction(name, InputActionType.Button); changed = true; }
                if (action.type != InputActionType.Button)
                {
                    UnityEngine.Object.DestroyImmediate(asset);
                    throw new InvalidOperationException(name + " 必须为 Button；请检查现有输入配置。");
                }
                bool bound = false;
                foreach (InputBinding value in action.bindings)
                    if (string.Equals(value.path, binding, StringComparison.OrdinalIgnoreCase)) bound = true;
                if (!bound) { action.AddBinding(binding); changed = true; }
            }
            if (!changed)
            { UnityEngine.Object.DestroyImmediate(asset); return existing; }
            File.WriteAllText(path, asset.ToJson(), new System.Text.UTF8Encoding(false));
            UnityEngine.Object.DestroyImmediate(asset);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
        }

        private static AnimationClip[] LoadNaginataAttacks()
        {
            var clips = new AnimationClip[AttackCount];
            for (int i = 0; i < clips.Length; i++)
            {
                string name = AttackStateName(i);
                clips[i] = AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + "/Animations/" + name + ".anim");
                if (!clips[i] || clips[i].length <= 0f)
                    throw new InvalidOperationException("请先导入 Blender 适配动作：" + name + ".anim");
                if (clips[i].isLooping)
                    throw new InvalidOperationException(name + " 必须为非循环片段。");
                // 时长取正式片段，不保留旧锤击的 5.4 / 6.8 秒假设。
                Debug.Log(name + "：" + clips[i].length.ToString("0.###") + " 秒。");
            }
            return clips;
        }

        private static string AttackStateName(int index) => index == 2
            ? ActiveHeavyState : "NaginataAttack" + (index + 1).ToString("00");

        private static AnimatorController CreateController(AnimationClip[] attacks)
        {
            // 正式薙刀控制器，仅维护当前移动与攻击绑定。
            string path = ControllerPath;
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            bool isNew = !controller;
            if (isNew) controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            if (isNew)
            {
                controller.AddParameter(CombatAnimationDriver.MoveParameter, AnimatorControllerParameterType.Float);
                controller.AddParameter(new AnimatorControllerParameter
                { name = CombatAnimationDriver.AttackSpeedParameter, type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
                var stateMachine = controller.layers[0].stateMachine;
                var locomotion = stateMachine.AddState("Locomotion", new Vector3(40f, 100f));
                locomotion.writeDefaultValues = true; // 所有状态一致；原 Humanoid 曲线与姿势默认值保持。
                var tree = new BlendTree
                { name = "Idle Walk Run", blendType = BlendTreeType.Simple1D,
                    blendParameter = CombatAnimationDriver.MoveParameter, useAutomaticThresholds = false };
                AssetDatabase.AddObjectToAsset(tree, controller);
                tree.AddChild(AssetDatabase.LoadAssetAtPath<AnimationClip>(SourceAnimations + "NaginataIdle.anim"), 0f);
                tree.AddChild(AssetDatabase.LoadAssetAtPath<AnimationClip>(SourceAnimations + "NaginataWalk.anim"), 1f);
                tree.AddChild(AssetDatabase.LoadAssetAtPath<AnimationClip>(SourceAnimations + "NaginataRun.anim"), 2f);
                locomotion.motion = tree;
                stateMachine.defaultState = locomotion;
            }
            var machine = controller.layers[0].stateMachine;
            int[] normalOrder = { 0, 1, 3, 4, 5 };
            for (int step = 0; step < normalOrder.Length; step++)
            {
                int index = normalOrder[step];
                AddAttackState(machine, AttackStateName(index), attacks[index],
                    new Vector3(300f + step * 230f, 20f), "NormalCombo" + (step + 1));
            }
            AddAttackState(machine, AttackStateName(2), attacks[2], new Vector3(530f, 210f), "AerialHeavy03");
            // 已删除的历史片段不再补建；当前控制器只维护移动和六个运行攻击。
            // 过渡由单一动画适配器管理，避免 Animator 条件和代码维护两套攻击状态。
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);
            return controller;
        }

        private static void AddAttackState(AnimatorStateMachine machine, string name, AnimationClip clip,
            Vector3 position, string tag)
        {
            AnimatorState state = null;
            foreach (ChildAnimatorState child in machine.states)
                if (child.state.name == name) { state = child.state; break; }
            if (!state) state = machine.AddState(name, position);
            state.motion = clip;
            state.tag = tag;
            state.writeDefaultValues = true;
            state.speedParameter = CombatAnimationDriver.AttackSpeedParameter;
            state.speedParameterActive = true;
        }

        private static void CreatePrefab(CombatMovementSettings movement, CombatAttackDefinition[] attacks,
            InputActionAsset inputAsset, RuntimeAnimatorController controller)
        {
            // 只更新完整角色的攻击数组和输入引用，保留模型、挂点及用户移动配置。
            var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var driver = contents.GetComponent<CombatAnimationDriver>();
                if (!driver || !driver.Target)
                    throw new InvalidOperationException("现有角色缺少 CombatAnimationDriver 或 Animator。");
                driver.Configure(driver.Target, attacks);
                driver.Target.runtimeAnimatorController = controller;
                PrefabUtility.RecordPrefabInstancePropertyModifications(driver.Target);
                var playerInput = contents.GetComponent<CombatPlayerInput>();
                if (playerInput) playerInput.Configure(inputAsset);
                ConfigurePolearmGround(contents);
                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        private static void ConfigurePolearmGround(GameObject player)
        {
            // 跨程序集只按组件名读取，设置仅落在当前完整薙刀角色 Prefab 中。
            foreach (MonoBehaviour component in player.GetComponentsInChildren<MonoBehaviour>(true))
                if (component && component.GetType().Name == "TwoHandPolearmGrip")
                {
                    var serialized = new SerializedObject(component);
                    SerializedProperty worldGround = serialized.FindProperty("useWorldGround");
                    if (worldGround == null) continue;
                    worldGround.boolValue = true;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                }
        }

        private static Material CreateMaterial(string name, Color color)
        {
            string path = Root + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material) return material;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) throw new InvalidOperationException("项目缺少 URP/Lit Shader。");
            material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.15f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void CreateScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var floorMaterial = CreateMaterial("PracticeFloor", new Color(0.22f, 0.26f, 0.28f));
            var lineMaterial = CreateMaterial("PracticeLines", new Color(0.32f, 0.39f, 0.39f));
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Practice Floor";
            floor.transform.position = new Vector3(0f, -0.1f, 0f);
            floor.transform.localScale = new Vector3(100f, 0.2f, 100f);
            floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;

            // 低成本地面网格提供移动参照；视觉条纹不参与碰撞。
            var markings = new GameObject("Distance Grid (2m)");
            for (int axis = 0; axis < 2; axis++)
                for (int i = -12; i <= 12; i++)
                {
                    var stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    stripe.name = "Grid " + axis + " " + i;
                    stripe.transform.SetParent(markings.transform, false);
                    stripe.transform.position = axis == 0 ? new Vector3(i * 2f, 0.002f, 0f) : new Vector3(0f, 0.002f, i * 2f);
                    stripe.transform.localScale = axis == 0 ? new Vector3(0.018f, 0.002f, 48f) : new Vector3(48f, 0.002f, 0.018f);
                    stripe.GetComponent<Renderer>().sharedMaterial = lineMaterial;
                    UnityEngine.Object.DestroyImmediate(stripe.GetComponent<Collider>());
                }

            var key = new GameObject("Key Light").AddComponent<Light>();
            key.type = LightType.Directional;
            key.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            key.color = new Color(1f, 0.94f, 0.83f);
            key.intensity = 2f;
            key.shadows = LightShadows.Soft;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.56f, 0.61f, 0.69f);
            RenderSettings.ambientEquatorColor = new Color(0.35f, 0.38f, 0.42f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.20f, 0.18f);
            RenderSettings.skybox = null;

            var camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.16f, 0.20f, 0.25f);
            camera.fieldOfView = 55f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 180f;
            camera.gameObject.AddComponent<AudioListener>();
            var orbit = camera.gameObject.AddComponent<CombatOrbitCamera>();
            var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), scene);
            var actor = player.GetComponent<CombatActorController>();
            actor.BindCamera(orbit);
            PrefabUtility.RecordPrefabInstancePropertyModifications(actor);
            orbit.ResetView();
            var overlay = new GameObject("Controls Overlay").AddComponent<CombatControlsOverlay>();
            overlay.Configure(player.GetComponent<CombatAnimationDriver>());
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.CloseScene(scene, true);
        }
    }
}
