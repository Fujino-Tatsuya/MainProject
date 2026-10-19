using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 47 — 어쌔신 머리카락 본(Hair_Source00~06_01~04, 7가닥 × 4마디) 2차 모션을 Armature 프리팹에 붙인다.
/// 전체 저작(<see cref="AssassinShellAuthoring"/>)을 다시 돌리지 않고 머리카락 컴포넌트만 다룬다 — 사용자가 손으로 고친
/// 단검 위치·회전과 애니 이벤트를 건드리지 않기 위해서다. 멱등: 없으면 추가, 있으면 바인드 포즈만 다시 캡처한다.
/// </summary>
public static class AssassinHairAuthoring
{
    private const string Tag = "[Assassin/Hair]";
    private const string ArmaturePath = "Assets/2.Prefabs/Player/Assassin/Assassin_Armature.prefab";
    private const string ModelPath = "Assets/50.Art/Char/assassin/Assassin.fbx";

    [MenuItem("Tools/Player/Assassin/머리카락 본 연결")]
    private static void Run()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(ArmaturePath);
        try
        {
            if (!Ensure(root))
                return;

            PrefabUtility.SaveAsPrefabAsset(root, ArmaturePath);
            Debug.Log($"{Tag} 머리카락 컴포넌트 연결·바인드 포즈 캡처: {ArmaturePath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // 납품 DiveHairTests 사용법.txt — "Fan Degrees는 22로 둡니다"(검수 통과 설정, 스크립트 기본값 14 와 다름).
    private const float DiveFanDegrees = 22f;

    // 🔸 v18(10-17) 부터 FBX 를 임포트 배율 ×1.25 로 들인다(PLAN-assassin §9). 납품 v13 솔버의 길이 값(m)은 배율 1 리그 기준이라
    //    승인된 모습을 크기만 키워 재현하려면 길이·속도·가속 차원 값을 리그 배율만큼 곱한다(각도·시간 상수는 그대로).
    //    납품 원본 AssassinHairMotion.cs 는 고치지 않고 캡처 때 여기서 덮는다 — 이 값들은 인스펙터에서 바꿔도 이 메뉴가 다시 쓴다.
    private const float DeliveredHairRadius = .018f;
    private const float DeliveredTipLength = .15f;
    private const float DeliveredGravitySupport = .65f; // 중력 배율 — 가속(m/s²) 차원
    private const float DeliveredMaximumDynamicSpeed = 2f; // m/s
    private const float DeliveredNeighbourOffsetLimit = .09f;
    // 납품 v13 리그(배율 1)의 Hair_Source00_01 → 00_02 길이. 임포트 배율이 실제 리그 크기와 맞는지 대조만 한다.
    private const float DeliveredReferenceSegment = .15705195f;

    private static readonly FieldInfo InitializedField =
        typeof(AssassinHairMotion).GetField("initialized", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>
    /// Armature 루트에 v13 솔버·v16/v15 보정·구동기를 보장하고 지금(애니메이션 전) 자세로 바인드 포즈를 캡처한다.
    /// 런타임 Awake 자동 캡처에 기대지 않는다 — 비활성 생성 등으로 첫 Awake 때 자세가 바인드가 아닐 수 있다.
    /// 이미 있는 컴포넌트의 인스펙터 값(보정 strength 등)은 건드리지 않는다 — 회피 fan 은 새로 붙일 때만 넣는다.
    /// 예외: 위 길이 차원 솔버 값은 납품값 × 리그 배율로 매번 맞춘다.
    /// </summary>
    public static bool Ensure(GameObject root)
    {
        AssassinHairMotion hair = root.GetComponent<AssassinHairMotion>();
        if (hair == null)
            hair = root.AddComponent<AssassinHairMotion>();

        if (root.GetComponent<AssassinActionHairAnimation>() == null)
            root.AddComponent<AssassinActionHairAnimation>();

        if (root.GetComponent<AssassinDiveHairAnimation>() == null)
            root.AddComponent<AssassinDiveHairAnimation>().fanDegrees = DiveFanDegrees;

        if (root.GetComponent<AssassinHairDriver>() == null)
            root.AddComponent<AssassinHairDriver>();

        if (InitializedField == null)
        {
            Debug.LogError($"{Tag} AssassinHairMotion.initialized 필드를 찾지 못했다 — 납품 솔버가 바뀌었는지 확인할 것.", root);
            return false;
        }

        float scale = RigScale(root);
        try
        {
            hair.CaptureBindPose(TipPoints(root, DeliveredTipLength * scale));
        }
        catch (System.InvalidOperationException error)
        {
            Debug.LogError($"{Tag} 바인드 포즈 캡처 실패 — FBX 본 이름(Head·Hips·Hair_SourceNN_MM)을 확인할 것: {error.Message}", root);
            return false;
        }

        // 캡처가 처음 붙은 컴포넌트에는 납품 프리셋을 넣으므로 그 뒤에 덮는다. 캡슐 반경은 CaptureBindPose 안의 상수라
        // 캡처할 때마다 새로 만들어진다 — 바로 곱해도 누적되지 않는다.
        hair.hairRadius = DeliveredHairRadius * scale;
        hair.gravitySupport = DeliveredGravitySupport * scale;
        hair.maximumDynamicSpeed = DeliveredMaximumDynamicSpeed * scale;
        hair.neighbourOffsetLimit = DeliveredNeighbourOffsetLimit * scale;
        foreach (AssassinHairMotion.BodyCapsule capsule in hair.capsules)
            capsule.radius *= scale;

        // 바인드 겹침 허용치(bindAllowanceReference)는 캡처 안에서 배율 1 반경으로 이미 쟀다 — 버리고 v13 식으로 다시 잰다.
        RecomputeBindAllowances(hair);

        // 캡처가 내부에서 ResetSimulation 을 불러 진단 카운터가 오른다 — 재실행해도 프리팹 diff 가 없게 0 으로 둔다.
        hair.resetCount = hair.teleportResetCount = hair.collisionCorrections = 0;
        hair.maximumObservedSpeed = hair.maximumLengthError = hair.maximumBoneAngle = hair.maximumContactResidual = 0f;
        EditorUtility.SetDirty(hair);
        Debug.Log($"{Tag} 리그 배율 {scale:0.####} — hairRadius {hair.hairRadius:0.#####} · 끝 마디 {DeliveredTipLength * scale:0.####} m · 캡슐 반경 ×{scale:0.####}", root);
        return true;
    }

    /// <summary>
    /// 바인드 겹침 허용치(<c>bindAllowanceReference</c>)를 지금 캡슐 반경·<c>hairRadius</c> 로 납품 v13 식 그대로 다시 잰다.
    /// 지금 자세가 바인드 포즈여야 한다(프리팹 내용을 연 직후). 테스트도 이걸로 직렬화 값이 최신인지 대조한다.
    /// </summary>
    public static bool RecomputeBindAllowances(AssassinHairMotion hair)
    {
        if (InitializedField == null)
            return false;
        foreach (AssassinHairMotion.Chain chain in hair.chains)
            chain.bindAllowanceReference = null;
        InitializedField.SetValue(hair, false);
        hair.Initialize();
        return true;
    }

    /// <summary>어쌔신 몸 크기 = Assassin.fbx 임포트 배율(Scale Factor). 단검·달리기 재생 속도(<see cref="AssassinShellAuthoring"/>)도 이 값을 쓴다.</summary>
    public static float ImportScale() =>
        AssetImporter.GetAtPath(ModelPath) is ModelImporter importer ? importer.globalScale : 1f;

    /// <summary>임포트 배율(<see cref="ImportScale"/>). 리그 실측과 1% 넘게 다르면 크기가 FBX 에 구워졌는지 의심하고 알린다.</summary>
    public static float RigScale(GameObject root)
    {
        float scale = ImportScale();
        Transform reference = FindBone(root.transform, "Hair_Source00_02");
        if (reference != null)
        {
            float measured = reference.localPosition.magnitude / DeliveredReferenceSegment;
            if (Mathf.Abs(measured / scale - 1f) > .01f)
                Debug.LogError($"{Tag} 임포트 배율 {scale} 과 리그 실측 배율 {measured:0.####} 이 다르다 — FBX 에 크기를 구웠다면 납품 기준 길이도 다시 볼 것.", root);
        }
        return scale;
    }

    // 납품 CaptureBindPose 의 기본 끝점(본4 + 본3→본4 방향 × 0.15)과 같은 방향, 길이만 리그 배율을 곱한다.
    private static Vector3[] TipPoints(GameObject root, float tipLength)
    {
        var tips = new Vector3[7];
        for (int c = 0; c < tips.Length; c++)
        {
            Transform last = FindBone(root.transform, $"Hair_Source{c:D2}_04");
            Transform previous = FindBone(root.transform, $"Hair_Source{c:D2}_03");
            if (last == null || previous == null)
                return null; // 캡처가 같은 본을 찾다 InvalidOperationException 으로 알린다.
            tips[c] = last.position + (last.position - previous.position).normalized * tipLength;
        }
        return tips;
    }

    private static Transform FindBone(Transform root, string boneName)
    {
        foreach (Transform bone in root.GetComponentsInChildren<Transform>(true))
            if (bone.name == boneName)
                return bone;
        return null;
    }
}
