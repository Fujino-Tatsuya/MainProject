using UnityEngine;

// 보스 전/후방 방향 표시기 — 로스트아크의 백어택/헤드어택 표시를 이 프로젝트 규약으로 옮긴 것.
//
// 바닥에 환형 섹터(도넛 조각) 2개를 그린다:
//   전방 호 = 헤드어택 · 카운터 구역 (카운터 창이 열리면 강조색)
//   후방 호 = 백어택 구역
// 중립(측면)은 그리지 않는다 — 참조 이미지도 전/후만 표시한다.
//
// 순수 로컬 연출이다(NetworkBehaviour 아님). 보스의 회전은 NetworkTransform 으로 이미 복제되므로
// 각 피어가 자기 화면에서 계산하면 자동으로 동기화된다 — 복제할 상태가 없다.
//
// ─── 이 컴포넌트가 처리하는 함정들 ───────────────────────────────────────────
//
// 🔴 **플레이어 위에 그려지면 안 된다.** 해법은 "특별한 처리"가 아니라 **정상 깊이 테스트**다.
//    플레이어는 불투명 큐라 깊이를 먼저 쓰고, 이 메시는 투명 큐라 그 뒤에 그려지면서
//    플레이어 몸에 가려진다. 흔히 쓰는 `ZTest Always`(항상 위에) 머티리얼을 쓰면 깨진다
//    → 머티리얼이 투명 큐인지 런타임에 검증해 경고한다.
//
// 🔴 **점프 공격 중에는 숨긴다.** 플래그가 아니라 실측으로 판단한다 — GroundProbe 로 발밑 바닥을
//    찾아 보스와의 높이 차가 임계를 넘으면 숨긴다. 특정 기믹 구현에 의존하지 않으므로
//    앞으로 어떤 공중 기믹이 추가돼도 자동으로 맞다.
//
// 🔴 **절대 Y 를 상수로 박지 않는다.** 보스룸 보행면은 Y 0.50, BossScene 은 0 이다.
//    항상 "찾은 바닥 + 간격"으로 눕힌다(GroundProbe 규약).
//    간격은 표준 0.05 보다 **의도적으로 낮은 0.04** 다 — AoE 장판(0.05)이 항상 위에 오게 해서
//    같은 평면 투명 정렬 깜빡임을 없앤다.
//
// 🔴 **회전은 yaw 만** 가져온다. 보스 애니메이션이 루트를 기울이면 링이 같이 기울어진다.
[DisallowMultipleComponent]
public class BossDirectionIndicator : MonoBehaviour, IBossTelegraph
{
    [Header("머티리얼 — 🔴 투명 큐 URP Unlit 을 배선할 것")]
    [SerializeField]
    [Tooltip("두 호가 공유하는 머티리얼. 색은 MaterialPropertyBlock 으로 호별로 따로 넣는다.\n" +
             "🔴 Surface Type = Transparent 여야 한다. 불투명이면 z-fighting 이 나고, " +
             "'항상 위에(ZTest Always)' 설정이면 플레이어 위에 그려진다.\n" +
             "🔴 Shader.Find 가 아니라 이 인스펙터 참조로 물려야 빌드에서 스트립되지 않는다.")]
    Material arcMaterial;

    [Header("모양")]
    [SerializeField, Min(0.1f)]
    [Tooltip("호 안쪽 반지름(m). 보스 몸통보다 크게 잡아 발밑에 가리지 않게 한다.")]
    float innerRadius = 1.6f;
    [SerializeField, Min(0.2f)]
    [Tooltip("호 바깥 반지름(m).")]
    float outerRadius = 2.6f;
    [SerializeField, Range(4, 64)]
    [Tooltip("호 하나당 세그먼트 수(부드러움).")]
    int segmentsPerArc = 24;

