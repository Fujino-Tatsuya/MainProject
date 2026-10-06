using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// PLAN-assassin A6 저작 메뉴. 모델/클립 원본은 건드리지 않고, 재실행 가능한 방식으로 컨트롤러·Armature·Variant·데이터 배선을 맞춘다.
/// </summary>
public static class AssassinShellAuthoring
{
    private const string Tag = "[Assassin/A6]";
    private const string BasePrefabPath = "Assets/2.Prefabs/Player/Player.prefab";
    private const string PrefabFolder = "Assets/2.Prefabs/Player/Assassin";
    private const string ArmaturePath = PrefabFolder + "/Assassin_Armature.prefab";
    private const string VariantPath = PrefabFolder + "/Player_Assassin.prefab";
    private const string ModelPath = "Assets/50.Art/Char/assassin/Assassin.fbx";
    private const string MaterialPath = "Assets/3.Materials/Toon/Assassin_Toon.mat";
    private const string AnimationFolder = "Assets/4.Animations/Player/Assassin";
    private const string ControllerPath = AnimationFolder + "/AssassinAnimatorController.controller";
    private const string DataFolder = "Assets/9.ScriptableObject/Player/Assassin";
    private const string BasicAttackDataPath = DataFolder + "/AssassinBasicAttackData.asset";
    private const string RosterPath = "Assets/9.ScriptableObject/Player/CharacterRoster.asset";

    private static readonly string[] WaveClips =
    {
        "Combo_Attack_Wave_01",
        "Combo_Attack_Wave_02",
        "Combo_Attack_Wave_03",
        "Combo_Attack_Wave_04",
    };

    private static readonly string[] AllClipNames =
    {
        "Idle", "Idle_Combat",
        "Run_Combat_Fast_Start", "Run_Combat_Fast_Loop", "Run_Combat_Fast_Stop",
        "Dodge_Combat_F_0",
        "Combo_Attack_Wave_01", "Combo_Attack_Wave_02", "Combo_Attack_Wave_03", "Combo_Attack_Wave_04",
        "Speed_Attack_Loop", "Combo_Attack_02_04", "Combo_Attack_03_04", "Buff",
        "Combo_Attack_02_01", "Skill_02_Move_000pct", "Attack_Up_01", "Parry_R",
    };

    private static readonly HashSet<string> LoopClips = new HashSet<string>
    {
        "Idle", "Idle_Combat", "Run_Combat_Fast_Loop",
    };

    [MenuItem("Tools/Player/Assassin/A6 전체 구성")]
    public static void BuildAll()
    {
        BuildShell();
        AttachBasicAttackAndIdle();
        StampClipEventsAndLoopSettings();
        AssetDatabase.SaveAssets();
        Debug.Log($"{Tag} 전체 구성 완료. 오류 로그가 없었는지 확인할 것.");
    }

    [MenuItem("Tools/Player/Assassin/1. 셸·Animator·Variant 생성 (A6)")]
    public static void BuildShell()
    {
        EnsureFolder(PrefabFolder);
        EnsureFolder(AnimationFolder);

        AnimatorController controller = EnsureController();
        if (controller == null)
            return;

        BuildAnimator(controller);
        GameObject armature = EnsureArmature(controller);
        if (armature == null)
            return;

        GameObject variant = EnsureVariant(armature);
        if (variant == null)
            return;

        EnsureRosterEntry(variant);
        AssetDatabase.SaveAssets();
        Debug.Log($"{Tag} 셸 생성/갱신 완료: {ControllerPath}, {ArmaturePath}, {VariantPath}");
    }

    [MenuItem("Tools/Player/Assassin/2. 평타·전투 대기 부착 + 데이터 (A6)")]
    public static void AttachBasicAttackAndIdle()
    {
        GameObject variantAsset = AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath);
        if (variantAsset == null)
        {
            Debug.LogError($"{Tag} Variant가 없다. 먼저 1번 메뉴를 실행할 것: {VariantPath}");
            return;
        }

        EnsureFolder(DataFolder);
        AssassinBasicAttackData data = EnsureBasicAttackData();
        if (data == null)
            return;

