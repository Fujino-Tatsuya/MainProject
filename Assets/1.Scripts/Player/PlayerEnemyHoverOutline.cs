using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 몬스터 빨간 외곽선(할 일 58) — <b>표시만, 로컬 전용.</b> 네트워크 동기화·서버 권한·판정 변경 없음.
/// 오너(오프라인 포함, <see cref="Player.IsInputSource"/>)에서만 돈다. 대상 세트는 둘이고 합집합을 칠한다.
/// <list type="bullet">
/// <item><b>호버</b> — 커서 아래 적 한 마리. 판정은 스킬 조준과 같은 <see cref="CursorUnitPick"/>(적 레이어 → Hurtbox 우선 → 자기 자신·HP 0 제외).
///   레이는 프레임당 한 번 — 단일 대상 조준 중엔 <see cref="PlayerSkillTargeting"/> 가 쏜 레이의 판정을
///   <see cref="ReportAimedTarget"/> 로 받아 쓰고 이쪽은 쏘지 않는다.</item>
/// <item><b>스킬 범위</b> — 범위 표시가 떠 있는 동안 그 스킬이 실제로 맞힐 영역 안의 적. 표시를 그리는 쪽이 매 프레임
///   서버 판정과 같은 <see cref="SkillHitVolume"/> 을 <see cref="ReportSkillHitArea"/> 로 넘기고, 여기서 NonAlloc 겹침 1회로 고른다.
///   보고가 끊긴 프레임(시전·취소)에 바로 꺼진다. 상한 초과는 가까운 순(<see cref="SkillRangeOutlineSelection"/>).</item>
/// </list>
/// 한 몬스터에 겹은 하나 — 두 세트에 다 들면 호버 쪽(호버 강도)이 이긴다. 겹 붙이기/떼기는 세트가 바뀐 프레임에만 한다.
///
/// 외곽선은 대상 렌더러에 겹 렌더러를 얹는 방식(<see cref="HoverOutlineOverlaySet"/>) — 몬스터 머티리얼은 무수정.
/// 겹은 림(프레넬) 셰이더(<c>Project/Enemy Hover Rim</c>)로 |N·V| 가 작은 실루엣 가장자리만 칠한다.
/// 색·임계값·부드러움·강도는 여기 한 곳에서 튜닝한다 — 값이 바뀐 프레임에 런타임 사본 머티리얼(호버용·범위용 2개)에 반영되므로
/// Play 중 인스펙터로 바로 볼 수 있다(원본 에셋은 안 바뀐다). 문서: Docs/tech/monster-hover-outline.md
/// </summary>
[RequireComponent(typeof(Player))]
public class PlayerEnemyHoverOutline : MonoBehaviour
{
    const int OverlapBufferSize = 64;

    [SerializeField]
    [Tooltip("림 머티리얼(Project/Enemy Hover Rim). 런타임 사본에 아래 값을 적용해 쓴다.")]
    Material outlineMaterial;

    [SerializeField]
    [Tooltip("림 색. 알파는 림 전체 불투명도.")]
    Color rimColor = new Color(1f, 0.08f, 0.08f, 1f);

    [SerializeField, Range(0f, 1f)]
    [Tooltip("|N·V| 가 이 값보다 작은 곳(실루엣 가장자리)만 칠한다. 클수록 림이 두껍다.")]
    float rimThreshold = 0.25f;

    [SerializeField, Range(0f, HoverRimSettings.MaxSoftness)]
    [Tooltip("임계값 경계의 번짐 폭(임계값 ± 이 값/2). 0 이면 딱 잘린다.")]
    float rimSoftness = 0.1f;

    [SerializeField, Range(0f, HoverRimSettings.MaxIntensity)]
    [Tooltip("색 밝기 배수. 1 = 색 그대로, 크면 블룸에 걸려 빛난다.")]
    float rimIntensity = 1f;

    [Header("스킬 범위 외곽선")]
    [SerializeField, Range(0f, HoverRimSettings.MaxIntensity)]
    [Tooltip("스킬 범위 안 적의 림 밝기 배수(색·임계값·부드러움은 위와 같다). 호버 대상은 위 rimIntensity 를 쓴다.")]
    float skillRangeRimIntensity = 1f;

    [SerializeField, Min(1)]
    [Tooltip("스킬 범위 외곽선을 동시에 칠할 최대 마리 수. 넘으면 가까운 순.")]
    int skillRangeMaxTargets = SkillRangeOutlineSelection.DefaultMaxTargets;

