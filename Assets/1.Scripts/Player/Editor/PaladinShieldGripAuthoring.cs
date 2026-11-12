using UnityEditor;
using UnityEngine;

/// <summary>
/// 팔라딘 방패 손잡이2(보조 고정점) 저작 — 메뉴 <c>Tools/Player/팔라딘 방패 손잡이2 연결</c>.
/// Player_Paladin.prefab 안에 '손잡이1'·'손잡이2'(방패 자식)·'ElbowSocket'(forearm_stretch.l 자식, 원점)를 없으면 만들고,
/// WeaponTransformRelay 의 방패 줄에 보조 고정(손잡이1 고정 회전축, 손잡이2 → ElbowSocket, 궁 상태 동안)을 연결한다.
/// 손잡이1 처음 위치 = 보정 전 손 소켓 자리(방패 로컬) — 손이 실제로 쥐는 아래 손잡이로 옮겨 맞춘다. 이미 있으면 위치는 그대로(사용자 튜닝 유지).
/// 손잡이2 처음 위치 = 방패를 손 소켓에 맞춘 기본 자세에서 팔꿈치 자리(대략) — Scene 뷰에서 방패 팔 띠 위치로 옮겨 맞춘다.
/// </summary>
public static class PaladinShieldGripAuthoring
{
    public const string PrefabPath = "Assets/2.Prefabs/Player/Paladin/Player_Paladin.prefab";
    public const string ShieldName = "SM_Wep_Shield_01";
    public const string PrimaryHandleName = "손잡이1";
    public const string HandleName = "손잡이2";
    public const string ElbowParentName = "forearm_stretch.l";
    public const string ElbowSocketName = "ElbowSocket";
    public const string HandSocketName = "L_WeaponSocket";
    public const string UltimateState = "Garen_Ultimate_Skill";

    [MenuItem("Tools/Player/팔라딘 방패 손잡이2 연결")]
    public static void Apply()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Transform shield = Find(root.transform, ShieldName);
            Transform forearm = Find(root.transform, ElbowParentName);
            Transform socket = Find(root.transform, HandSocketName);
            WeaponTransformRelay relay = root.GetComponentInChildren<WeaponTransformRelay>(true);
            if (shield == null || forearm == null || socket == null || relay == null)
            {
                Debug.LogError($"[PaladinShieldGrip] 못 찾음 — 방패 {shield != null} · {ElbowParentName} {forearm != null} · {HandSocketName} {socket != null} · relay {relay != null}");
                return;
            }

            Transform elbow = forearm.Find(ElbowSocketName);
            if (elbow == null)
            {
                elbow = new GameObject(ElbowSocketName).transform;
                elbow.SetParent(forearm, false);
            }

            Transform handle = shield.Find(HandleName);
            Transform primary = shield.Find(PrimaryHandleName);
            if (handle == null || primary == null)
            {
                Vector3 pos = shield.position;
                Quaternion rot = shield.rotation;
                shield.SetPositionAndRotation(socket.position, socket.rotation);   // 런타임처럼 손 소켓에 맞춘 자세에서
                Vector3 elbowLocal = shield.InverseTransformPoint(elbow.position);
                Vector3 socketLocal = shield.InverseTransformPoint(socket.position);   // 손잡이1 기본 = 보정 전 손 소켓 자리
                shield.SetPositionAndRotation(pos, rot);
                if (handle == null)
                {
                    handle = new GameObject(HandleName).transform;
                    handle.SetParent(shield, false);
                    handle.localPosition = elbowLocal;
                }
                if (primary == null)
                {
                    primary = new GameObject(PrimaryHandleName).transform;
                    primary.SetParent(shield, false);
                    primary.localPosition = socketLocal;
                }
            }