        GameObject root = PrefabUtility.LoadPrefabContents(VariantPath);
        try
        {
            AssassinConeAttack cone = EnsureComponent<AssassinConeAttack>(root);
            AssassinBasicAttack attack = EnsureComponent<AssassinBasicAttack>(root);
            AssassinCombatIdle combatIdle = EnsureComponent<AssassinCombatIdle>(root);

            SetReference(attack, "data", data);
            SetReference(combatIdle, "data", data);

            Animator animator = root.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                Debug.LogError($"{Tag} Variant 아래 Animator를 찾지 못해 평타/전투 대기 배선을 완료할 수 없다: {VariantPath}");
            }
            else
            {
                SetReference(attack, "animator", animator);
                SetReference(combatIdle, "animator", animator);
            }

            if (cone == null)
                Debug.LogError($"{Tag} AssassinConeAttack 부착 실패: {VariantPath}");

            PrefabUtility.SaveAsPrefabAsset(root, VariantPath);
            Debug.Log($"{Tag} 평타·전투 대기 부착/갱신: {VariantPath} (데이터 {BasicAttackDataPath})");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        RecordAndValidateHash(AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath));
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/Player/Assassin/3. 클립 이벤트·Loop 설정 (A6)")]
    public static void StampClipEventsAndLoopSettings()
    {
        int configured = 0;
        foreach (string clipName in AllClipNames)
        {
            AnimationClip clip = LoadClip(clipName);
            if (clip == null)
                continue;

            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = LoopClips.Contains(clipName);
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            configured++;
        }

        // 일반 평타: 실제 타격 프레임 확인 전 기본값. Play에서 반드시 튜닝한다.
        foreach (string clipName in WaveClips)
            ReplaceEvents(clipName, DefaultAttackEvent(0.40f, DefaultAttackAnimationEventType.Hit),
                DefaultAttackEvent(0.85f, DefaultAttackAnimationEventType.End));

        // A7 연결 자리까지 같은 메뉴가 준비한다. 실제 타격 프레임은 Play에서 튜닝한다.
        ReplaceEvents("Combo_Attack_02_01",
            DefaultAttackEvent(0.40f, DefaultAttackAnimationEventType.Hit),
            DefaultAttackEvent(0.85f, DefaultAttackAnimationEventType.End));
        ReplaceEvents("Speed_Attack_Loop",
            DefaultAttackEvent(0.15f, DefaultAttackAnimationEventType.Hit),
            DefaultAttackEvent(0.35f, DefaultAttackAnimationEventType.Hit),
            DefaultAttackEvent(0.55f, DefaultAttackAnimationEventType.Hit),
            DefaultAttackEvent(0.75f, DefaultAttackAnimationEventType.Hit),
            DefaultAttackEvent(0.90f, DefaultAttackAnimationEventType.End));

        ReplaceEvents("Combo_Attack_02_04", SkillEvent(0.40f, SkillAnimationEventType.Hit), SkillEvent(0.85f, SkillAnimationEventType.End));
        ReplaceEvents("Combo_Attack_03_04", SkillEvent(0.40f, SkillAnimationEventType.Hit), SkillEvent(0.85f, SkillAnimationEventType.End));
        ReplaceEvents("Buff", SkillEvent(0.85f, SkillAnimationEventType.End));
        ReplaceEvents("Skill_02_Move_000pct",
            SkillEvent(0.18f, SkillAnimationEventType.Hit), SkillEvent(0.32f, SkillAnimationEventType.Hit),
            SkillEvent(0.46f, SkillAnimationEventType.Hit), SkillEvent(0.60f, SkillAnimationEventType.Hit),
            SkillEvent(0.74f, SkillAnimationEventType.Hit), SkillEvent(0.90f, SkillAnimationEventType.End));
        ReplaceEvents("Attack_Up_01", SkillEvent(0.40f, SkillAnimationEventType.Hit), SkillEvent(0.85f, SkillAnimationEventType.End));
        ReplaceEvents("Parry_R", SkillEvent(0.85f, SkillAnimationEventType.End));

        AssetDatabase.SaveAssets();
        Debug.Log($"{Tag} 클립 {configured}/{AllClipNames.Length}개 Loop 설정, 이벤트 교체 완료. " +
                  "Hit/End 정규화 시점은 임시값이므로 Play에서 타격 프레임을 튜닝할 것.");
    }

    private static AssassinBasicAttackData EnsureBasicAttackData()
    {
        AssassinBasicAttackData data = AssetDatabase.LoadAssetAtPath<AssassinBasicAttackData>(BasicAttackDataPath);
        bool created = data == null;
        if (created)
        {
            data = ScriptableObject.CreateInstance<AssassinBasicAttackData>();
            AssetDatabase.CreateAsset(data, BasicAttackDataPath);
            Debug.Log($"{Tag} 데이터 생성: {BasicAttackDataPath}");
        }
        else
        {
            Debug.Log($"{Tag} 데이터 재사용(튜닝 값 유지): {BasicAttackDataPath}");
        }

        SerializedObject so = new SerializedObject(data);
        SerializedProperty steps = so.FindProperty("normalSteps");
        if (steps == null)
        {
            Debug.LogError($"{Tag} AssassinBasicAttackData.normalSteps를 찾지 못했다.");
            return null;
        }

        if (steps.arraySize != 4)
        {
            if (!created)
            {
                Debug.LogError($"{Tag} 기존 데이터의 normalSteps가 4개가 아니다({steps.arraySize}). 튜닝 보존을 위해 자동 수정하지 않는다.");
                return null;
            }
            steps.arraySize = 4;
        }

        float[] multipliers = { 1f, 1f, 1f, 1.3f };
        float[] ranges = { 1.5f, 1.5f, 1.6f, 1.7f };
        float[] angles = { 90f, 100f, 100f, 120f };
        float[] speeds = { 3f, 3f, 3f, 2.5f };

        for (int i = 0; i < 4; i++)
        {
            SerializedProperty step = steps.GetArrayElementAtIndex(i);
            SerializedProperty clip = step.FindPropertyRelative("clip");
            if (clip.objectReferenceValue == null)
                clip.objectReferenceValue = LoadClip(WaveClips[i]);

            if (!created)
                continue;

            step.FindPropertyRelative("attackDamageMultiplier").floatValue = multipliers[i];
            step.FindPropertyRelative("range").floatValue = ranges[i];
            step.FindPropertyRelative("angle").floatValue = angles[i];
            step.FindPropertyRelative("playbackSpeed").floatValue = speeds[i];
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(data);
        return data;
    }

    private static AnimatorController EnsureController()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            Debug.Log($"{Tag} Animator Controller 생성: {ControllerPath}");
        }
        else
        {
            Debug.Log($"{Tag} Animator Controller 갱신: {ControllerPath}");
        }

        EnsureParameter(controller, "DefaultAttack", AnimatorControllerParameterType.Trigger);
        EnsureParameter(controller, "Interrupt", AnimatorControllerParameterType.Trigger);
        EnsureParameter(controller, "IsMoving", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "AttackIndex", AnimatorControllerParameterType.Int);
        EnsureParameter(controller, "IsGrabbed", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "IsCombatIdle", AnimatorControllerParameterType.Bool);
        return controller;
    }

    private static void BuildAnimator(AnimatorController controller)
    {
        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        AnimatorState idle = EnsureState(sm, "Idle", LoadClip("Idle"));
        AnimatorState combatIdle = EnsureState(sm, "Idle_Combat", LoadClip("Idle_Combat"));
        AnimatorState runStart = EnsureState(sm, "Run_Start", LoadClip("Run_Combat_Fast_Start"));
        AnimatorState runLoop = EnsureState(sm, "Run_Loop", LoadClip("Run_Combat_Fast_Loop"));
        AnimatorState runStop = EnsureState(sm, "Run_Stop", LoadClip("Run_Combat_Fast_Stop"));
        AnimatorState dodge = EnsureState(sm, "Dodge", LoadClip("Dodge_Combat_F_0"));
        AnimatorState interrupt = EnsureState(sm, "Interrupt", LoadClip("Attack_Up_01"));
        sm.defaultState = idle;

        EnsureTransition(idle, combatIdle, false, 0f, "IsCombatIdle", AnimatorConditionMode.If, 0f);
        EnsureTransition(combatIdle, idle, false, 0f, "IsCombatIdle", AnimatorConditionMode.IfNot, 0f);
        EnsureTransition(idle, runStart, false, 0f, "IsMoving", AnimatorConditionMode.If, 0f);
        EnsureTransition(combatIdle, runStart, false, 0f, "IsMoving", AnimatorConditionMode.If, 0f);
        EnsureTransition(runStart, runLoop, true, 0.85f, null, default, 0f);
        EnsureTransition(runStart, runStop, false, 0f, "IsMoving", AnimatorConditionMode.IfNot, 0f);
        EnsureTransition(runLoop, runStop, false, 0f, "IsMoving", AnimatorConditionMode.IfNot, 0f);
        EnsureTransition(runStop, runStart, false, 0f, "IsMoving", AnimatorConditionMode.If, 0f);
        EnsureTransition(runStop, idle, true, 0.85f, "IsCombatIdle", AnimatorConditionMode.IfNot, 0f);
        EnsureTransition(runStop, combatIdle, true, 0.85f, "IsCombatIdle", AnimatorConditionMode.If, 0f);

        EnsureReturnToIdleTransitions(dodge, idle, combatIdle);
        EnsureReturnToIdleTransitions(interrupt, idle, combatIdle);
        EnsureAnyStateTransition(sm, interrupt, "Interrupt", AnimatorConditionMode.If, 0f);

        for (int i = 0; i < WaveClips.Length; i++)
        {
            AnimatorState attack = EnsureState(sm, $"Default_Attack{i}", LoadClip(WaveClips[i]));
            attack.speed = i == 3 ? 2.5f : 3f;
            EnsureReturnToIdleTransitions(attack, idle, combatIdle);
            EnsureAnyStateAttackTransition(sm, attack, i);
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log($"{Tag} Animator 구성: Idle 2, Run Start/Loop/Stop, Dodge, Interrupt, Wave 4 상태");
    }

    private static GameObject EnsureArmature(AnimatorController controller)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (model == null)
        {
            Debug.LogError($"{Tag} 모델이 없다: {ModelPath}. SVN r383 이상인지 확인할 것.");
            return null;
        }
        if (material == null)
        {
            Debug.LogError($"{Tag} Toon 머티리얼이 없다: {MaterialPath}");
            return null;
        }

        GameObject root;
        bool created = AssetDatabase.LoadAssetAtPath<GameObject>(ArmaturePath) == null;
        if (created)
        {
            root = (GameObject)PrefabUtility.InstantiatePrefab(model);
            root.name = "Assassin_Armature";
        }
        else
        {
            root = PrefabUtility.LoadPrefabContents(ArmaturePath);
        }

        try
        {
            Animator animator = EnsureComponent<Animator>(root);
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            NetworkTransform networkTransform = EnsureComponent<NetworkTransform>(root);
            networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            networkTransform.SyncPositionX = networkTransform.SyncPositionY = networkTransform.SyncPositionZ = false;
            networkTransform.SyncScaleX = networkTransform.SyncScaleY = networkTransform.SyncScaleZ = false;

            NetworkAnimator networkAnimator = EnsureComponent<NetworkAnimator>(root);
            networkAnimator.AuthorityMode = NetworkAnimator.AuthorityModes.Owner;
            networkAnimator.Animator = animator;

            EnsureComponent<PlayerAnimationEventRelay>(root);
            EnsureComponent<EffectAnimEventRelay>(root);

            SDArmTwist twist = EnsureComponent<SDArmTwist>(root);
            twist.CaptureBindPose();
            if (!twist.left.captured || !twist.right.captured)
                Debug.LogError($"{Tag} SDArmTwist 팔 바인드 포즈 캡처 실패. FBX 본 이름(Left/RightLowerArm·Hand·ForearmTwist01/02)을 확인할 것.", root);

            SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (SkinnedMeshRenderer renderer in renderers)
                renderer.sharedMaterial = material;
            if (renderers.Length == 0)
                Debug.LogError($"{Tag} SkinnedMeshRenderer를 찾지 못해 머티리얼을 적용하지 못했다.", root);
            else if (renderers.Length != 2)
                Debug.LogWarning($"{Tag} 예상 메시 2개와 다르다({renderers.Length}개). 찾은 렌더러 모두 Assassin_Toon을 적용했다.", root);

            EnsureAnchor(root.transform, "DefaultAttack", new Vector3(0f, 1f, 0.85f), new Vector3(2f, 2f, 1.7f));
            EnsureAnchor(root.transform, "InterruptAttack", new Vector3(0f, 1f, 0.9f), new Vector3(1.4f, 1.8f, 1.4f));

            Transform vfx = EnsureChild(root.transform, "VFX");
            EnsureChild(vfx, "BasicAttack01");
            EnsureChild(vfx, "BasicAttack02");
            EnsureChild(vfx, "BasicAttack03");
            EnsureChild(vfx, "BasicAttack04");
            EnsureChild(vfx, "Transformation");
            EnsureChild(vfx, "EnhancedAttack");
            EnsureChild(vfx, "BackAttack");
            CharacterHudAuthoring.EnsureHudRoot(root.transform);

            PrefabUtility.SaveAsPrefabAsset(root, ArmaturePath);
            Debug.Log($"{Tag} Armature {(created ? "생성" : "갱신")}: {ArmaturePath} (rootMotion=false, Owner NT/NA, HUD·앵커·VFX 소켓)");
        }
        finally
        {
            if (created)
                Object.DestroyImmediate(root);
            else
                PrefabUtility.UnloadPrefabContents(root);
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(ArmaturePath);
    }

    private static GameObject EnsureVariant(GameObject armaturePrefab)
    {
        GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePrefabPath);
        if (basePrefab == null)
        {
            Debug.LogError($"{Tag} base Player 프리팹이 없다: {BasePrefabPath}");
            return null;
        }

        bool created = AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath) == null;
        GameObject root = created
            ? (GameObject)PrefabUtility.InstantiatePrefab(basePrefab)
            : PrefabUtility.LoadPrefabContents(VariantPath);
        root.name = "Player_Assassin";

        try
        {
            Transform armature = root.transform.Find("Armature");
            GameObject source = armature != null
                ? PrefabUtility.GetCorrespondingObjectFromSource(armature.gameObject)
                : null;
            if (armature == null || source != armaturePrefab)
            {
                if (armature != null)
                    Object.DestroyImmediate(armature.gameObject);

                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(armaturePrefab, root.transform);
                instance.name = "Armature";
                instance.transform.SetSiblingIndex(0);
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
                armature = instance.transform;
                Debug.Log($"{Tag} Variant의 Armature를 Assassin_Armature 중첩 인스턴스로 배치했다.");
            }

            SetReference(root.GetComponent<PlayerMovement>(), "armature", armature);
            SetReference(root.GetComponent<PlayerSoulController>(), "soulVisualRoot", armature);
            PrefabUtility.SaveAsPrefabAsset(root, VariantPath);
        }
        finally
        {
            if (created)
                Object.DestroyImmediate(root);
            else
                PrefabUtility.UnloadPrefabContents(root);
        }

        GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath);
        RecordAndValidateHash(saved);
        Debug.Log($"{Tag} Player Variant {(created ? "생성" : "갱신")}: {VariantPath}");
        return saved;
    }

    private static void EnsureRosterEntry(GameObject variant)
    {
        CharacterRoster roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>(RosterPath);
        if (roster == null)
        {
            Debug.LogError($"{Tag} CharacterRoster가 없다: {RosterPath}");
            return;
        }

        SerializedObject so = new SerializedObject(roster);
        SerializedProperty entries = so.FindProperty("entries");
        int target = -1;
        int placeholder = -1;
        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty entry = entries.GetArrayElementAtIndex(i);
            string displayName = entry.FindPropertyRelative("displayName").stringValue;
            Object prefab = entry.FindPropertyRelative("playerPrefab").objectReferenceValue;
            bool available = entry.FindPropertyRelative("available").boolValue;
            if (displayName == "어쌔신" || prefab == variant)
            {
                target = i;
                break;
            }
            if (placeholder < 0 && prefab == null && !available)
                placeholder = i;
        }

        if (target < 0)
        {
            target = placeholder;
            if (target < 0)
            {
                target = entries.arraySize;
                entries.InsertArrayElementAtIndex(target);
            }
        }

        SerializedProperty targetEntry = entries.GetArrayElementAtIndex(target);
        targetEntry.FindPropertyRelative("displayName").stringValue = "어쌔신";
        targetEntry.FindPropertyRelative("playerPrefab").objectReferenceValue = variant;
        targetEntry.FindPropertyRelative("portrait").objectReferenceValue = null;
        targetEntry.FindPropertyRelative("available").boolValue = false;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(roster);
        Debug.Log($"{Tag} CharacterRoster[{target}] = 어쌔신, available=false: {RosterPath}");
    }

    private static void RecordAndValidateHash(GameObject variant)
    {
        if (variant == null)
            return;

        NetworkObject networkObject = variant.GetComponent<NetworkObject>();
        GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePrefabPath);
        NetworkObject baseNetworkObject = basePrefab != null ? basePrefab.GetComponent<NetworkObject>() : null;
        if (networkObject == null || baseNetworkObject == null)
        {
            Debug.LogError($"{Tag} Variant/base의 NetworkObject를 찾지 못해 GlobalObjectIdHash를 검증할 수 없다.");
            return;
        }

        EditorUtility.SetDirty(networkObject);
        AssetDatabase.SaveAssetIfDirty(variant);

        uint hash = new SerializedObject(networkObject).FindProperty("GlobalObjectIdHash").uintValue;
        uint baseHash = new SerializedObject(baseNetworkObject).FindProperty("GlobalObjectIdHash").uintValue;
        if (hash == 0 || hash == baseHash)
            Debug.LogError($"{Tag} GlobalObjectIdHash 기록 실패: variant={hash}, base={baseHash}. YAML 오버라이드를 확인할 것.");
        else
            Debug.Log($"{Tag} GlobalObjectIdHash={hash} (base={baseHash}) 기록 확인.");
    }

    private static void EnsureParameter(AnimatorController controller, string name, AnimatorControllerParameterType type)
    {
        foreach (AnimatorControllerParameter parameter in controller.parameters)
        {
            if (parameter.name != name)
                continue;
            if (parameter.type != type)
                Debug.LogError($"{Tag} Animator 파라미터 {name} 타입이 다르다: {parameter.type} (기대 {type})");
            return;
        }

        controller.AddParameter(name, type);
    }

    private static AnimatorState EnsureState(AnimatorStateMachine machine, string name, Motion motion)
    {
        foreach (ChildAnimatorState child in machine.states)
        {
            if (child.state.name != name)
                continue;
            child.state.motion = motion;
            return child.state;
        }

        AnimatorState state = machine.AddState(name);
        state.motion = motion;
        return state;
    }

    private static void EnsureReturnToIdleTransitions(AnimatorState from, AnimatorState idle, AnimatorState combatIdle)
    {
        EnsureTransition(from, idle, true, 0.98f, "IsCombatIdle", AnimatorConditionMode.IfNot, 0f);
        EnsureTransition(from, combatIdle, true, 0.98f, "IsCombatIdle", AnimatorConditionMode.If, 0f);
    }

    private static void EnsureTransition(
        AnimatorState from,
        AnimatorState to,
        bool hasExitTime,
        float exitTime,
        string parameter,
        AnimatorConditionMode mode,
        float threshold)
    {
        foreach (AnimatorStateTransition existing in from.transitions)
        {
            if (existing.destinationState == to && ConditionsMatch(existing.conditions, parameter, mode, threshold))
                return;
        }

        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = hasExitTime;
        transition.exitTime = exitTime;
        transition.duration = 0.08f;
        if (!string.IsNullOrEmpty(parameter))
            transition.AddCondition(mode, threshold, parameter);
    }

    private static void EnsureAnyStateTransition(
        AnimatorStateMachine machine,
        AnimatorState to,
        string parameter,
        AnimatorConditionMode mode,
        float threshold)
    {
        foreach (AnimatorStateTransition existing in machine.anyStateTransitions)
        {
            if (existing.destinationState == to && ConditionsMatch(existing.conditions, parameter, mode, threshold))
                return;
        }

        AnimatorStateTransition transition = machine.AddAnyStateTransition(to);
        transition.hasExitTime = false;
        transition.duration = 0.05f;
        transition.canTransitionToSelf = false;
        transition.AddCondition(mode, threshold, parameter);
    }

    private static void EnsureAnyStateAttackTransition(AnimatorStateMachine machine, AnimatorState to, int attackIndex)
    {
        foreach (AnimatorStateTransition existing in machine.anyStateTransitions)
        {
            AnimatorCondition[] conditions = existing.conditions;
            if (existing.destinationState == to && conditions.Length == 2 &&
                HasCondition(conditions, "DefaultAttack", AnimatorConditionMode.If, 0f) &&
                HasCondition(conditions, "AttackIndex", AnimatorConditionMode.Equals, attackIndex))
                return;
        }

        AnimatorStateTransition transition = machine.AddAnyStateTransition(to);
        transition.hasExitTime = false;
        transition.duration = 0.04f;
        transition.canTransitionToSelf = false;
        transition.AddCondition(AnimatorConditionMode.If, 0f, "DefaultAttack");
        transition.AddCondition(AnimatorConditionMode.Equals, attackIndex, "AttackIndex");
    }

    private static bool ConditionsMatch(
        AnimatorCondition[] conditions,
        string parameter,
        AnimatorConditionMode mode,
        float threshold)
    {
        if (string.IsNullOrEmpty(parameter))
            return conditions.Length == 0;
        return conditions.Length == 1 && HasCondition(conditions, parameter, mode, threshold);
    }

    private static bool HasCondition(
        AnimatorCondition[] conditions,
        string parameter,
        AnimatorConditionMode mode,
        float threshold)
    {
        foreach (AnimatorCondition condition in conditions)
        {
            if (condition.parameter == parameter && condition.mode == mode &&
                Mathf.Approximately(condition.threshold, threshold))
                return true;
        }
        return false;
    }

    private static Transform EnsureAnchor(Transform root, string name, Vector3 center, Vector3 size)
    {
        Transform anchor = EnsureChild(root, name);
        BoxCollider box = anchor.GetComponent<BoxCollider>();
        if (box == null)
            box = anchor.gameObject.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.center = center;
        box.size = size;
        if (anchor.GetComponent<ColliderInfo>() == null)
            anchor.gameObject.AddComponent<ColliderInfo>();
        return anchor;
    }

    private static Transform EnsureChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null)
            return child;

        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private static T EnsureComponent<T>(GameObject root) where T : Component
    {
        T component = root.GetComponent<T>();
        return component != null ? component : root.AddComponent<T>();
    }

    private static void SetReference(Object component, string propertyName, Object value)
    {
        if (component == null)
        {
            Debug.LogError($"{Tag} {propertyName} 배선 대상 컴포넌트가 없다.");
            return;
        }

        SerializedObject so = new SerializedObject(component);
        SerializedProperty property = so.FindProperty(propertyName);
        if (property == null)
        {
            Debug.LogError($"{Tag} {component.GetType().Name}.{propertyName} 직렬화 필드가 없다.");
            return;
        }

        property.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static AnimationClip LoadClip(string name)
    {
        string path = $"{AnimationFolder}/{name}.anim";
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
            Debug.LogError($"{Tag} 애니메이션 클립이 없다: {path}");
        return clip;
    }

    private static AnimationEventSpec DefaultAttackEvent(float normalizedTime, DefaultAttackAnimationEventType type)
    {
        return new AnimationEventSpec(normalizedTime, nameof(PlayerAnimationEventRelay.HandleDefaultAttackEvent), (int)type);
    }

    private static AnimationEventSpec SkillEvent(float normalizedTime, SkillAnimationEventType type)
    {
        return new AnimationEventSpec(normalizedTime, nameof(PlayerAnimationEventRelay.HandleSkillEvent), (int)type);
    }

    private static void ReplaceEvents(string clipName, params AnimationEventSpec[] specs)
    {
        AnimationClip clip = LoadClip(clipName);
        if (clip == null)
            return;

        AnimationEvent[] events = new AnimationEvent[specs.Length];
        for (int i = 0; i < specs.Length; i++)
        {
            events[i] = new AnimationEvent
            {
                time = Mathf.Clamp01(specs[i].NormalizedTime) * clip.length,
                functionName = specs[i].FunctionName,
                intParameter = specs[i].IntParameter,
            };
        }

        AnimationUtility.SetAnimationEvents(clip, events);
        EditorUtility.SetDirty(clip);
        Debug.Log($"{Tag} 이벤트 교체: {clipName} ({events.Length}개, 누적 안 함)");
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        Debug.Log($"{Tag} 폴더 생성: {path}");
    }

    private readonly struct AnimationEventSpec
    {
        public readonly float NormalizedTime;
        public readonly string FunctionName;
        public readonly int IntParameter;

        public AnimationEventSpec(float normalizedTime, string functionName, int intParameter)
        {
            NormalizedTime = normalizedTime;
            FunctionName = functionName;
            IntParameter = intParameter;
        }
    }
}
