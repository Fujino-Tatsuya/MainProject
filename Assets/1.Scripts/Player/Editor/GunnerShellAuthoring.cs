using System.Text.RegularExpressions;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 거너 껍데기 생성 (PLAN-gunner.md G9). 메뉴 한 번으로 아래 셋을 만든다 — 이미 있으면 건너뛴다(덮어쓰지 않음).
/// 1) 애니메이터 컨트롤러: 코드가 쓰는 파라미터(DefaultAttack·Interrupt·IsMoving·AttackIndex·IsGrabbed)와 Idle/Walk 빈 상태.
///    클립은 G0(gunner.fbx 클립 분할, SVN) 이후 G3 에서 채운다.
/// 2) Gunner_Armature.prefab: gunner.fbx + Animator·NetworkTransform(회전만, 오너)·NetworkAnimator(오너)·애니 이벤트 릴레이, 손에 laser_gun.
///    Paladin_Armature 루트 구성과 같다.
/// 3) Player_Gunner.prefab: Player.prefab Variant + 자식 "Armature"(이름 규칙 — player-prefabs.md §1.4),
///    PlayerMovement.armature·PlayerSoulController.soulVisualRoot 배선, 🔴 GlobalObjectIdHash 실기록(SetDirty → SaveAssetIfDirty).
/// </summary>
public static class GunnerShellAuthoring
{
    const string BasePrefabPath = "Assets/2.Prefabs/Player/Player.prefab";
    const string Folder = "Assets/2.Prefabs/Player/Gunner";
    const string ArmaturePath = Folder + "/Gunner_Armature.prefab";
    const string VariantPath = Folder + "/Player_Gunner.prefab";
    const string ModelPath = "Assets/50.Art/Char/gunner/gunner.fbx";
    const string GunModelPath = "Assets/50.Art/Char/gunner/laser_gun.fbx";
    const string ControllerFolder = "Assets/4.Animations/Player/Gunner";
    const string ControllerPath = ControllerFolder + "/GunnerAnimatorController.controller";

    // 오른손 본 추정 — 리그 이름 규칙을 모르므로 후보를 넓게 잡고, 못 찾으면 루트에 두고 경고한다.
    static readonly Regex RightHandBone = new Regex(@"(?i)^(c_)?(hand|wrist)([._ ]?(r|right))$|^(right|r)[._ ]?hand$");

    [MenuItem("Tools/Player/Gunner/껍데기 생성 (G9)")]
    public static void CreateShell()
    {
        EnsureFolder(Folder);
        EnsureFolder(ControllerFolder);

        AnimatorController controller = EnsureController();
        GameObject armature = EnsureArmature(controller);
        if (armature == null)
            return;

        EnsureVariant(armature);
        AssetDatabase.SaveAssets();
    }

    const string DataFolder = "Assets/9.ScriptableObject/Player/Gunner";
    const string HeatDataPath = DataFolder + "/GunnerHeatData.asset";
    const string BasicAttackDataPath = DataFolder + "/GunnerBasicAttackData.asset";

