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
