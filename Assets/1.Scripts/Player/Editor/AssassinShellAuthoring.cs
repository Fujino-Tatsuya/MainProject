using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// PLAN-assassin A6~A8 저작 메뉴. 모델/클립 원본은 건드리지 않고, 재실행 가능한 방식으로 컨트롤러·Armature·Variant·데이터 배선을 맞춘다.
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
    private const string StateDataPath = DataFolder + "/AssassinStateData.asset";
    private const string EnhanceSkillDataPath = DataFolder + "/AssassinEnhanceSkillData.asset";
    private const string TransformSkillDataPath = DataFolder + "/AssassinTransformSkillData.asset";
    private const string DashStrikeDataPath = DataFolder + "/AssassinDashStrikeSkillData.asset";
    private const string TransformedDashStrikeDataPath = DataFolder + "/AssassinTransformedDashStrikeSkillData.asset";
    private const string RosterPath = "Assets/9.ScriptableObject/Player/CharacterRoster.asset";

    // A7 Animator 상태 — 스킬 데이터 animatorStateName 과 같은 이름이어야 CrossFade 가 맞는다.
    private const string EnhancedAttackState = "Default_Attack_Enhanced";
    private const string TransformedAttackState = "Default_Attack_Transformed";
    private const string EnhanceSkillState = "Assassin_E_Buff";
    private const string TransformSkillState = "Assassin_R_Transform";
    private const float EnhanceBuffSpeed = 3f;   // §8.1 Buff 3배속 ≈ 0.5초
    private const float TransformParrySpeed = 1f; // 🔸 기획 수치 없음 — Play 튜닝

    // A8 Q 관통 돌진. 실제 이동은 스킬(0.2초)이 하고 클립은 모양만 — 재생 속도는 기획 수치가 없어
    // 두 Q 의 행동 종료 시점을 맞추는 값으로 둔다(Combo_Attack_03_04 1.92s ÷ 1.5 ≈ Combo_Attack_02_04 1.28s). 🔸 Play 튜닝.
    private const string DashStrikeSkillState = "Assassin_Q_DashStrike";
    private const string TransformedDashStrikeSkillState = "Assassin_Q_DashStrike_Transformed";
    private const float DashStrikeSpeed = 1f;
    private const float TransformedDashStrikeSpeed = 1.5f;
    private const int EnemyHittableLayers = 17664; // Enemy·Projectile·EnemyHurtBox — 가붕이 Q·어쌔신 평타와 같다

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

    [MenuItem("Tools/Player/Assassin/전체 구성 (A6~A8)")]
    public static void BuildAll()
    {
        BuildShell();
        AttachBasicAttackAndIdle();
        StampClipEventsAndLoopSettings();
        AttachStateAndSkills();
        AttachDashStrikeSkills();
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

    /// <summary>
    /// A7 — AssassinState·일반 E 강화·R 변신 부착, 데이터 SO 생성, PlayerSkillController Sub/Ultimate 배선, Animator 상태.
    /// 데이터는 처음 만들 때만 기본값을 쓰고 재실행 시 튜닝 값을 유지한다.
    /// </summary>
    [MenuItem("Tools/Player/Assassin/4. 상태·E 강화·R 변신 부착 + 데이터 (A7)")]
    public static void AttachStateAndSkills()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath) == null)
        {
            Debug.LogError($"{Tag} Variant가 없다. 먼저 1번 메뉴를 실행할 것: {VariantPath}");
            return;
        }

        EnsureFolder(DataFolder);
        AssassinStateData stateData = EnsureAsset<AssassinStateData>(StateDataPath, out _);

        AssassinEnhanceSkillData enhanceData = EnsureAsset<AssassinEnhanceSkillData>(EnhanceSkillDataPath, out bool enhanceCreated);
        if (enhanceCreated)
        {
            AnimationClip buff = LoadClip("Buff");
            InitializeSkillData(enhanceData, cooldown: 6f, commitManually: true,
                maxActiveDuration: ClipDuration(buff, EnhanceBuffSpeed) + 0.3f,
                animatorStateName: EnhanceSkillState, snapRotation: false);
        }

        AssassinTransformSkillData transformData = EnsureAsset<AssassinTransformSkillData>(TransformSkillDataPath, out bool transformCreated);
        if (transformCreated)
        {
            AnimationClip parry = LoadClip("Parry_R");
            InitializeSkillData(transformData, cooldown: 8f, commitManually: true,
                maxActiveDuration: ClipDuration(parry, TransformParrySpeed) + 0.3f,
                animatorStateName: TransformSkillState, snapRotation: false);
        }

        GameObject root = PrefabUtility.LoadPrefabContents(VariantPath);
        try
        {
            AssassinState state = EnsureComponent<AssassinState>(root);
            AssassinEnhanceSkill enhance = EnsureComponent<AssassinEnhanceSkill>(root);
            AssassinTransformSkill transform = EnsureComponent<AssassinTransformSkill>(root);

            SetReference(state, "data", stateData);
            SetReference(enhance, "data", enhanceData);
            SetReference(transform, "data", transformData);

            PlayerSkillController skills = root.GetComponent<PlayerSkillController>();
            SetReference(skills, "subSkill", enhance);
            SetReference(skills, "ultimateSkill", transform);

            PrefabUtility.SaveAsPrefabAsset(root, VariantPath);
            Debug.Log($"{Tag} A7 상태·E 강화·R 변신 부착/갱신: {VariantPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        // 강타·변신 묶음 스텝 데이터와 Animator 상태를 같은 메뉴에서 맞춘다(둘 다 재실행 안전).
        EnsureBasicAttackData();
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller != null)
            BuildAnimator(controller);
        else
            Debug.LogError($"{Tag} Animator Controller가 없다. 먼저 1번 메뉴를 실행할 것: {ControllerPath}");

        RecordAndValidateHash(AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath));
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// A8 — Q 관통 돌진 일반/변신 부착(같은 컴포넌트 2개, 데이터로 구분), 데이터 SO 2개, mainSkill·alternateSkills[Main] 배선, Animator 상태.
    /// 데이터는 처음 만들 때만 기본값을 쓰고 재실행 시 튜닝 값을 유지한다. 컴포넌트는 데이터 참조로 찾아 다시 쓴다.
    /// </summary>
    [MenuItem("Tools/Player/Assassin/5. Q 관통 돌진 부착 + 데이터 (A8)")]
    public static void AttachDashStrikeSkills()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath) == null)
        {
            Debug.LogError($"{Tag} Variant가 없다. 먼저 1번 메뉴를 실행할 것: {VariantPath}");
            return;
        }

        EnsureFolder(DataFolder);
        AssassinDashStrikeSkillData normalData =
            EnsureAsset<AssassinDashStrikeSkillData>(DashStrikeDataPath, out bool normalCreated);
        if (normalCreated)
            InitializeDashStrikeData(normalData, 5f, "Combo_Attack_02_04", DashStrikeSpeed, DashStrikeSkillState);

        AssassinTransformedDashStrikeSkillData transformedData =
            EnsureAsset<AssassinTransformedDashStrikeSkillData>(TransformedDashStrikeDataPath, out bool transformedCreated);
        if (transformedCreated)
            InitializeDashStrikeData(transformedData, 3f, "Combo_Attack_03_04", TransformedDashStrikeSpeed, TransformedDashStrikeSkillState);

        GameObject root = PrefabUtility.LoadPrefabContents(VariantPath);
        try
        {
            List<AssassinDashStrikeSkill> dashes = new List<AssassinDashStrikeSkill>(root.GetComponents<AssassinDashStrikeSkill>());
            AssassinDashStrikeSkill normal = TakeDashWithData(dashes, normalData);
            AssassinDashStrikeSkill transformed = TakeDashWithData(dashes, transformedData);
            normal ??= TakeAnyDash(dashes) ?? root.AddComponent<AssassinDashStrikeSkill>();
            transformed ??= TakeAnyDash(dashes) ?? root.AddComponent<AssassinDashStrikeSkill>();
            foreach (AssassinDashStrikeSkill extra in dashes)
            {
                Debug.LogWarning($"{Tag} 남는 AssassinDashStrikeSkill 제거.", root);
                Object.DestroyImmediate(extra, true);
            }

            SetReference(normal, "data", normalData);
            SetReference(transformed, "data", transformedData);

            PlayerSkillController skills = root.GetComponent<PlayerSkillController>();
            SetReference(skills, "mainSkill", normal);
            SetReference(skills, "alternateSkills.mainSkill", transformed);

            PrefabUtility.SaveAsPrefabAsset(root, VariantPath);
            Debug.Log($"{Tag} A8 Q 관통 돌진 부착/갱신: mainSkill=일반, alternateSkills.mainSkill=변신 ({VariantPath})");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller != null)
        {
            BuildAnimator(controller);
            ValidateSkillState(controller, normalData);
            ValidateSkillState(controller, transformedData);
        }
        else
        {
            Debug.LogError($"{Tag} Animator Controller가 없다. 먼저 1번 메뉴를 실행할 것: {ControllerPath}");
        }

        RecordAndValidateHash(AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath));
        AssetDatabase.SaveAssets();
    }

    // 컨트롤러는 animatorStateName 의 해시로 CrossFade 한다 — 같은 해시의 상태가 0층에 있어야 한다.
    private static void ValidateSkillState(AnimatorController controller, PlayerSkillData data)
    {
        if (data == null)
            return;

        int hash = Animator.StringToHash(data.AnimatorStateName);
        foreach (ChildAnimatorState child in controller.layers[0].stateMachine.states)
        {
            if (child.state.nameHash == hash)
            {
                Debug.Log($"{Tag} {data.name} → Animator 상태 '{data.AnimatorStateName}' 해시 {hash} 확인.");
                return;
            }
        }

        Debug.LogError($"{Tag} {data.name} 의 animatorStateName '{data.AnimatorStateName}' 상태가 컨트롤러에 없다.");
    }

    private static void InitializeDashStrikeData(
        AssassinDashStrikeSkillData data, float cooldown, string clipName, float speed, string animatorStateName)
    {
        // 쿨타임 = 돌진 시작(서버 승인) — 자동 커밋. 회전은 입력 방향으로 즉시 맞춘다.
        InitializeSkillData(data, cooldown, commitManually: false,
            maxActiveDuration: ClipDuration(LoadClip(clipName), speed) + 0.3f,
            animatorStateName: animatorStateName, snapRotation: true);

        SerializedObject so = new SerializedObject(data);
        so.FindProperty("attackDamageMultiplier").floatValue = 1.5f;
        so.FindProperty("hittableLayers").intValue = EnemyHittableLayers;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(data);
    }

    private static AssassinDashStrikeSkill TakeDashWithData(List<AssassinDashStrikeSkill> dashes, PlayerSkillData data)
    {
        AssassinDashStrikeSkill found = dashes.Find(d => d.Data == data);
        if (found != null)
            dashes.Remove(found);
        return found;
    }

    private static AssassinDashStrikeSkill TakeAnyDash(List<AssassinDashStrikeSkill> dashes)
    {
        if (dashes.Count == 0)
            return null;
        AssassinDashStrikeSkill first = dashes[0];
        dashes.RemoveAt(0);
        return first;
    }

    private static void InitializeSkillData(
        PlayerSkillData data, float cooldown, bool commitManually, float maxActiveDuration,
        string animatorStateName, bool snapRotation)
    {
        SerializedObject so = new SerializedObject(data);
        so.FindProperty("cooldownTime").floatValue = cooldown;
        so.FindProperty("commitCooldownManually").boolValue = commitManually;
        so.FindProperty("maxActiveDuration").floatValue = Mathf.Max(0.1f, maxActiveDuration);
        so.FindProperty("animatorStateName").stringValue = animatorStateName;
        so.FindProperty("snapRotationOnStart").boolValue = snapRotation;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(data);
        Debug.Log($"{Tag} 스킬 데이터 기본값: {data.name} (쿨 {cooldown}s, 수동 커밋 {commitManually}, 안전망 {maxActiveDuration:F2}s, 상태 {animatorStateName})");
    }

    private static float ClipDuration(AnimationClip clip, float speed) =>
        clip != null ? clip.length / Mathf.Max(0.01f, speed) : 1f;

    private static T EnsureAsset<T>(string path, out bool created) where T : ScriptableObject
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        created = asset == null;
        if (!created)
            return asset;

        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        Debug.Log($"{Tag} 데이터 생성: {path}");
        return asset;
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

            // A7 에서 생긴 필드 — 기존 에셋은 0 으로 읽히므로 1타로 채운다.
            SerializedProperty hitCount = step.FindPropertyRelative("hitCount");
            if (hitCount.intValue < 1)
                hitCount.intValue = 1;

            if (!created)
                continue;

            step.FindPropertyRelative("attackDamageMultiplier").floatValue = multipliers[i];
            step.FindPropertyRelative("range").floatValue = ranges[i];
            step.FindPropertyRelative("angle").floatValue = angles[i];
            step.FindPropertyRelative("playbackSpeed").floatValue = speeds[i];
        }

        // A7: 클립이 비어 있을 때(= 처음 채울 때)만 기본 수치까지 쓴다. 이후 재실행은 튜닝 값을 유지한다.
        EnsureSpecialStep(so.FindProperty("enhancedStep"), "Combo_Attack_02_01", 2.5f, 1.8f, 120f, 2f, 1);
        EnsureSpecialStep(so.FindProperty("transformedStep"), "Speed_Attack_Loop", 1.2f, 1.8f, 120f, 1.25f, 4);

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(data);
        return data;
    }

    private static void EnsureSpecialStep(
        SerializedProperty step, string clipName, float multiplier, float range, float angle, float speed, int hitCount)
    {
        if (step == null)
        {
            Debug.LogError($"{Tag} AssassinBasicAttackData 에서 {clipName} 스텝 필드를 찾지 못했다.");
            return;
        }

        SerializedProperty clip = step.FindPropertyRelative("clip");
        if (clip.objectReferenceValue != null)
            return;

        clip.objectReferenceValue = LoadClip(clipName);
        step.FindPropertyRelative("attackDamageMultiplier").floatValue = multiplier;
        step.FindPropertyRelative("range").floatValue = range;
        step.FindPropertyRelative("angle").floatValue = angle;
        step.FindPropertyRelative("playbackSpeed").floatValue = speed;
        step.FindPropertyRelative("hitCount").intValue = hitCount;
        Debug.Log($"{Tag} 평타 스텝 초기화: {step.name} = {clipName} (×{multiplier}, {range}m, {angle}°, {speed}배속, {hitCount}타)");
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

        // A7: 강타·변신 묶음(AttackIndex 4·5) — 재생 속도는 평타 데이터 값을 따른다.
        AssassinBasicAttackData attackData = AssetDatabase.LoadAssetAtPath<AssassinBasicAttackData>(BasicAttackDataPath);
        AnimatorState enhancedAttack = EnsureState(sm, EnhancedAttackState, LoadClip("Combo_Attack_02_01"));
        enhancedAttack.speed = attackData != null && attackData.EnhancedStep != null ? attackData.EnhancedStep.PlaybackSpeed : 2f;
        EnsureReturnToIdleTransitions(enhancedAttack, idle, combatIdle);
        EnsureAnyStateAttackTransition(sm, enhancedAttack, AssassinBasicAttack.EnhancedAnimatorIndex);

        AnimatorState transformedAttack = EnsureState(sm, TransformedAttackState, LoadClip("Speed_Attack_Loop"));
        transformedAttack.speed = attackData != null && attackData.TransformedStep != null ? attackData.TransformedStep.PlaybackSpeed : 1.25f;
        EnsureReturnToIdleTransitions(transformedAttack, idle, combatIdle);
        // 유지 입력으로 묶음을 반복하면 같은 상태로 다시 들어가야 한다.
        EnsureAnyStateAttackTransition(sm, transformedAttack, AssassinBasicAttack.TransformedAnimatorIndex, canTransitionToSelf: true);

        // A7: 스킬 상태는 PlayerSkillController 가 animatorStateName 으로 CrossFade 한다(AnyState 전이 불필요).
        AnimatorState enhanceSkill = EnsureState(sm, EnhanceSkillState, LoadClip("Buff"));
        enhanceSkill.speed = EnhanceBuffSpeed;
        EnsureReturnToIdleTransitions(enhanceSkill, idle, combatIdle);

        AnimatorState transformSkill = EnsureState(sm, TransformSkillState, LoadClip("Parry_R"));
        transformSkill.speed = TransformParrySpeed;
        EnsureReturnToIdleTransitions(transformSkill, idle, combatIdle);

        // A8: Q 관통 돌진 일반/변신 — 이동은 스킬이 하므로 클립은 모양만(루트모션 꺼짐).
        AnimatorState dashStrike = EnsureState(sm, DashStrikeSkillState, LoadClip("Combo_Attack_02_04"));
        dashStrike.speed = DashStrikeSpeed;
        EnsureReturnToIdleTransitions(dashStrike, idle, combatIdle);

        AnimatorState transformedDashStrike = EnsureState(sm, TransformedDashStrikeSkillState, LoadClip("Combo_Attack_03_04"));
        transformedDashStrike.speed = TransformedDashStrikeSpeed;
        EnsureReturnToIdleTransitions(transformedDashStrike, idle, combatIdle);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log($"{Tag} Animator 구성: Idle 2, Run Start/Loop/Stop, Dodge, Interrupt, Wave 4, 강타·변신 묶음, E Buff·R Parry·Q 돌진 2 상태");
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

    private static void EnsureAnyStateAttackTransition(
        AnimatorStateMachine machine, AnimatorState to, int attackIndex, bool canTransitionToSelf = false)
    {
        foreach (AnimatorStateTransition existing in machine.anyStateTransitions)
        {
            AnimatorCondition[] conditions = existing.conditions;
            if (existing.destinationState == to && conditions.Length == 2 &&
                HasCondition(conditions, "DefaultAttack", AnimatorConditionMode.If, 0f) &&
                HasCondition(conditions, "AttackIndex", AnimatorConditionMode.Equals, attackIndex))
            {
                existing.canTransitionToSelf = canTransitionToSelf;
                return;
            }
        }

        AnimatorStateTransition transition = machine.AddAnyStateTransition(to);
        transition.hasExitTime = false;
        transition.duration = 0.04f;
        transition.canTransitionToSelf = canTransitionToSelf;
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