    /// <summary>
    /// G3 — 과열·기본 공격 데이터 에셋을 만들고(있으면 재사용) Player_Gunner 루트에 GunnerHeat·GunnerBeamAttack·
    /// GunnerBasicAttack·GunnerHeatHUD 를 붙인다(이미 있으면 데이터 참조만 채운다).
    /// </summary>
    [MenuItem("Tools/Player/Gunner/기본 공격·과열 부착 (G3)")]
    public static void AttachBasicAttack()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath) == null)
        {
            Debug.LogError($"[Gunner] Variant 가 없다 — 먼저 '껍데기 생성 (G9)' 실행: {VariantPath}");
            return;
        }

        EnsureFolder(DataFolder);
        var heatData = EnsureAsset<GunnerHeatData>(HeatDataPath);
        var attackData = EnsureAsset<GunnerBasicAttackData>(BasicAttackDataPath);

        GameObject root = PrefabUtility.LoadPrefabContents(VariantPath);
        try
        {
            SetReference(EnsureComponent<GunnerHeat>(root), "data", heatData);
            EnsureComponent<GunnerBeamAttack>(root);
            SetReference(EnsureComponent<GunnerBasicAttack>(root), "data", attackData);
            EnsureComponent<GunnerHeatHUD>(root);

            PrefabUtility.SaveAsPrefabAsset(root, VariantPath);
            Debug.Log($"[Gunner] 기본 공격·과열 부착 완료: {VariantPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    const string ChargeLaserDataPath = DataFolder + "/GunnerChargeLaserData.asset";

    /// <summary>
    /// G4 — Q 충전 레이저. 데이터(수동 쿨 커밋·대상 마스크·애니 상태) 생성, Player_Gunner 에 GunnerBeamView·GunnerChargeLaserSkill 부착,
    /// PlayerSkillController 의 Q 슬롯 배선. 애니 상태는 "애니메이터 구성" 메뉴가 만든다(같이 한 번 더 돈다).
    /// </summary>
    [MenuItem("Tools/Player/Gunner/Q 충전 레이저 부착 (G4)")]
    public static void AttachChargeLaser()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath) == null)
        {
            Debug.LogError($"[Gunner] Variant 가 없다 — 먼저 '껍데기 생성 (G9)' 실행: {VariantPath}");
            return;
        }

        EnsureFolder(DataFolder);
        bool created = AssetDatabase.LoadAssetAtPath<GunnerChargeLaserData>(ChargeLaserDataPath) == null;
        var data = EnsureAsset<GunnerChargeLaserData>(ChargeLaserDataPath);
        if (created)
        {
            var so = new SerializedObject(data);
            so.FindProperty("cooldownTime").floatValue = 6f;
            so.FindProperty("commitCooldownManually").boolValue = true;
            so.FindProperty("maxActiveDuration").floatValue = 5f;
            so.FindProperty("hittableLayers").intValue = 17664; // Enemy·Projectile·EnemyHurtBox
            so.FindProperty("animatorStateName").stringValue = "Gunner_Q_Start";
            so.FindProperty("snapRotationOnStart").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);
        }

        GameObject root = PrefabUtility.LoadPrefabContents(VariantPath);
        try
        {
            EnsureComponent<GunnerBeamView>(root);
            var skill = EnsureComponent<GunnerChargeLaserSkill>(root);
            SetReference(skill, "data", data);
            SetReference(root.GetComponent<PlayerSkillController>(), "mainSkill", skill);

            PrefabUtility.SaveAsPrefabAsset(root, VariantPath);
            Debug.Log($"[Gunner] Q 충전 레이저 부착 완료: {VariantPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        BuildAnimator();
    }

    const string CoolBackstepDataPath = DataFolder + "/GunnerCoolBackstepData.asset";

    /// <summary>G5 — E 냉각 백스텝. 데이터 생성, GunnerCoolBackstepSkill 부착, E 슬롯 배선, 애니 상태.</summary>
    [MenuItem("Tools/Player/Gunner/E 냉각 백스텝 부착 (G5)")]
    public static void AttachCoolBackstep()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath) == null)
        {
            Debug.LogError($"[Gunner] Variant 가 없다 — 먼저 '껍데기 생성 (G9)' 실행: {VariantPath}");
            return;
        }

        EnsureFolder(DataFolder);
        bool created = AssetDatabase.LoadAssetAtPath<GunnerCoolBackstepData>(CoolBackstepDataPath) == null;
        var data = EnsureAsset<GunnerCoolBackstepData>(CoolBackstepDataPath);
        if (created)
        {
            var so = new SerializedObject(data);
            so.FindProperty("cooldownTime").floatValue = 5f;
            so.FindProperty("maxActiveDuration").floatValue = 2f;
            so.FindProperty("animatorStateName").stringValue = "Gunner_E_Backstep";
            so.FindProperty("snapRotationOnStart").boolValue = true; // 조준(무기 전방)을 바라본 채 뒤로
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);
        }

        GameObject root = PrefabUtility.LoadPrefabContents(VariantPath);
        try
        {
            var skill = EnsureComponent<GunnerCoolBackstepSkill>(root);
            SetReference(skill, "data", data);
            SetReference(root.GetComponent<PlayerSkillController>(), "subSkill", skill);

            PrefabUtility.SaveAsPrefabAsset(root, VariantPath);
            Debug.Log($"[Gunner] E 냉각 백스텝 부착 완료: {VariantPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        BuildAnimator();
    }

    const string InterruptDataPath = DataFolder + "/GunnerInterruptData.asset";
    const string InterruptAnchorName = "InterruptAttack";

    /// <summary>
    /// G6 — 우클릭 근접 간파. Gunner_Armature 에 판정 앵커(InterruptAttack: BoxCollider + ColliderInfo, 전방) 추가,
    /// 데이터 생성, GunnerInterruptSkill 부착·앵커 배선, 우클릭 슬롯 배선, 애니 상태.
    /// </summary>
    [MenuItem("Tools/Player/Gunner/우클릭 근접 간파 부착 (G6)")]
    public static void AttachInterrupt()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath) == null)
        {
            Debug.LogError($"[Gunner] Variant 가 없다 — 먼저 '껍데기 생성 (G9)' 실행: {VariantPath}");
            return;
        }

        // 1) 몸체 프리팹에 앵커 — Armature 루트 아래라 캐릭터 방향과 함께 돈다(가붕이 InterruptAttack 과 같은 위치 규칙)
        GameObject armature = PrefabUtility.LoadPrefabContents(ArmaturePath);
        try
        {
            if (armature.transform.Find(InterruptAnchorName) == null)
            {
                var anchor = new GameObject(InterruptAnchorName);
                anchor.transform.SetParent(armature.transform, false);
                var box = anchor.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.center = new Vector3(0f, 1f, 0.9f);
                box.size = new Vector3(1.4f, 1.8f, 1.4f);
                anchor.AddComponent<ColliderInfo>();
                PrefabUtility.SaveAsPrefabAsset(armature, ArmaturePath);
                Debug.Log($"[Gunner] 간파 앵커 추가: {ArmaturePath}/{InterruptAnchorName} (전방 0.9m, 1.4×1.8×1.4)");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(armature);
        }

        // 2) 데이터
        EnsureFolder(DataFolder);
        bool created = AssetDatabase.LoadAssetAtPath<GunnerInterruptData>(InterruptDataPath) == null;
        var data = EnsureAsset<GunnerInterruptData>(InterruptDataPath);
        if (created)
        {
            var so = new SerializedObject(data);
            so.FindProperty("cooldownTime").floatValue = 3f;
            so.FindProperty("maxActiveDuration").floatValue = 2f;
            so.FindProperty("hittableLayers").intValue = 17664;
            so.FindProperty("animatorStateName").stringValue = "Gunner_RMB_Interrupt";
            so.FindProperty("snapRotationOnStart").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);
        }

        // 3) Variant — 스킬 부착·앵커·슬롯
        GameObject root = PrefabUtility.LoadPrefabContents(VariantPath);
        try
        {
            EnsureComponent<GunnerBeamView>(root);
            var skill = EnsureComponent<GunnerInterruptSkill>(root);
            SetReference(skill, "data", data);

            Transform anchor = root.transform.Find("Armature/" + InterruptAnchorName);
            if (anchor == null)
                Debug.LogError("[Gunner] Armature/InterruptAttack 을 못 찾았다 — 앵커 미배선");
            else
                SetReference(skill, "hitboxAnchor", anchor.GetComponent<ColliderInfo>());

            SetReference(root.GetComponent<PlayerSkillController>(), "interruptSkill", skill);

            PrefabUtility.SaveAsPrefabAsset(root, VariantPath);
            Debug.Log($"[Gunner] 우클릭 근접 간파 부착 완료: {VariantPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        BuildAnimator();
    }

    const string UpperBodyMaskPath = ControllerFolder + "/GunnerUpperBody.mask";
    // Auto-Rig Pro 컨트롤 리그라 상체가 한 서브트리에 모여 있지 않다(forearm.r/hand.r·head.x 가 c_traj 바로 아래).
    // 그래서 "척추 아래 전부"가 아니라 "하체·골반·루트 계열을 뺀 전부"를 켠다.
    static readonly Regex LowerBodyBone = new Regex(@"(?i)(thigh|leg|foot|toe|knee|root|^c_pos$|^c_traj$|^rig$)");
    const string PelvisSegment = "/c_root.x";

    /// <summary>
    /// G0 이후 — gunner.fbx 클립을 컨트롤러에 연결한다(재실행 시 같은 이름 상태를 갱신).
    /// Base: Idle·Walk·Gunner_Attack_Start(= Q_charge_loop — 기본 공격 준비 동작이자 연사 중 하체).
    /// UpperBody(척추 이상 AvatarMask, Override): Empty(기본) · Gunner_Attack_Fire(= gunner_attack, 속도 = FireSpeed) → 끝나면 Empty.
    /// </summary>
    [MenuItem("Tools/Player/Gunner/애니메이터 구성 (G0 이후)")]
    public static void BuildAnimator()
    {
        EnsureFolder(ControllerFolder);
        AnimatorController controller = EnsureController();

        var clips = new System.Collections.Generic.Dictionary<string, AnimationClip>();
        foreach (Object asset in AssetDatabase.LoadAllAssetRepresentationsAtPath(ModelPath))
        {
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                clips[clip.name] = clip;
        }

        AnimationClip Clip(string name)
        {
            if (clips.TryGetValue(name, out AnimationClip clip))
                return clip;
            Debug.LogError($"[Gunner] {ModelPath} 에 클립 '{name}' 이 없다 — FBX 임포트 설정(G0) 확인");
            return null;
        }

        if (System.Array.TrueForAll(controller.parameters, p => p.name != "FireSpeed"))
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "FireSpeed",
                type = AnimatorControllerParameterType.Float,
                defaultFloat = 1f,
            });

        AnimatorStateMachine baseMachine = controller.layers[0].stateMachine;
        EnsureState(baseMachine, "Idle").motion = Clip("gunner_idle");
        EnsureState(baseMachine, "Walk").motion = Clip("gunner_walk");
        EnsureState(baseMachine, "Gunner_Attack_Start").motion = Clip("gunner_skill_Q_charge_loop");

        // Q: 시작 → 집중(정지/이동, IsMoving 으로 전환) → 발사(GunnerBeamView 가 튼다) → 회복 → Idle
        AnimatorState idle = EnsureState(baseMachine, "Idle");
        AnimatorState qStart = EnsureState(baseMachine, "Gunner_Q_Start");
        qStart.motion = Clip("gunner_skill_Q_start");
        AnimatorState qCharge = EnsureState(baseMachine, "Gunner_Q_Charge");
        qCharge.motion = Clip("gunner_skill_Q_charge_loop");
        AnimatorState qChargeMove = EnsureState(baseMachine, "Gunner_Q_ChargeMove");
        qChargeMove.motion = Clip("gunner_skill_Q_charge_move_loop");
        AnimatorState qFire = EnsureState(baseMachine, "Gunner_Q_Fire");
        qFire.motion = Clip("gunner_skill_Q_fire");
        AnimatorState qRecover = EnsureState(baseMachine, "Gunner_Q_Recover");
        qRecover.motion = Clip("gunner_skill_Q_recover");

        EnsureExitTimeTransition(qStart, qCharge);
        EnsureConditionTransition(qCharge, qChargeMove, AnimatorConditionMode.If);
        EnsureConditionTransition(qChargeMove, qCharge, AnimatorConditionMode.IfNot);
        EnsureExitTimeTransition(qFire, qRecover);
        EnsureExitTimeTransition(qRecover, idle);

        // E: 백스텝 → Idle(스킬 종료 때 컨트롤러도 Idle 로 넘긴다)
        AnimatorState eBackstep = EnsureState(baseMachine, "Gunner_E_Backstep");
        eBackstep.motion = Clip("gunner_skill_E_cool_backstep");
        EnsureExitTimeTransition(eBackstep, idle);

        // 우클릭: 근접 간파 → Idle
        AnimatorState rmb = EnsureState(baseMachine, "Gunner_RMB_Interrupt");
        rmb.motion = Clip("gunner_skill_RMB_interrupt");
        EnsureExitTimeTransition(rmb, idle);

        AvatarMask mask = EnsureUpperBodyMask();

        AnimatorControllerLayer[] layers = controller.layers;
        int upper = System.Array.FindIndex(layers, l => l.name == "UpperBody");
        if (upper < 0)
        {
            controller.AddLayer("UpperBody");
            layers = controller.layers;
            upper = layers.Length - 1;
        }
        layers[upper].defaultWeight = 1f;
        layers[upper].blendingMode = AnimatorLayerBlendingMode.Override;
        layers[upper].avatarMask = mask;
        controller.layers = layers; // 레이어는 복사본이라 되써야 반영된다

        AnimatorStateMachine upperMachine = controller.layers[upper].stateMachine;
        AnimatorState empty = EnsureState(upperMachine, "Empty");
        upperMachine.defaultState = empty;

        AnimatorState fire = EnsureState(upperMachine, "Gunner_Attack_Fire");
        fire.motion = Clip("gunner_attack");
        fire.speedParameterActive = true;
        fire.speedParameter = "FireSpeed";
        if (System.Array.TrueForAll(fire.transitions, t => t.destinationState != empty))
        {
            AnimatorStateTransition back = fire.AddTransition(empty);
            back.hasExitTime = true;
            back.exitTime = 1f;
            back.duration = 0.1f;
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Gunner] 애니메이터 구성 완료: {ControllerPath} (Base 3 상태, UpperBody 마스크 {mask.name})");
    }

    static void EnsureExitTimeTransition(AnimatorState from, AnimatorState to)
    {
        if (!System.Array.TrueForAll(from.transitions, t => t.destinationState != to))
            return;
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = true;
        transition.exitTime = 1f;
        transition.duration = 0.1f;
    }

    static void EnsureConditionTransition(AnimatorState from, AnimatorState to, AnimatorConditionMode mode)
    {
        if (!System.Array.TrueForAll(from.transitions, t => t.destinationState != to))
            return;
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.duration = 0.15f;
        transition.AddCondition(mode, 0f, "IsMoving");
    }

    static AnimatorState EnsureState(AnimatorStateMachine machine, string name)
    {
        foreach (ChildAnimatorState child in machine.states)
        {
            if (child.state.name == name)
                return child.state;
        }
        return machine.AddState(name);
    }

    // Generic 리그라 Humanoid 부위 대신 본 경로로 만든다. 모델 루트·하체·골반 서브트리는 끄고 나머지(척추·팔·손·머리)를 켠다.
    static AvatarMask EnsureUpperBodyMask()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        Transform[] all = model.GetComponentsInChildren<Transform>(true);

        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperBodyMaskPath);
        if (mask == null)
        {
            mask = new AvatarMask();
            AssetDatabase.CreateAsset(mask, UpperBodyMaskPath);
        }

        mask.transformCount = all.Length;
        int active = 0;
        for (int i = 0; i < all.Length; i++)
        {
            string path = AnimationUtility.CalculateTransformPath(all[i], model.transform);
            bool on = all[i] != model.transform
                && !LowerBodyBone.IsMatch(all[i].name)
                && !("/" + path + "/").Contains(PelvisSegment + "/");
            mask.SetTransformPath(i, path);
            mask.SetTransformActive(i, on);
            if (on)
                active++;
        }

        EditorUtility.SetDirty(mask);
        Debug.Log($"[Gunner] 상체 마스크: 하체·골반·루트 제외, 켠 본 {active}/{all.Length}");
        return mask;
    }

    static T EnsureAsset<T>(string path) where T : ScriptableObject
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null)
            return asset;

        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        Debug.Log($"[Gunner] 데이터 생성: {path}");
        return asset;
    }

    static T EnsureComponent<T>(GameObject root) where T : Component
    {
        var component = root.GetComponent<T>();
        return component != null ? component : root.AddComponent<T>();
    }

    static AnimatorController EnsureController()
    {
        var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (existing != null)
        {
            Debug.Log($"[Gunner] 컨트롤러가 이미 있다 — 건너뜀: {ControllerPath}");
            return existing;
        }

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("DefaultAttack", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Interrupt", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);
        controller.AddParameter("AttackIndex", AnimatorControllerParameterType.Int);
        controller.AddParameter("IsGrabbed", AnimatorControllerParameterType.Bool);

        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        AnimatorState idle = sm.AddState("Idle");
        AnimatorState walk = sm.AddState("Walk");
        sm.defaultState = idle;

        AnimatorStateTransition toWalk = idle.AddTransition(walk);
        toWalk.hasExitTime = false;
        toWalk.duration = 0.1f;
        toWalk.AddCondition(AnimatorConditionMode.If, 0f, "IsMoving");

        AnimatorStateTransition toIdle = walk.AddTransition(idle);
        toIdle.hasExitTime = false;
        toIdle.duration = 0.1f;
        toIdle.AddCondition(AnimatorConditionMode.IfNot, 0f, "IsMoving");

        Debug.Log($"[Gunner] 컨트롤러 생성: {ControllerPath} (Idle/Walk 빈 상태 — 클립은 G3)");
        return controller;
    }

    static GameObject EnsureArmature(AnimatorController controller)
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(ArmaturePath);
        if (existing != null)
        {
            Debug.Log($"[Gunner] Armature 가 이미 있다 — 건너뜀: {ArmaturePath}");
            return existing;
        }

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
        {
            Debug.LogError($"[Gunner] 모델이 없다: {ModelPath} — SVN 아트 업데이트 확인");
            return null;
        }

        var root = (GameObject)PrefabUtility.InstantiatePrefab(model);
        root.name = "Gunner_Armature";
        try
        {
            Animator animator = root.GetComponent<Animator>();
            if (animator == null)
                animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            // Paladin_Armature 와 같은 설정: 위치·스케일은 루트 NetworkTransform 이, 이 노드는 회전만(오너 권한).
            var networkTransform = root.AddComponent<NetworkTransform>();
            networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            networkTransform.SyncPositionX = networkTransform.SyncPositionY = networkTransform.SyncPositionZ = false;
            networkTransform.SyncScaleX = networkTransform.SyncScaleY = networkTransform.SyncScaleZ = false;

            var networkAnimator = root.AddComponent<NetworkAnimator>();
            networkAnimator.AuthorityMode = NetworkAnimator.AuthorityModes.Owner;
            networkAnimator.Animator = animator;

            root.AddComponent<PlayerAnimationEventRelay>();
            root.AddComponent<EffectAnimEventRelay>();

            AttachGun(root.transform);

            PrefabUtility.SaveAsPrefabAsset(root, ArmaturePath);
            Debug.Log($"[Gunner] Armature 생성: {ArmaturePath}");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(ArmaturePath);
    }

    static void AttachGun(Transform armatureRoot)
    {
        var gunModel = AssetDatabase.LoadAssetAtPath<GameObject>(GunModelPath);
        if (gunModel == null)
        {
            Debug.LogWarning($"[Gunner] 무기 모델이 없다: {GunModelPath} — 무기 없이 진행");
            return;
        }

        Transform hand = null;
        foreach (Transform t in armatureRoot.GetComponentsInChildren<Transform>(true))
        {
            if (RightHandBone.IsMatch(t.name))
            {
                hand = t;
                break;
            }
        }

        if (hand == null)
            Debug.LogWarning("[Gunner] 오른손 본을 못 찾았다 — laser_gun 을 Armature 루트에 둔다. 손 위치는 수동으로 옮길 것.");

        var gun = (GameObject)PrefabUtility.InstantiatePrefab(gunModel, hand != null ? hand : armatureRoot);
        gun.name = "LaserGun";
        gun.transform.localPosition = Vector3.zero;
        gun.transform.localRotation = Quaternion.identity;
        Debug.Log($"[Gunner] laser_gun 부착: {(hand != null ? hand.name : "(루트)")}");
    }

    static void EnsureVariant(GameObject armaturePrefab)
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath);
        if (existing != null)
        {
            Debug.Log($"[Gunner] Variant 가 이미 있다 — 건너뜀: {VariantPath}");
            LogHash(existing);
            return;
        }

        var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePrefabPath);
        var root = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab);
        root.name = "Player_Gunner";
        try
        {
            var armature = (GameObject)PrefabUtility.InstantiatePrefab(armaturePrefab, root.transform);
            armature.name = "Armature";
            armature.transform.SetSiblingIndex(0);
            armature.transform.localPosition = Vector3.zero;
            armature.transform.localRotation = Quaternion.identity;

            SetReference(root.GetComponent<PlayerMovement>(), "armature", armature.transform);
            SetReference(root.GetComponent<PlayerSoulController>(), "soulVisualRoot", armature.transform);

            PrefabUtility.SaveAsPrefabAsset(root, VariantPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }

        // 🔴 SaveAsPrefabAsset 만으로는 Variant 의 GlobalObjectIdHash 오버라이드가 YAML 에 안 써진다(player-prefabs.md §0).
        var saved = AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath);
        var networkObject = saved.GetComponent<NetworkObject>();
        EditorUtility.SetDirty(networkObject);
        AssetDatabase.SaveAssetIfDirty(saved);

        Debug.Log($"[Gunner] Variant 생성: {VariantPath}");
        LogHash(saved);
    }

    static void LogHash(GameObject variant)
    {
        var networkObject = variant.GetComponent<NetworkObject>();
        uint hash = new SerializedObject(networkObject).FindProperty("GlobalObjectIdHash").uintValue;
        var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BasePrefabPath);
        uint baseHash = new SerializedObject(basePrefab.GetComponent<NetworkObject>()).FindProperty("GlobalObjectIdHash").uintValue;

        if (hash == baseHash)
            Debug.LogError($"[Gunner] GlobalObjectIdHash 가 base 와 같다({hash}) — YAML 기록 확인 필요");
        else
            Debug.Log($"[Gunner] GlobalObjectIdHash = {hash} (base {baseHash})");
    }

    static void SetReference(Object component, string propertyName, Object value)
    {
        if (component == null)
        {
            Debug.LogWarning($"[Gunner] {propertyName} 배선 대상 컴포넌트가 없다");
            return;
        }

        var so = new SerializedObject(component);
        SerializedProperty property = so.FindProperty(propertyName);
        if (property == null)
        {
            Debug.LogWarning($"[Gunner] {component.GetType().Name}.{propertyName} 필드가 없다");
            return;
        }

        property.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