    // 한 몬스터의 겹 묶음. 세트에서 빠지면 겹을 떼고 풀로 돌아간다.
    sealed class Entry
    {
        public Unit Unit;
        public bool IsHover;
        public readonly HoverOutlineOverlaySet Overlays = new HoverOutlineOverlaySet();
    }

    Player owner;
    PlayerAimIndicator aimIndicator;
    int enemyMask;
    Material hoverMaterial;
    Material rangeMaterial;
    HoverRimSettings appliedHover;
    HoverRimSettings appliedRange;
    readonly HoverOutlineTracker tracker = new HoverOutlineTracker();
    readonly List<Entry> entries = new List<Entry>();
    readonly Stack<Entry> pool = new Stack<Entry>();

    int reportedFrame = -1;
    Unit reportedTarget;

    int areaFrame = -1;
    SkillHitVolume area;
    readonly Collider[] overlapHits = new Collider[OverlapBufferSize];
    readonly List<OutlineCandidate<Unit>> candidates = new List<OutlineCandidate<Unit>>(OverlapBufferSize);
    readonly List<Unit> rangeTargets = new List<Unit>(SkillRangeOutlineSelection.DefaultMaxTargets);

    /// <summary>지금 호버 외곽선을 그리는 적(없으면 null).</summary>
    public Unit HoveredEnemy => tracker.Current as Unit;

    /// <summary>지금 스킬 범위 외곽선을 그리는 적들(호버 대상 제외, 가까운 순).</summary>
    public IReadOnlyList<Unit> SkillRangeTargets => rangeTargets;

    void Awake()
    {
        owner = GetComponent<Player>();
        aimIndicator = GetComponent<PlayerAimIndicator>();
        // LayerMask.GetMask 는 호출마다 배열을 만든다 — 한 번만 읽는다.
        enemyMask = GameLayers.EnemyTargetMask;
    }

    /// <summary>[오너 로컬] 스킬 조준이 이번 프레임 같은 커서 레이로 고른 대상(없으면 null). 이 프레임엔 자기 레이를 생략한다.</summary>
    public void ReportAimedTarget(Unit target)
    {
        reportedFrame = Time.frameCount;
        reportedTarget = target;
    }

    /// <summary>
    /// [오너 로컬] 이번 프레임 범위 표시가 떠 있는 스킬이 실제로 맞힐 영역. 표시를 그리는 쪽이 매 프레임 부른다 —
    /// 안 부른 프레임엔 범위 외곽선이 꺼진다.
    /// </summary>
    public void ReportSkillHitArea(SkillHitVolume volume)
    {
        areaFrame = Time.frameCount;
        area = volume;
    }

    /// <summary>[오너 로컬] 표시가 이번 프레임에 꺼졌다(시전·취소) — 같은 프레임의 보고도 버린다.</summary>
    public void ClearSkillHitArea() => areaFrame = -1;

    // 조준·스킬 틱(Update)이 보고를 마친 뒤 판정한다.
    void LateUpdate()
    {
        bool active = owner != null && owner.IsInputSource && owner.CurrentState != PlayerActionState.Dead;
        Unit hovered = active ? ResolveHovered() : null;
        tracker.Set(hovered, out _);
        ResolveRangeTargets(active, hovered);
        SyncEntries(hovered);

        for (int i = 0; i < entries.Count; i++)
            entries[i].Overlays.SyncVisibility();
        if (entries.Count > 0)
            ApplySettings();
    }

    Unit ResolveHovered()
    {
        if (reportedFrame == Time.frameCount)
            return CursorUnitPick.IsSelectable(reportedTarget, owner) ? reportedTarget : null;

        Camera cam = aimIndicator != null ? aimIndicator.TargetCamera : null;
        if (cam == null || Mouse.current == null)
            return null;

        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        return CursorUnitPick.Pick(ray, enemyMask, owner, out Unit unit) == CursorPickResult.ValidTarget
            ? unit
            : null;
    }

    // 스킬의 피격 마스크 ∩ 적 마스크로 같은 영역을 겹쳐 본다 — 상자 판정은 같고, 상자·플레이어 같은 비적은 빠진다.
    void ResolveRangeTargets(bool active, Unit hovered)
    {
        rangeTargets.Clear();
        candidates.Clear();
        if (!active || areaFrame != Time.frameCount)
            return;

        int mask = area.Mask & enemyMask;
        if (mask == 0)
            return;

        int count = area.Overlap(overlapHits, mask, QueryTriggerInteraction.Collide);
        Vector3 origin = owner.transform.position;
        for (int i = 0; i < count; i++)
        {
            Collider col = overlapHits[i];
            overlapHits[i] = null;
            if (col == null || !area.Accepts(col.bounds))
                continue;

            Unit unit = CursorUnitPick.ResolveUnit(col);
            if (unit == null)
                continue;

            Vector3 offset = unit.transform.position - origin;
            offset.y = 0f;
            candidates.Add(new OutlineCandidate<Unit>(unit, offset.sqrMagnitude, CursorUnitPick.IsSelectable(unit, owner)));
        }

        SkillRangeOutlineSelection.Select(candidates, hovered, skillRangeMaxTargets, rangeTargets);
    }