    [Header("색")]
    [SerializeField]
    [Tooltip("전방 호(헤드어택·카운터 구역) 기본색.")]
    Color frontColor = new Color(1f, 0.35f, 0.25f, 0.45f);
    [SerializeField]
    [Tooltip("카운터 창이 열린 동안의 전방 호 색(강조).")]
    Color counterReadyColor = new Color(1f, 0.9f, 0.2f, 0.8f);
    [SerializeField]
    [Tooltip("후방 호(백어택 구역) 색.")]
    Color backColor = new Color(0.3f, 0.7f, 1f, 0.45f);

    [Header("배치 / 예외 처리")]
    [SerializeField, Min(0f)]
    [Tooltip("바닥 위로 띄우는 간격(m). 표준(GroundProbe.SurfaceOffset = 0.05)보다 살짝 낮게 둬서 " +
             "AoE 장판이 항상 이 링 위에 그려지게 한다(같은 평면 투명 정렬 깜빡임 방지).")]
    float heightOffset = 0.04f;
    [SerializeField, Min(0.05f)]
    [Tooltip("보스와 발밑 바닥의 높이 차가 이 값을 넘으면 숨긴다 — 점프 공격 등 공중 상태. " +
             "지형 요철·에이전트 부유로 인한 오차보다 크게, 실제 점프 높이보다 작게 잡는다.")]
    float airborneHideHeight = 0.6f;
    [SerializeField]
    [Tooltip("바닥 탐색에 추가로 포함할 레이어(Default+Ground 는 GroundProbe 가 항상 포함).")]
    LayerMask extraGroundMask;

    // 런타임
    MonsterBase _boss;                 // 상태(사망) 조회용. 없으면 상태 예외 처리만 생략한다.
    BossDataSO _data;                  // 각도 출처 — counterFrontAngle 을 그대로 쓴다(판정과 동일 값)
    MeshRenderer _frontRenderer;
    MeshRenderer _backRenderer;
    Mesh _frontMesh;
    Mesh _backMesh;
    MaterialPropertyBlock _mpb;
    bool _counterWindowOpen;
    bool _visible = true;

    // 메시를 다시 만들어야 하는지 판단하는 스냅샷(각도·반지름을 인스펙터에서 돌려도 반영되게).
    float _builtFrontAngle = -1f;
    float _builtBackAngle = -1f;
    float _builtInner = -1f;
    float _builtOuter = -1f;
    int _builtSegments = -1;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    void Awake()
    {
        _mpb = new MaterialPropertyBlock();
        _boss = GetComponentInParent<MonsterBase>();
        _data = ResolveData();

        if (arcMaterial == null)
        {
            Debug.LogError(
                $"{name}: arcMaterial 이 비어 있다 — 방향 표시기가 아무것도 그리지 않는다. " +
                "투명(Transparent) URP Unlit 머티리얼을 배선할 것.",
                this);
            enabled = false;
            return;
        }

        // 불투명 큐면 바닥과 z-fighting 이 나고, 플레이어를 가릴 수도 있다.
        if (arcMaterial.renderQueue < (int)UnityEngine.Rendering.RenderQueue.Transparent)
            Debug.LogWarning(
                $"{name}: arcMaterial 의 renderQueue({arcMaterial.renderQueue}) 가 투명 큐가 아니다 — " +
                "바닥과 z-fighting 이 나거나 플레이어 위에 그려질 수 있다. Surface Type 을 Transparent 로 둘 것.",
                this);

        BuildRenderers();
    }

    void LateUpdate()
    {
        // SO 는 보스의 OnNetworkSpawn 에서 확정되므로 Awake 시점엔 아직 없다 — 지연 해석한다.
        // (해석되기 전에는 각도가 기본 60/60 으로 그려지고, 해석되면 다음 프레임에 재생성된다.)
        if (_data == null) _data = ResolveData();

        // 애니메이션·NetworkTransform 반영 뒤에 배치해야 한 프레임 밀리지 않는다.
        RebuildMeshesIfNeeded();

        if (!ShouldShow(out Vector3 groundPoint))
        {
            SetVisible(false);
            return;
        }

        Transform boss = _boss != null ? _boss.transform : transform.parent;
        if (boss == null) boss = transform;

        // 위치 = 보스 XZ + 찾은 바닥 Y(+간격). 절대 Y 상수 금지.
        transform.position = new Vector3(boss.position.x, groundPoint.y + heightOffset, boss.position.z);

        // 회전 = yaw 만. 보스 애니가 루트를 기울여도 링은 눕는다.
        Vector3 forward = boss.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);

