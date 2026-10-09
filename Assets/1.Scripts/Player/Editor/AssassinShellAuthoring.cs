using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// PLAN-assassin A6~A12 저작 메뉴. 모델/클립 원본은 건드리지 않고, 재실행 가능한 방식으로 컨트롤러·Armature·Variant·데이터 배선을 맞춘다.
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
    private const string FaceMaterialPath = "Assets/3.Materials/Toon/Assassin_Face_Toon.mat";
    // 단검(10-06 은희 추가, SVN) — 모델에 무기 본이 없어 Humanoid 손 본에 정적으로 붙인다.
    // 클립의 Dagger_Weapon_* 커브는 원본(UE) 경로라 이 리그에 바인딩되지 않는다 — 손을 따라가기만 한다.
    private const string DaggerLeftPath = "Assets/50.Art/Char/assassin/Dagger_Weapon_L.fbx";
    private const string DaggerRightPath = "Assets/50.Art/Char/assassin/Dagger_Weapon_R.fbx";
    private const string AnimationFolder = "Assets/4.Animations/Player/Assassin";
    private const string ControllerPath = AnimationFolder + "/AssassinAnimatorController.controller";
    private const string UpperBodyMaskPath = AnimationFolder + "/AssassinUpperBody.mask";
    private const string DataFolder = "Assets/9.ScriptableObject/Player/Assassin";
    private const string BasicAttackDataPath = DataFolder + "/AssassinBasicAttackData.asset";
    private const string StateDataPath = DataFolder + "/AssassinStateData.asset";
    private const string EnhanceSkillDataPath = DataFolder + "/AssassinEnhanceSkillData.asset";
    private const string TransformSkillDataPath = DataFolder + "/AssassinTransformSkillData.asset";
    private const string DashStrikeDataPath = DataFolder + "/AssassinDashStrikeSkillData.asset";
    private const string TransformedDashStrikeDataPath = DataFolder + "/AssassinTransformedDashStrikeSkillData.asset";
    private const string CircleStrikeDataPath = DataFolder + "/AssassinCircleStrikeSkillData.asset";
    private const string InterruptDataPath = DataFolder + "/AssassinInterruptSkillData.asset";
    // A10 간파 수치 원본 — 암살자 전용 수치 없음(§11). 처음 만들 때만 값 복사.
    private const string PaladinInterruptDataPath = "Assets/9.ScriptableObject/Player/Garen/FirstMeleeInterruptSkillData.asset";
    private const string InterruptAnchorName = "InterruptAttack";

    // A12 HUD·임시 VFX. HUD 프리팹은 없을 때만 만든다(정식 UI 로 교체되면 그대로 둔다).
    private const string HudPrefabPath = PrefabFolder + "/AssassinHUD.prefab";
    private const string HudInstanceName = "AssassinHUD";
    private const string KrFontPath = "Assets/Resources/NotoSansKR-VariableFont_wght SDF.asset";
    // 🔸 임시 VFX — 프로젝트 공용 파티클. 민경 정식 VFX 로 교체 대상(필드가 비어 있을 때만 채우므로 교체값은 재실행에도 유지).
    private const string TransformLoopVfxPath = "Assets/50.Art/VFX/Common/Combat/CharacterCircle/CharacterCirclePurple.prefab";
    private const string EnhancedReadyVfxPath = "Assets/50.Art/VFX/Common/Combat/CharacterCircle/CharacterCircleYellow.prefab";
    private const string DashTrailVfxPath = "Assets/50.Art/VFX/Common/Boss/Dash/FX_Dash_Trail.prefab";
    private const string CircleStrikeVfxPath = "Assets/50.Art/VFX/Common/Burst/Burst_rings.prefab";
    private const string BackAttackVfxPath = "Assets/50.Art/VFX/Common/Burst/Burst_sharp.prefab";
    // 평타·시전 임시 VFX(10-06 은희 요청) — 가붕이 평타 베기·공용 연출 재사용. 민경 교체 대상.
    private const string PlayerSlashFolder = "Assets/50.Art/VFX/Common/Players/Player1/DefaultAttack/";
    private static readonly string[] NormalSlashVfxPaths =
    {
        PlayerSlashFolder + "FX_SingleSlash_O.prefab",
        PlayerSlashFolder + "FX_SingleSlash_X.prefab",
        PlayerSlashFolder + "FX_SingleSlash_O.prefab",
        PlayerSlashFolder + "FX_DoubleSlash_X.prefab",
    };
    private const string EnhancedSlashVfxPath = PlayerSlashFolder + "FX_DoubleSlash_O.prefab";
    private const string TransformedSlashVfxPath = "Assets/50.Art/VFX/Common/Monsters/FX_Mob_Slash.prefab";
    private const string EnhanceCastVfxPath = "Assets/50.Art/VFX/Common/Burst/Flash_star.prefab";
    private const string TransformCastVfxPath = "Assets/50.Art/VFX/Common/Burst/Poof_electric.prefab";
    private const string InterruptCastVfxPath = "Assets/50.Art/VFX/Common/FX_Interrupt_Flash.prefab";
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

    // A9 변신 E 원형 5타 — Skill_02_Move_000pct 1.5배속(≈1.22초, §9.3). 모델 이동은 연출(루트모션 꺼짐).
    private const string CircleStrikeSkillState = "Assassin_E_CircleStrike_Transformed";
    private const float CircleStrikeSpeed = 1.5f;
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

    [MenuItem("Tools/Player/Assassin/전체 구성 (A6~A12)")]
    public static void BuildAll()
    {
        BuildShell();
        AttachBasicAttackAndIdle();
        StampClipEventsAndLoopSettings();
        AttachStateAndSkills();
        AttachDashStrikeSkills();
        AttachCircleStrikeSkill();
        AttachInterruptSkill();
        AttachHudVfxAndTooltips();
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
        // ComboWindowOpen 은 대시 취소 경계로만 쓴다(그 전까지만 대시로 끊김, 할 일 9) — 다음 타 연결은 End 시점 홀드 그대로.
        foreach (string clipName in WaveClips)
            ReplaceEvents(clipName, DefaultAttackEvent(0.40f, DefaultAttackAnimationEventType.Hit),
                DefaultAttackEvent(0.60f, DefaultAttackAnimationEventType.ComboWindowOpen),
                DefaultAttackEvent(0.85f, DefaultAttackAnimationEventType.End));

        // A7 연결 자리까지 같은 메뉴가 준비한다. 실제 타격 프레임은 Play에서 튜닝한다.
        ReplaceEvents("Combo_Attack_02_01",
            DefaultAttackEvent(0.40f, DefaultAttackAnimationEventType.Hit),
            DefaultAttackEvent(0.60f, DefaultAttackAnimationEventType.ComboWindowOpen),
            DefaultAttackEvent(0.85f, DefaultAttackAnimationEventType.End));
        // 변신 묶음은 마지막 Hit(0.75) 뒤에 창을 연다 — 4타 묶음 전체가 대시로 끊길 수 있다.
        ReplaceEvents("Speed_Attack_Loop",
            DefaultAttackEvent(0.15f, DefaultAttackAnimationEventType.Hit),
            DefaultAttackEvent(0.35f, DefaultAttackAnimationEventType.Hit),
            DefaultAttackEvent(0.55f, DefaultAttackAnimationEventType.Hit),
            DefaultAttackEvent(0.75f, DefaultAttackAnimationEventType.Hit),
            DefaultAttackEvent(0.80f, DefaultAttackAnimationEventType.ComboWindowOpen),
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

    /// <summary>
    /// A9 — 변신 E 원형 5타 부착, 데이터 SO, alternateSkills[Sub] 배선, Animator 상태.
    /// 데이터는 처음 만들 때만 기본값을 쓰고 재실행 시 튜닝 값을 유지한다. 클립 Hit 5·End 이벤트는 3번 메뉴가 심는다.
    /// </summary>
    [MenuItem("Tools/Player/Assassin/6. 변신 E 부착 + 데이터 (A9)")]
    public static void AttachCircleStrikeSkill()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath) == null)
        {
            Debug.LogError($"{Tag} Variant가 없다. 먼저 1번 메뉴를 실행할 것: {VariantPath}");
            return;
        }

        EnsureFolder(DataFolder);
        AssassinCircleStrikeSkillData data =
            EnsureAsset<AssassinCircleStrikeSkillData>(CircleStrikeDataPath, out bool created);
        if (created)
            InitializeCircleStrikeData(data);

        GameObject root = PrefabUtility.LoadPrefabContents(VariantPath);
        try
        {
            AssassinCircleStrikeSkill skill = EnsureComponent<AssassinCircleStrikeSkill>(root);
            SetReference(skill, "data", data);

            PlayerSkillController skills = root.GetComponent<PlayerSkillController>();
            SetReference(skills, "alternateSkills.subSkill", skill);

            PrefabUtility.SaveAsPrefabAsset(root, VariantPath);
            Debug.Log($"{Tag} A9 변신 E 부착/갱신: alternateSkills.subSkill=원형 5타 ({VariantPath})");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller != null)
        {
            BuildAnimator(controller);
            ValidateSkillState(controller, data);
        }
        else
        {
            Debug.LogError($"{Tag} Animator Controller가 없다. 먼저 1번 메뉴를 실행할 것: {ControllerPath}");
        }

        RecordAndValidateHash(AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath));
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// A10 — 우클릭 간파 부착, 데이터 SO(가붕이 간파 값 복사), interruptSkill 배선, 앵커 = Armature/InterruptAttack.
    /// 일반·변신 공통이라 대체 세트는 없다. Animator "Interrupt" 상태(Attack_Up_01)는 1번 메뉴가, Hit/End 이벤트는 3번 메뉴가 만든다.
    /// 데이터는 처음 만들 때만 복사하고 재실행 시 튜닝 값을 유지한다.
    /// </summary>
    [MenuItem("Tools/Player/Assassin/7. 간파 부착 + 데이터 (A10)")]
    public static void AttachInterruptSkill()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath) == null)
        {
            Debug.LogError($"{Tag} Variant가 없다. 먼저 1번 메뉴를 실행할 것: {VariantPath}");
            return;
        }

        EnsureFolder(DataFolder);
        AssassinInterruptSkillData data = EnsureAsset<AssassinInterruptSkillData>(InterruptDataPath, out bool created);
        if (created)
            CopyInterruptValuesFromPaladin(data);

        GameObject root = PrefabUtility.LoadPrefabContents(VariantPath);
        try
        {
            AssassinInterruptSkill skill = EnsureComponent<AssassinInterruptSkill>(root);
            SetReference(skill, "data", data);

            Transform anchor = root.transform.Find("Armature/" + InterruptAnchorName);
            ColliderInfo anchorInfo = anchor != null ? anchor.GetComponent<ColliderInfo>() : null;
            if (anchorInfo == null)
                Debug.LogError($"{Tag} Armature/{InterruptAnchorName} 앵커(ColliderInfo)를 찾지 못했다 — 1번 메뉴를 먼저 실행할 것.");
            else
                SetReference(skill, "hitboxAnchor", anchorInfo);

            PlayerSkillController skills = root.GetComponent<PlayerSkillController>();
            SetReference(skills, "interruptSkill", skill);

            PrefabUtility.SaveAsPrefabAsset(root, VariantPath);
            Debug.Log($"{Tag} A10 간파 부착/갱신: interruptSkill=AssassinInterruptSkill, 앵커 {InterruptAnchorName} ({VariantPath})");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller != null)
        {
            BuildAnimator(controller);
            ValidateSkillState(controller, data);
        }
        else
        {
            Debug.LogError($"{Tag} Animator Controller가 없다. 먼저 1번 메뉴를 실행할 것: {ControllerPath}");
        }

        RecordAndValidateHash(AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath));
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// A12 — 어쌔신 HUD 프리팹(없으면 생성)을 <c>Assassin_Armature/HUD</c> 에 중첩, Variant 루트에 AssassinSkillView 부착·임시 VFX 배선,
    /// 어쌔신 스킬·패시브 SO 툴팁 임시 문구(비어 있을 때만). 공용 CombatHUD 는 건드리지 않는다. 재실행해도 결과가 같다.
    /// </summary>
    [MenuItem("Tools/Player/Assassin/8. HUD·VFX·툴팁 부착 (A12)")]
    public static void AttachHudVfxAndTooltips()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath) == null ||
            AssetDatabase.LoadAssetAtPath<GameObject>(ArmaturePath) == null)
        {
            Debug.LogError($"{Tag} Variant/Armature가 없다. 먼저 1번 메뉴를 실행할 것: {VariantPath}");
            return;
        }

        GameObject hudPrefab = EnsureHudPrefab();
        if (hudPrefab == null)
            return;

        GameObject armature = PrefabUtility.LoadPrefabContents(ArmaturePath);
        try
        {
            Transform hud = CharacterHudAuthoring.EnsureHudRoot(armature.transform);
            if (hud.Find(HudInstanceName) == null)
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(hudPrefab, hud);
                instance.name = HudInstanceName;
            }

            PrefabUtility.SaveAsPrefabAsset(armature, ArmaturePath);
            Debug.Log($"{Tag} A12 HUD 중첩: {ArmaturePath}/{CharacterHudAuthoring.HudRootName}/{HudInstanceName}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(armature);
        }

        GameObject root = PrefabUtility.LoadPrefabContents(VariantPath);
        try
        {
            AssassinSkillView view = EnsureComponent<AssassinSkillView>(root);
            SerializedObject so = new SerializedObject(view);
            SetIfEmpty(so, "transformLoopPrefab", TransformLoopVfxPath);
            SetIfEmpty(so, "enhancedReadyLoopPrefab", EnhancedReadyVfxPath);
            SetIfEmpty(so, "dashTrailPrefab", DashTrailVfxPath);
            SetIfEmpty(so, "circleStrikePrefab", CircleStrikeVfxPath);
            SetIfEmpty(so, "backAttackHitPrefab", BackAttackVfxPath);
            SetArrayIfEmpty(so, "normalSlashPrefabs", NormalSlashVfxPaths);
            SetIfEmpty(so, "enhancedSlashPrefab", EnhancedSlashVfxPath);
            SetIfEmpty(so, "transformedSlashPrefab", TransformedSlashVfxPath);
            SetIfEmpty(so, "enhanceCastPrefab", EnhanceCastVfxPath);
            SetIfEmpty(so, "transformCastPrefab", TransformCastVfxPath);
            SetIfEmpty(so, "interruptCastPrefab", InterruptCastVfxPath);
            so.FindProperty("transformSocket").objectReferenceValue = root.transform.Find("Armature/VFX/Transformation");
            // 강화 준비 루프는 강타 베기 소켓(EnhancedAttack)과 분리 — 베기 위치를 옮겨도 발밑 원은 그대로.
            so.FindProperty("enhancedSocket").objectReferenceValue = root.transform.Find("Armature/VFX/EnhancedReady");
            SerializedProperty slashSockets = so.FindProperty("normalSlashSockets");
            slashSockets.arraySize = 4;
            for (int i = 0; i < 4; i++)
                slashSockets.GetArrayElementAtIndex(i).objectReferenceValue = root.transform.Find($"Armature/VFX/BasicAttack0{i + 1}");
            so.FindProperty("enhancedSlashSocket").objectReferenceValue = root.transform.Find("Armature/VFX/EnhancedAttack");
            so.FindProperty("transformedSlashSocket").objectReferenceValue = root.transform.Find("Armature/VFX/TransformedAttack");
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, VariantPath);
            Debug.Log($"{Tag} A12 AssassinSkillView 부착/갱신(임시 VFX — 민경 교체 대상): {VariantPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        // CombatHUD 를 건드리는 '스킬 툴팁 구성' 메뉴 대신 SO 문구만 채운다.
        SkillTooltipAuthoring.SeedAssassinTooltips();

        RecordAndValidateHash(AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath));
        AssetDatabase.SaveAssets();
    }

    private static void SetIfEmpty(SerializedObject so, string propertyName, string assetPath)
    {
        SerializedProperty property = so.FindProperty(propertyName);
        if (property == null)
        {
            Debug.LogError($"{Tag} {so.targetObject.GetType().Name}.{propertyName} 직렬화 필드가 없다.");
            return;
        }
        if (property.objectReferenceValue != null)
            return;

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (prefab == null)
            Debug.LogWarning($"{Tag} 임시 VFX 프리팹이 없어 {propertyName} 을 비운다: {assetPath}");
        property.objectReferenceValue = prefab;
    }

    // 배열 칸마다 SetIfEmpty — 민경이 바꾼 칸은 유지한다.
    private static void SetArrayIfEmpty(SerializedObject so, string propertyName, string[] assetPaths)
    {
        SerializedProperty array = so.FindProperty(propertyName);
        if (array == null || !array.isArray)
        {
            Debug.LogError($"{Tag} {so.targetObject.GetType().Name}.{propertyName} 배열 필드가 없다.");
            return;
        }
        if (array.arraySize < assetPaths.Length)
            array.arraySize = assetPaths.Length;

        for (int i = 0; i < assetPaths.Length; i++)
        {
            SerializedProperty element = array.GetArrayElementAtIndex(i);
            if (element.objectReferenceValue != null)
                continue;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPaths[i]);
            if (prefab == null)
                Debug.LogWarning($"{Tag} 임시 VFX 프리팹이 없어 {propertyName}[{i}] 를 비운다: {assetPaths[i]}");
            element.objectReferenceValue = prefab;
        }
    }

    // 임시 HUD — 화면 하단 중앙(거너 과열 게이지와 같은 높이대, 공용 CombatHUD 위). 스프라이트 없이 Image 색만 쓴다.
    private static GameObject EnsureHudPrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefabPath);
        if (existing != null)
            return existing;

        GameObject root = new GameObject(HudInstanceName);
        try
        {
            AssassinHUD hud = root.AddComponent<AssassinHUD>();

            GameObject canvasGo = new GameObject("Canvas", typeof(RectTransform));
            canvasGo.layer = LayerMask.NameToLayer("UI");
            canvasGo.transform.SetParent(root.transform, false);
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            UnityEngine.UI.CanvasScaler scaler = canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            TMPro.TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(KrFontPath);
            if (font == null)
                Debug.LogWarning($"{Tag} 한글 폰트가 없다({KrFontPath}) — 라벨이 깨질 수 있다");

            RectTransform panel = NewUiRect("Panel", canvasGo.transform);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0f);
            panel.pivot = new Vector2(0.5f, 0f);
            panel.anchoredPosition = new Vector2(0f, 170f);
            panel.sizeDelta = new Vector2(320f, 64f);

            // 분노 게이지 — 패널 위쪽 왼편
            BuildRageGauge(hud, panel, font);

            // 일반 E 강화 준비 — 게이지 오른쪽 글자
            RectTransform enhanced = NewUiRect("EnhancedReady", panel);
            enhanced.anchorMin = enhanced.anchorMax = new Vector2(0f, 1f);
            enhanced.pivot = new Vector2(0f, 1f);
            enhanced.anchoredPosition = new Vector2(110f, 2f);
            enhanced.sizeDelta = new Vector2(200f, 22f);
            NewLabel(enhanced, font, "E 강화 준비", new Color(1f, 0.85f, 0.25f), TMPro.TextAlignmentOptions.Left);
            enhanced.gameObject.SetActive(false);

            // 변신 남은 시간 — 패널 아래 바 + 라벨, 해제 가능 눈금
            RectTransform transformRoot = NewUiRect("Transform", panel);
            Stretch(transformRoot);

            RectTransform bar = NewUiRect("Bar", transformRoot);
            bar.anchorMin = new Vector2(0f, 0f);
            bar.anchorMax = new Vector2(1f, 0f);
            bar.pivot = new Vector2(0.5f, 0f);
            bar.anchoredPosition = Vector2.zero;
            bar.sizeDelta = new Vector2(0f, 12f);

            RectTransform back = NewUiRect("Back", bar);
            Stretch(back);
            back.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(0f, 0f, 0f, 0.6f);

            RectTransform fill = NewUiRect("Fill", bar);
            Stretch(fill);
            UnityEngine.UI.Image fillImage = fill.gameObject.AddComponent<UnityEngine.UI.Image>();

            RectTransform tick = NewUiRect("ReleaseTick", bar);
            tick.anchorMin = new Vector2(1f, 0f);
            tick.anchorMax = new Vector2(1f, 1f);
            tick.pivot = new Vector2(0.5f, 0.5f);
            tick.sizeDelta = new Vector2(3f, 6f);
            tick.anchoredPosition = Vector2.zero;
            tick.gameObject.AddComponent<UnityEngine.UI.Image>().color = Color.white;

            RectTransform labelRect = NewUiRect("Label", transformRoot);
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 0f);
            labelRect.pivot = new Vector2(0.5f, 0f);
            labelRect.anchoredPosition = new Vector2(0f, 14f);
            labelRect.sizeDelta = new Vector2(0f, 20f);
            TMPro.TextMeshProUGUI label = NewLabel(labelRect, font, "변신", Color.white, TMPro.TextAlignmentOptions.BottomLeft);
            transformRoot.gameObject.SetActive(false);

            SerializedObject so = new SerializedObject(hud);
            so.FindProperty("canvas").objectReferenceValue = canvas;
            so.FindProperty("transformRoot").objectReferenceValue = transformRoot.gameObject;
            so.FindProperty("transformFill").objectReferenceValue = fill;
            so.FindProperty("transformFillImage").objectReferenceValue = fillImage;
            so.FindProperty("releaseTick").objectReferenceValue = tick;
            so.FindProperty("transformLabel").objectReferenceValue = label;
            so.FindProperty("enhancedReadyRoot").objectReferenceValue = enhanced.gameObject;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, HudPrefabPath);
            Debug.Log($"{Tag} A12 HUD 프리팹 생성(임시 배치 — 화면 하단 중앙 y=170): {HudPrefabPath}");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefabPath);
    }

    private const string RageGaugeName = "Rage";
    private static readonly string[] LegacyStackOrbNames = { "Stack1", "Stack2", "Stack3", "Stack4" };

    /// <summary>
    /// 구 R 스택 구슬을 분노 게이지 바로 바꾼다(§4.2, 2026-10-09). 기존 HUD 프리팹만 고친다 — 다른 요소의 배치는 그대로.
    /// 재실행해도 결과가 같다(이미 게이지가 있으면 배선만 다시 한다).
    /// </summary>
    [MenuItem("Tools/Player/Assassin/9. HUD 분노 게이지 갱신 (R 스택 대체)")]
    public static void UpgradeHudToRageGauge()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefabPath) == null)
        {
            Debug.LogError($"{Tag} HUD 프리팹이 없다. 8번 메뉴가 게이지 포함으로 새로 만든다: {HudPrefabPath}");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(HudPrefabPath);
        try
        {
            AssassinHUD hud = root.GetComponent<AssassinHUD>();
            Transform panel = root.transform.Find("Canvas/Panel");
            if (hud == null || panel == null)
            {
                Debug.LogError($"{Tag} HUD 프리팹 구조가 다르다(AssassinHUD·Canvas/Panel) — 수동으로 고칠 것: {HudPrefabPath}");
                return;
            }

            foreach (string orbName in LegacyStackOrbNames)
            {
                Transform orb = panel.Find(orbName);
                if (orb != null)
                    Object.DestroyImmediate(orb.gameObject);
            }

            Transform existing = panel.Find(RageGaugeName);
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            BuildRageGauge(hud, (RectTransform)panel, AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(KrFontPath));

            PrefabUtility.SaveAsPrefabAsset(root, HudPrefabPath);
            Debug.Log($"{Tag} HUD 분노 게이지 갱신 — R 스택 구슬 제거, 게이지 바·최소 변신량 눈금 배선: {HudPrefabPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // 분노 게이지 — 패널 위쪽 왼편 가로 바(구 스택 구슬 자리) + 최소 변신량 눈금 + 바 안 수치.
    private static void BuildRageGauge(AssassinHUD hud, RectTransform panel, TMPro.TMP_FontAsset font)
    {
        RectTransform bar = NewUiRect(RageGaugeName, panel);
        bar.anchorMin = bar.anchorMax = new Vector2(0f, 1f);
        bar.pivot = new Vector2(0f, 1f);
        bar.anchoredPosition = new Vector2(0f, -3f);
        bar.sizeDelta = new Vector2(100f, 14f);

        RectTransform back = NewUiRect("Back", bar);
        Stretch(back);
        back.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(0f, 0f, 0f, 0.6f);

        RectTransform fill = NewUiRect("Fill", bar);
        Stretch(fill);
        fill.anchorMax = new Vector2(0f, 1f);
        UnityEngine.UI.Image fillImage = fill.gameObject.AddComponent<UnityEngine.UI.Image>();

        RectTransform tick = NewUiRect("MinTick", bar);
        tick.anchorMin = new Vector2(0.4f, 0f);
        tick.anchorMax = new Vector2(0.4f, 1f);
        tick.pivot = new Vector2(0.5f, 0.5f);
        tick.sizeDelta = new Vector2(2f, 4f);
        tick.anchoredPosition = Vector2.zero;
        tick.gameObject.AddComponent<UnityEngine.UI.Image>().color = Color.white;

        RectTransform labelRect = NewUiRect("Label", bar);
        Stretch(labelRect);
        TMPro.TextMeshProUGUI label = NewLabel(labelRect, font, "0", Color.white, TMPro.TextAlignmentOptions.Center);
        label.fontSize = 12f;
        label.raycastTarget = false;

        SerializedObject so = new SerializedObject(hud);
        so.FindProperty("rageFill").objectReferenceValue = fill;
        so.FindProperty("rageFillImage").objectReferenceValue = fillImage;
        so.FindProperty("rageMinTick").objectReferenceValue = tick;
        so.FindProperty("rageLabel").objectReferenceValue = label;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static TMPro.TextMeshProUGUI NewLabel(
        RectTransform rect, TMPro.TMP_FontAsset font, string text, Color color, TMPro.TextAlignmentOptions alignment)
    {
        TMPro.TextMeshProUGUI label = rect.gameObject.AddComponent<TMPro.TextMeshProUGUI>();
        if (font != null)
            label.font = font;
        label.fontSize = 16f;
        label.color = color;
        label.alignment = alignment;
        label.text = text;
        return label;
    }

    private static RectTransform NewUiRect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    // 가붕이 간파 SO 의 같은 이름 직렬화 필드를 값 복사한다(툴팁 문구는 어쌔신 것을 따로 쓴다).
    private static void CopyInterruptValuesFromPaladin(AssassinInterruptSkillData data)
    {
        FirstMeleeInterruptSkillData source = AssetDatabase.LoadAssetAtPath<FirstMeleeInterruptSkillData>(PaladinInterruptDataPath);
        if (source == null)
        {
            Debug.LogError($"{Tag} 가붕이 간파 데이터가 없어 기본값으로 둔다: {PaladinInterruptDataPath}");
            return;
        }

        SerializedObject from = new SerializedObject(source);
        SerializedObject to = new SerializedObject(data);
        SerializedProperty property = from.GetIterator();
        int copied = 0;
        for (bool enter = true; property.NextVisible(enter); enter = false)
        {
            if (property.name == "m_Script" || property.name == "tooltip" || to.FindProperty(property.name) == null)
                continue;
            to.CopyFromSerializedProperty(property);
            copied++;
        }

        to.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(data);
        Debug.Log($"{Tag} 간파 데이터 값 복사: {PaladinInterruptDataPath} → {InterruptDataPath} (필드 {copied}개, 쿨 {data.CooldownTime}s, ×{data.AttackDamageMultiplier})");
    }

    private static void InitializeCircleStrikeData(AssassinCircleStrikeSkillData data)
    {
        // 쿨타임 = 확정(공격 시작) 순간 서버 승인 — 자동 커밋. 원 중심 쪽으로 즉시 돌아선다.
        InitializeSkillData(data, cooldown: 12f, commitManually: false,
            maxActiveDuration: ClipDuration(LoadClip("Skill_02_Move_000pct"), CircleStrikeSpeed) + 0.3f,
            animatorStateName: CircleStrikeSkillState, snapRotation: true);

        // 조준 = GroundPoint 고정 거리(A4): 중심 거리 1.5m, 효과 반경 2m, 좌클릭 확정.
        SerializedObject so = new SerializedObject(data);
        so.FindProperty("attackDamageMultiplier").floatValue = 2.8f;
        so.FindProperty("hittableLayers").intValue = EnemyHittableLayers;
        so.FindProperty("targetingMode").enumValueIndex = (int)SkillTargetingMode.GroundPoint;
        so.FindProperty("confirmMode").enumValueIndex = (int)SkillConfirmMode.ClickToConfirm;
        so.FindProperty("castRange").floatValue = 1.5f;
        so.FindProperty("fixedDistance").boolValue = true;
        so.FindProperty("aoeRadius").floatValue = 2f;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(data);
    }

    // 컨트롤러는 animatorStateName 의 해시로 레이어 지정 없이(-1) CrossFade 한다 — 그 해시를 가진 첫 레이어의 상태가 재생된다.
    // 일반 E Buff 는 상체 레이어에 있다.
    private static void ValidateSkillState(AnimatorController controller, PlayerSkillData data)
    {
        if (data == null)
            return;

        int hash = Animator.StringToHash(data.AnimatorStateName);
        foreach (AnimatorControllerLayer layer in controller.layers)
        {
            foreach (ChildAnimatorState child in layer.stateMachine.states)
            {
                if (child.state.nameHash == hash)
                {
                    Debug.Log($"{Tag} {data.name} → Animator 상태 '{data.AnimatorStateName}'({layer.name}) 해시 {hash} 확인.");
                    return;
                }
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
        // 일반 E Buff 는 걸으며 쓰므로 0층이 아니라 상체 레이어에 둔다(EnsureUpperBodyLayer).
        EnsureUpperBodyLayer(controller);

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

        // A9: 변신 E 원형 5타 — 실제 좌표는 고정, 클립의 모델 이동은 연출(루트모션 꺼짐).
        AnimatorState circleStrike = EnsureState(sm, CircleStrikeSkillState, LoadClip("Skill_02_Move_000pct"));
        circleStrike.speed = CircleStrikeSpeed;
        EnsureReturnToIdleTransitions(circleStrike, idle, combatIdle);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log($"{Tag} Animator 구성: Idle 2, Run Start/Loop/Stop, Dodge, Interrupt, Wave 4, 강타·변신 묶음, R Parry·Q 돌진 2·변신 E 상태 + 상체 레이어 E Buff");
    }

    [MenuItem("Tools/Player/Assassin/9. 일반 E 상체 레이어 (걸으며 Buff)")]
    public static void BuildUpperBodyLayer()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError($"{Tag} Animator Controller가 없다. 먼저 1번 메뉴를 실행할 것: {ControllerPath}");
            return;
        }

        EnsureUpperBodyLayer(controller);
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        ValidateSkillState(controller, AssetDatabase.LoadAssetAtPath<PlayerSkillData>(EnhanceSkillDataPath));
    }

    // 일반 E Buff 를 상체 마스크 레이어(Override, 가중치 1)에서 재생한다 — 하체는 0층의 Idle/Run 이 그대로 움직인다.
    // 평소엔 빈 상태(Write Defaults 끔)라 0층을 그대로 통과시킨다. Buff 가 끝나면 빈 상태로 돌아간다.
    // 강제 종료(대시·피격·사망)는 AssassinEnhanceSkill.OnEnd 가 빈 상태로 CrossFade 한다.
    private static void EnsureUpperBodyLayer(AnimatorController controller)
    {
        AnimatorStateMachine baseMachine = controller.layers[0].stateMachine;
        foreach (ChildAnimatorState child in baseMachine.states)
        {
            if (child.state.name != EnhanceSkillState)
                continue;
            baseMachine.RemoveState(child.state);
            break;
        }

        AvatarMask mask = EnsureUpperBodyMask();
        AnimatorControllerLayer[] layers = controller.layers;
        int layerIndex = Array.FindIndex(layers, l => l.name == AssassinEnhanceSkill.UpperBodyLayerName);
        if (layerIndex < 0)
        {
            controller.AddLayer(AssassinEnhanceSkill.UpperBodyLayerName);
            layers = controller.layers;
            layerIndex = layers.Length - 1;
        }

        layers[layerIndex].avatarMask = mask;
        layers[layerIndex].blendingMode = AnimatorLayerBlendingMode.Override;
        layers[layerIndex].defaultWeight = 1f;
        controller.layers = layers;

        AnimatorStateMachine sm = controller.layers[layerIndex].stateMachine;
        AnimatorState empty = EnsureState(sm, AssassinEnhanceSkill.UpperBodyEmptyState, null);
        empty.writeDefaultValues = false;
        sm.defaultState = empty;

        AnimatorState buff = EnsureState(sm, EnhanceSkillState, LoadClip("Buff"));
        buff.speed = EnhanceBuffSpeed;
        buff.writeDefaultValues = false; // 한 레이어 안에서 Write Defaults 를 섞지 않는다
        EnsureTransition(buff, empty, true, 0.98f, null, default, 0f);
    }

    // Humanoid 상체 — 몸통·머리·팔·손가락·손 IK. 루트·다리·발 IK 는 0층(걷기)을 따른다.
    private static AvatarMask EnsureUpperBodyMask()
    {
        AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperBodyMaskPath);
        if (mask == null)
        {
            mask = new AvatarMask();
            AssetDatabase.CreateAsset(mask, UpperBodyMaskPath);
        }

        for (AvatarMaskBodyPart part = 0; part < AvatarMaskBodyPart.LastBodyPart; part++)
        {
            bool upper = part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head ||
                part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm ||
                part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers ||
                part == AvatarMaskBodyPart.LeftHandIK || part == AvatarMaskBodyPart.RightHandIK;
            mask.SetHumanoidBodyPartActive(part, upper);
        }

        EditorUtility.SetDirty(mask);
        return mask;
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
            // 슬롯마다 — HeadNeck 메시는 서브메시가 여럿(Head·Face)이다. 0번만 바꾸면 Face 슬롯이 FBX 의
            // 원래 재질(패키지의 Built-in Assassin_Face — 가져오지 않음)로 남아 깨진다(10-06 은희 Play).
            // Face 는 같은 아틀라스지만 따로 조정할 수 있게 별도 Variant(Assassin_Face_Toon)를 쓴다.
            // 🔸 FBX .meta 의 재질 remap 이 가져오지 않은 패키지 재질을 가리켜 원본 슬롯 이름이 비어 있다 — 이름으로 Face 를 못 가르면
            //    기본 Toon 으로 채우고, 이미 채워진 슬롯(손으로 Assassin_Face_Toon 을 넣은 것 포함)은 건드리지 않는다.
            Material faceMaterial = AssetDatabase.LoadAssetAtPath<Material>(FaceMaterialPath);
            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(renderer);
                Material[] current = renderer.sharedMaterials;
                Material[] sourceSlots = source != null ? source.sharedMaterials : current;
                var slots = new Material[sourceSlots.Length];
                for (int i = 0; i < slots.Length; i++)
                {
                    Material existing = i < current.Length ? current[i] : null;
                    string slotName = sourceSlots[i] != null ? sourceSlots[i].name : string.Empty;
                    bool isFace = slotName.IndexOf("Face", StringComparison.OrdinalIgnoreCase) >= 0 && faceMaterial != null;
                    slots[i] = existing != null && existing != sourceSlots[i] ? existing : isFace ? faceMaterial : material;
                    Debug.Log($"{Tag} 재질 슬롯 {renderer.name}[{i}] 원본 '{slotName}' → {slots[i].name}");
                }
                renderer.sharedMaterials = slots;
            }
            if (renderers.Length == 0)
                Debug.LogError($"{Tag} SkinnedMeshRenderer를 찾지 못해 머티리얼을 적용하지 못했다.", root);
            else if (renderers.Length != 2)
                Debug.LogWarning($"{Tag} 예상 메시 2개와 다르다({renderers.Length}개). 찾은 렌더러 모두 Assassin_Toon을 적용했다.", root);

            EnsureDagger(root.transform, "LeftHand", DaggerLeftPath, material);
            EnsureDagger(root.transform, "RightHand", DaggerRightPath, material);

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
            EnsureChild(vfx, "TransformedAttack");
            EnsureChild(vfx, "EnhancedReady");
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
        // 10-07 로비 선택 노출(은희) — 다시 돌려도 선택 가능 상태·초상화를 되돌리지 않는다.
        targetEntry.FindPropertyRelative("available").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(roster);
        Debug.Log($"{Tag} CharacterRoster[{target}] = 어쌔신, available=true: {RosterPath}");
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

    // 손 본 아래에 단검 FBX 인스턴스를 붙인다(이미 있으면 그대로 — 손으로 맞춘 위치·회전 유지).
    // FBX 루트의 회전은 원본 리그의 손 기준 자세라 그대로 두고 위치만 손에 맞춘다(Play 로 확인).
    private static void EnsureDagger(Transform root, string handBoneName, string fbxPath, Material material)
    {
        GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (fbx == null)
        {
            Debug.LogWarning($"{Tag} 단검 FBX 가 없다(SVN 확인): {fbxPath}");
            return;
        }

        Transform hand = FindDeep(root, handBoneName);
        if (hand == null)
        {
            Debug.LogError($"{Tag} 손 본 {handBoneName} 을 찾지 못해 단검을 붙이지 못했다.", root);
            return;
        }

        if (hand.Find(fbx.name) != null)
        {
            Debug.Log($"{Tag} 단검 유지: {handBoneName}/{fbx.name}");
            return;
        }

        var dagger = (GameObject)PrefabUtility.InstantiatePrefab(fbx, hand);
        dagger.transform.localPosition = Vector3.zero;
        dagger.transform.localRotation = fbx.transform.localRotation;
        dagger.transform.localScale = Vector3.one;
        foreach (Renderer renderer in dagger.GetComponentsInChildren<Renderer>(true))
            renderer.sharedMaterial = material;
        Debug.Log($"{Tag} 단검 부착: {handBoneName}/{fbx.name} (Assassin_Toon)");
    }

    private static Transform FindDeep(Transform parent, string name)
    {
        foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == name)
                return child;
        }
        return null;
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