    // 세트가 바뀐 몬스터만 겹을 떼고 붙인다. 호버 ↔ 범위 전환은 머티리얼만 바꿔 다시 붙인다.
    void SyncEntries(Unit hovered)
    {
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            Entry entry = entries[i];
            bool wantHover = entry.Unit != null && entry.Unit == hovered;
            bool wantRange = !wantHover && entry.Unit != null && ContainsRange(entry.Unit);
            if (!wantHover && !wantRange)
            {
                Release(i);
                continue;
            }

            if (wantHover != entry.IsHover)
            {
                entry.IsHover = wantHover;
                entry.Overlays.Attach(entry.Unit.transform, GetRuntimeMaterial(wantHover));
            }
        }

        if (hovered != null && FindEntry(hovered) < 0)
            Acquire(hovered, true);

        for (int i = 0; i < rangeTargets.Count; i++)
        {
            if (FindEntry(rangeTargets[i]) < 0)
                Acquire(rangeTargets[i], false);
        }
    }

    bool ContainsRange(Unit unit)
    {
        for (int i = 0; i < rangeTargets.Count; i++)
            if (rangeTargets[i] == unit)
                return true;
        return false;
    }

    int FindEntry(Unit unit)
    {
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].Unit == unit)
                return i;
        return -1;
    }

    void Acquire(Unit unit, bool isHover)
    {
        Entry entry = pool.Count > 0 ? pool.Pop() : new Entry();
        entry.Unit = unit;
        entry.IsHover = isHover;
        entry.Overlays.Attach(unit.transform, GetRuntimeMaterial(isHover));
        entries.Add(entry);
    }

    void Release(int index)
    {
        Entry entry = entries[index];
        entry.Overlays.Clear();
        entry.Unit = null;
        entries.RemoveAt(index);
        pool.Push(entry);
    }

    void ReleaseAll()
    {
        for (int i = entries.Count - 1; i >= 0; i--)
            Release(i);
    }

    Material GetRuntimeMaterial(bool isHover)
    {
        if (outlineMaterial == null)
            return null;

        if (isHover)
        {
            if (hoverMaterial == null)
            {
                hoverMaterial = new Material(outlineMaterial) { name = outlineMaterial.name + " (Hover)" };
                appliedHover = CurrentSettings(rimIntensity);
                appliedHover.ApplyTo(hoverMaterial);
            }
            return hoverMaterial;
        }

        if (rangeMaterial == null)
        {
            rangeMaterial = new Material(outlineMaterial) { name = outlineMaterial.name + " (SkillRange)" };
            appliedRange = CurrentSettings(skillRangeRimIntensity);
            appliedRange.ApplyTo(rangeMaterial);
        }
        return rangeMaterial;
    }

    HoverRimSettings CurrentSettings(float intensity) =>
        new HoverRimSettings(rimColor, rimThreshold, rimSoftness, intensity).Sanitized();

    // 값이 바뀐 프레임에만 사본에 쓴다 — Play 중 인스펙터 튜닝이 칠해진 대상에 바로 보인다.
    void ApplySettings()
    {
        if (hoverMaterial != null)
        {
            HoverRimSettings current = CurrentSettings(rimIntensity);
            if (!current.Equals(appliedHover))
            {
                appliedHover = current;
                current.ApplyTo(hoverMaterial);
            }
        }

        if (rangeMaterial != null)
        {
            HoverRimSettings current = CurrentSettings(skillRangeRimIntensity);
            if (!current.Equals(appliedRange))
            {
                appliedRange = current;
                current.ApplyTo(rangeMaterial);
            }
        }
    }

    void OnDisable()
    {
        tracker.Set(null, out _);
        rangeTargets.Clear();
        areaFrame = -1;
        ReleaseAll();
    }

    void OnDestroy()
    {
        ReleaseAll();
        if (hoverMaterial != null)
            Destroy(hoverMaterial);
        if (rangeMaterial != null)
            Destroy(rangeMaterial);
    }
}