        SetVisible(true);
    }

    // ─── IBossTelegraph ───────────────────────────────────────────────
    public void SetCounterWindow(bool open)
    {
        if (_counterWindowOpen == open) return;
        _counterWindowOpen = open;
        ApplyColors();
    }

    // ─── 표시 조건 ────────────────────────────────────────────────────
    bool ShouldShow(out Vector3 groundPoint)
    {
        groundPoint = default;

        // 사망 후에는 방향 정보가 의미 없다(디졸브/디스폰 대기 중).
        if (_boss != null && _boss.State == MonsterState.Dead)
            return false;

        Transform boss = _boss != null ? _boss.transform : transform.parent;
        if (boss == null) return false;

        // 발밑 바닥을 찾는다. GroundProbe 는 Unit 계층 콜라이더를 제외하므로
        // 보스 자기 히트박스(Default 레이어)를 바닥으로 오인하지 않는다.
        if (!GroundProbe.TryFindGround(boss.position, extraGroundMask, out RaycastHit ground, out _))
            return false;

        // 🔴 점프 등 공중 상태 — 링이 공중에 뜨거나 발과 떨어져 어색해진다. 실측으로 판단한다.
        if (boss.position.y - ground.point.y > airborneHideHeight)
            return false;

        groundPoint = ground.point;
        return true;
    }

    void SetVisible(bool visible)
    {
        if (_visible == visible) return;
        _visible = visible;

        if (_frontRenderer != null) _frontRenderer.enabled = visible;
        if (_backRenderer != null) _backRenderer.enabled = visible;
    }

    // ─── 생성 ─────────────────────────────────────────────────────────
    BossDataSO ResolveData()
    {
        // BossDataSO 는 MonsterBase 의 protected 필드라 직접 못 읽는다 — 보스가 노출한 각도를 쓴다.
        return _boss is TwentyThreeBoss boss ? boss.Data : null;
    }

    void BuildRenderers()
    {
        _frontRenderer = CreateArcRenderer("FrontArc");
        _backRenderer = CreateArcRenderer("BackArc");
        RebuildMeshesIfNeeded();
        ApplyColors();
    }

    MeshRenderer CreateArcRenderer(string childName)
    {
        var go = new GameObject(childName);
        go.transform.SetParent(transform, false);

        go.AddComponent<MeshFilter>();
        MeshRenderer r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = arcMaterial;

        // 바닥 장식이므로 그림자에 관여하지 않는다(그림자 캐스팅은 순전히 낭비 + 지면에 얼룩을 만든다).
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        // 라이트 프로브/반사 프로브도 불필요(Unlit).
        r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        return r;
    }

    void RebuildMeshesIfNeeded()
    {
        float front = FrontAngle;
        float back = BackAngle;

        bool same = Mathf.Approximately(front, _builtFrontAngle)
                    && Mathf.Approximately(back, _builtBackAngle)
                    && Mathf.Approximately(innerRadius, _builtInner)
                    && Mathf.Approximately(outerRadius, _builtOuter)
                    && segmentsPerArc == _builtSegments;
        if (same) return;

        float inner = Mathf.Max(0.05f, innerRadius);
        float outer = Mathf.Max(inner + 0.05f, outerRadius);
        int seg = Mathf.Clamp(segmentsPerArc, 4, 64);

        // 전방 = 보스 forward(+Z) 중심 ±front / 후방 = 뒤(180°) 중심 ±back.
        _frontMesh = BuildArcMesh(_frontMesh, "BossFrontArc", -front, front, inner, outer, seg);
        _backMesh = BuildArcMesh(_backMesh, "BossBackArc", 180f - back, 180f + back, inner, outer, seg);

        AssignMesh(_frontRenderer, _frontMesh);
        AssignMesh(_backRenderer, _backMesh);

        _builtFrontAngle = front;
        _builtBackAngle = back;
        _builtInner = innerRadius;
        _builtOuter = outerRadius;
        _builtSegments = seg;
    }

    static void AssignMesh(MeshRenderer r, Mesh mesh)
    {
        if (r == null || mesh == null) return;
        if (r.TryGetComponent(out MeshFilter mf))
            mf.sharedMesh = mesh;
    }

    /// <summary>전방 반각(도). 🔴 카운터 판정과 **같은 값**을 쓴다 — 표시가 판정에 대해 거짓말할 수 없다.</summary>
    float FrontAngle => _data != null ? Mathf.Clamp(_data.counterFrontAngle, 1f, 179f) : 60f;

    /// <summary>후방 반각(도). 백어택 판정이 나중에 같은 값을 읽으면 표시와 판정이 어긋나지 않는다.</summary>
    float BackAngle => _data != null ? Mathf.Clamp(_data.backAttackAngle, 1f, 179f) : 60f;

    // 로컬 XZ 평면(y=0)에 눕는 환형 섹터. 부모 회전이 yaw 만이므로 그대로 바닥에 눕는다.
    // 각도는 +Z(보스 정면)를 0 으로 하고 시계방향(Unity yaw)과 같은 방향으로 센다.
    static Mesh BuildArcMesh(Mesh reuse, string meshName, float fromDeg, float toDeg,
                             float inner, float outer, int segments)
    {
        int ringVerts = segments + 1;
        var verts = new Vector3[ringVerts * 2];
        var uvs = new Vector2[ringVerts * 2];
        var normals = new Vector3[ringVerts * 2];
        var tris = new int[segments * 6];

        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float deg = Mathf.Lerp(fromDeg, toDeg, t);
            float rad = deg * Mathf.Deg2Rad;

            // +Z 기준 yaw: x = sin, z = cos.
            Vector3 dir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));

            int vi = i * 2;
            verts[vi] = dir * inner;
            verts[vi + 1] = dir * outer;
            uvs[vi] = new Vector2(t, 0f);
            uvs[vi + 1] = new Vector2(t, 1f);
            normals[vi] = Vector3.up;
            normals[vi + 1] = Vector3.up;
        }

        for (int i = 0; i < segments; i++)
        {
            int vi = i * 2;
            int ti = i * 6;
            // 법선이 +Y(위에서 본다) 이므로 시계방향 와인딩이 앞면이 된다.
            tris[ti + 0] = vi;
            tris[ti + 1] = vi + 1;
            tris[ti + 2] = vi + 3;
            tris[ti + 3] = vi;
            tris[ti + 4] = vi + 3;
            tris[ti + 5] = vi + 2;
        }

        Mesh mesh = reuse != null ? reuse : new Mesh { name = meshName };
        mesh.Clear();
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.normals = normals;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        return mesh;
    }

    void ApplyColors()
    {
        SetRendererColor(_frontRenderer, _counterWindowOpen ? counterReadyColor : frontColor);
        SetRendererColor(_backRenderer, backColor);
    }

    // 머티리얼 인스턴스화 없이 렌더러별 색만 다르게 — HitFlash 와 같은 MPB 패턴.
    void SetRendererColor(MeshRenderer r, Color c)
    {
        if (r == null || arcMaterial == null) return;

        r.GetPropertyBlock(_mpb);
        if (arcMaterial.HasProperty(BaseColorId)) _mpb.SetColor(BaseColorId, c);
        else if (arcMaterial.HasProperty(ColorId)) _mpb.SetColor(ColorId, c);
        r.SetPropertyBlock(_mpb);
    }

#if UNITY_EDITOR
    // 인스펙터에서 색·각도를 돌리면 즉시 반영(Play 중 튜닝용).
    void OnValidate()
    {
        if (!Application.isPlaying || _mpb == null) return;
        RebuildMeshesIfNeeded();
        ApplyColors();
    }
#endif
}
