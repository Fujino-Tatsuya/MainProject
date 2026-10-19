using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 몬스터 마우스 호버 빨간 외곽선(할 일 58) — <b>표시만, 로컬 전용.</b> 각 피어가 자기 커서 아래 적 한 마리만 칠한다.
/// 네트워크 동기화·서버 권한·판정 변경 없음. 오너(오프라인 포함, <see cref="Player.IsInputSource"/>)에서만 돈다.
///
/// 대상 판정은 스킬 조준과 같은 <see cref="CursorUnitPick"/>(적 레이어 → Hurtbox 우선 → 자기 자신·HP 0 제외).
/// 레이는 프레임당 한 번 — 단일 대상 조준 중엔 <see cref="PlayerSkillTargeting"/> 가 쏜 레이의 판정을
/// <see cref="ReportAimedTarget"/> 로 받아 쓰고 이쪽은 쏘지 않는다.
///
/// 외곽선은 대상 렌더러에 겹 렌더러를 얹는 방식(<see cref="HoverOutlineOverlaySet"/>) — 몬스터 머티리얼은 무수정.
/// 색·두께는 여기 한 곳에서 튜닝한다(런타임 사본 머티리얼에 적용 — 원본 에셋은 안 바뀐다). 문서: Docs/tech/monster-hover-outline.md
/// </summary>
[RequireComponent(typeof(Player))]
public class PlayerEnemyHoverOutline : MonoBehaviour
{
    static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
    static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
    static readonly int OutlineScaleId = Shader.PropertyToID("_OutlineScale");

    [SerializeField]
    [Tooltip("외곽선 머티리얼(Zone/InteractOutline — 'Outline' 패스만). 런타임 사본에 아래 색·두께를 적용해 쓴다.")]
    Material outlineMaterial;

    [SerializeField]
    [Tooltip("호버 외곽선 색.")]
    Color outlineColor = new Color(1f, 0.08f, 0.08f, 1f);

    [SerializeField, Range(0f, 10f)]
    [Tooltip("화면 공간 두께. 몬스터의 기존 검정 FlatKit 외곽선보다 두꺼워야 그 바깥에 빨간 테가 보인다.")]
    float outlineWidth = 4f;

    [SerializeField, Range(1f, 1.1f)]
    [Tooltip("메시 자체 부풀림(각진 모서리 틈 메우기). 큰 스키닝 메시는 원점 기준으로 밀려 어긋날 수 있어 기본 1(끔).")]
    float outlineMeshScale = 1f;

    Player owner;
    PlayerAimIndicator aimIndicator;
    Material runtimeMaterial;
    readonly HoverOutlineTracker tracker = new HoverOutlineTracker();
    readonly HoverOutlineOverlaySet overlays = new HoverOutlineOverlaySet();

    int reportedFrame = -1;
    Unit reportedTarget;

    /// <summary>지금 외곽선을 그리는 적(없으면 null).</summary>
    public Unit HoveredEnemy => tracker.Current as Unit;

    void Awake()
    {
        owner = GetComponent<Player>();
        aimIndicator = GetComponent<PlayerAimIndicator>();
    }

    /// <summary>[오너 로컬] 스킬 조준이 이번 프레임 같은 커서 레이로 고른 대상(없으면 null). 이 프레임엔 자기 레이를 생략한다.</summary>
    public void ReportAimedTarget(Unit target)
    {
        reportedFrame = Time.frameCount;
        reportedTarget = target;
    }

    // 조준(Update)이 보고를 마친 뒤 판정한다.
    void LateUpdate()
    {
        Unit next = ResolveHovered();
        if (tracker.Set(next, out _))
        {
            overlays.Clear();
            if (next != null)
                overlays.Attach(next.transform, GetRuntimeMaterial());
        }

        overlays.SyncVisibility();
    }

    Unit ResolveHovered()
    {
        if (owner == null || !owner.IsInputSource || owner.CurrentState == PlayerActionState.Dead)
            return null;

        if (reportedFrame == Time.frameCount)
            return CursorUnitPick.IsSelectable(reportedTarget, owner) ? reportedTarget : null;

        Camera cam = aimIndicator != null ? aimIndicator.TargetCamera : null;
        if (cam == null || Mouse.current == null)
            return null;

        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        return CursorUnitPick.Pick(ray, GameLayers.EnemyTargetMask, owner, out Unit unit) == CursorPickResult.ValidTarget
            ? unit
            : null;
    }

    Material GetRuntimeMaterial()
    {
        if (outlineMaterial == null)
            return null;

        if (runtimeMaterial == null)
            runtimeMaterial = new Material(outlineMaterial) { name = outlineMaterial.name + " (Hover)" };

        // 붙일 때마다 적용 — Play 중 인스펙터 튜닝이 다음 호버부터 반영된다.
        runtimeMaterial.SetColor(OutlineColorId, outlineColor);
        runtimeMaterial.SetFloat(OutlineWidthId, outlineWidth);
        runtimeMaterial.SetFloat(OutlineScaleId, outlineMeshScale);
        return runtimeMaterial;
    }

    void OnDisable()
    {
        tracker.Set(null, out _);
        overlays.Clear();
    }

    void OnDestroy()
    {
        overlays.Clear();
        if (runtimeMaterial != null)
            Destroy(runtimeMaterial);
    }
}