            var so = new SerializedObject(relay);
            SerializedProperty follows = so.FindProperty("follows");
            bool wired = false;
            for (int i = 0; i < follows.arraySize; i++)
            {
                SerializedProperty f = follows.GetArrayElementAtIndex(i);
                if (f.FindPropertyRelative("weapon").objectReferenceValue != shield) continue;
                f.FindPropertyRelative("primaryHandle").objectReferenceValue = primary;
                f.FindPropertyRelative("secondaryHandle").objectReferenceValue = handle;
                f.FindPropertyRelative("secondaryTarget").objectReferenceValue = elbow;
                f.FindPropertyRelative("secondaryState").stringValue = UltimateState;
                SerializedProperty blend = f.FindPropertyRelative("secondaryBlend");
                if (blend.floatValue <= 0f) blend.floatValue = WeaponTransformRelay.DefaultSecondaryBlend;
                wired = true;
            }
            if (!wired)
            {
                Debug.LogError("[PaladinShieldGrip] relay 에 방패 줄이 없다 — 연결 안 함.");
                return;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[PaladinShieldGrip] 연결 — 손잡이1 local {primary.localPosition} · 손잡이2 local {handle.localPosition} · ElbowSocket ({ElbowParentName}) · 상태 {UltimateState}");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// <summary>
    /// 런타임(Play 중)에서 맞춘 손잡이2·ElbowSocket 로컬 위치·블렌드를 Player_Paladin.prefab 에 저장한다(미리보기 창 '프리팹에 저장').
    /// 프리팹을 디스크에서 읽어 이 세 값만 바꿔 저장 — 프리팹에 이미 있던 다른 변경은 그대로 남는다.
    /// </summary>
    public static bool SaveGripToPrefab(Vector3 handleLocal, Vector3 elbowLocal, float blend, Vector3? primaryLocal = null)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            if (!ApplyGrip(root, handleLocal, elbowLocal, blend, primaryLocal))
            {
                Debug.LogError("[PaladinShieldGrip] 프리팹에 손잡이2·ElbowSocket·relay 가 없다 — 먼저 'Tools/Player/팔라딘 방패 손잡이2 연결'.");
                return false;
            }
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[PaladinShieldGrip] 프리팹에 저장 — 손잡이1 {(primaryLocal.HasValue ? primaryLocal.Value.ToString() : "그대로")} · 손잡이2 {handleLocal} · ElbowSocket {elbowLocal} · 블렌드 {blend:0.###}초");
            return true;
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    /// <summary>
    /// 열린 프리팹 내용(<paramref name="root"/>)에 손잡이2·ElbowSocket 로컬 위치와 방패 줄 블렌드를 넣는다(저장은 부른 쪽). 못 찾으면 false.
    /// <paramref name="primaryLocal"/> 이 있으면 손잡이1 로컬 위치도(손잡이1 이 프리팹에 없으면 false).
    /// </summary>
    public static bool ApplyGrip(GameObject root, Vector3 handleLocal, Vector3 elbowLocal, float blend, Vector3? primaryLocal = null)
    {
        Transform shield = Find(root.transform, ShieldName);
        Transform handle = shield != null ? shield.Find(HandleName) : null;
        Transform primary = shield != null ? shield.Find(PrimaryHandleName) : null;
        Transform elbow = Find(root.transform, ElbowSocketName);
        WeaponTransformRelay relay = root.GetComponentInChildren<WeaponTransformRelay>(true);
        if (handle == null || elbow == null || relay == null || (primaryLocal.HasValue && primary == null))
            return false;
        if (primaryLocal.HasValue)
            primary.localPosition = primaryLocal.Value;
        handle.localPosition = handleLocal;
        elbow.localPosition = elbowLocal;
        var so = new SerializedObject(relay);
        SerializedProperty follows = so.FindProperty("follows");
        for (int i = 0; i < follows.arraySize; i++)
        {
            SerializedProperty f = follows.GetArrayElementAtIndex(i);
            if (f.FindPropertyRelative("weapon").objectReferenceValue == shield)
                f.FindPropertyRelative("secondaryBlend").floatValue = Mathf.Max(0f, blend);
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    public static Transform Find(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform t = Find(root.GetChild(i), name);
            if (t != null) return t;
        }
        return null;
    }
}
