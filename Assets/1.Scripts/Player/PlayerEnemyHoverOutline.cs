using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 몬스터 외곽선(할 일 58) — <b>표시만, 로컬 전용.</b> 판정·서버 권한 변경 없음.
/// 오너(오프라인 포함, <see cref="Player.IsInputSource"/>)에서만 돈다. 대상 세트는 셋이고 한 몬스터는 한 색이다.
/// <list type="bullet">
/// <item><b>호버</b>(빨강) — 커서 아래 적 한 마리. 판정은 스킬 조준과 같은 <see cref="CursorUnitPick"/>(적 레이어 → Hurtbox 우선 → 자기 자신·HP 0 제외).
///   레이는 프레임당 한 번 — 단일 대상 조준 중엔 <see cref="PlayerSkillTargeting"/> 가 쏜 레이의 판정을
///   <see cref="ReportAimedTarget"/> 로 받아 쓰고 이쪽은 쏘지 않는다.</item>
/// <item><b>스킬 범위</b>(빨강) — 범위 표시가 떠 있는 동안 그 스킬이 실제로 맞힐 영역 안의 적. 표시를 그리는 쪽이 매 프레임
///   서버 판정과 같은 <see cref="SkillHitVolume"/> 을 <see cref="ReportSkillHitArea"/> 로 넘기고, 여기서 NonAlloc 겹침 1회로 고른다.
///   보고가 끊긴 프레임(시전·취소)에 바로 꺼진다. 상한 초과는 가까운 순(<see cref="SkillRangeOutlineSelection"/>).</item>
/// <item><b>나를 노림</b>(어두운 회색) — 서버가 복제한 <see cref="MonsterBase.AggroTargetId"/> 가 내 NetworkObjectId 인 살아 있는 몬스터.
///   빨강 세트에 든 몬스터는 빠진다(빨강 우선). 상한 초과는 가까운 순(<see cref="MonsterAggroTargetRule"/>).
///   스킬 인디케이터가 떠 있는 동안(<see cref="ReportSkillIndicatorActive"/>)은 전부 꺼진다.</item>
/// </list>
/// 그리기는 화면 공간 마스크 외곽선(<see cref="EnemyOutlineFeature"/>) — 여기서는 대상 렌더러 목록을 <see cref="EnemyOutlineDrawList"/> 에 넘기기만 한다.
/// 몬스터 머티리얼·레이어·계층은 무수정. 색·두께는 여기 한 곳에서 튜닝한다(매 프레임 넘기므로 Play 중 인스펙터로 바로 보인다).
/// 문서: Docs/tech/monster-hover-outline.md
/// </summary>
[RequireComponent(typeof(Player))]
public class PlayerEnemyHoverOutline : MonoBehaviour
{
    const int OverlapBufferSize = 64;
    // 스폰된 NetworkObject 를 훑어 몬스터 목록을 다시 만드는 주기(초). 몬스터 수가 바뀌는 속도면 충분하다.
    const float MonsterScanInterval = 0.25f;

    [SerializeField]
    [Tooltip("호버·스킬 범위 대상 외곽선 색(빨강). 알파 = 불투명도.")]
    Color outlineColor = new Color(1f, 0.08f, 0.08f, 1f);

    [SerializeField]
    [Tooltip("나를 추적·공격 중인 몬스터 외곽선 색(어두운 회색 — 거의 티 안 나게). 빨강 대상이면 빨강이 이긴다.")]
    Color aggroOutlineColor = new Color(0.22f, 0.22f, 0.22f, 1f);

    [SerializeField, Range(EnemyOutlineDrawList.MinWidthPixels, EnemyOutlineDrawList.MaxWidthPixels)]
    [Tooltip("외곽선 굵기(화면 픽셀). 해상도·거리와 무관하게 같은 픽셀 수.")]
    int outlineWidthPixels = 1;

    [Header("스킬 범위 외곽선")]
    [SerializeField, Min(1)]
    [Tooltip("스킬 범위 외곽선을 동시에 칠할 최대 마리 수. 넘으면 가까운 순.")]
    int skillRangeMaxTargets = SkillRangeOutlineSelection.DefaultMaxTargets;

    [Header("나를 노리는 몬스터 외곽선")]
    [SerializeField, Min(0)]
    [Tooltip("나를 노리는 몬스터를 동시에 칠할 최대 마리 수(빨강 대상 제외). 넘으면 가까운 순. 0 = 끔.")]
    int aggroMaxTargets = MonsterAggroTargetRule.DefaultMaxTargets;

    // 한 몬스터의 렌더러 묶음. 세트에서 빠지면 풀로 돌아간다.
    sealed class Entry
    {
        public Unit Unit;
        public int ColorIndex;
        public readonly List<Renderer> Renderers = new List<Renderer>();
    }

    Player owner;
    PlayerAimIndicator aimIndicator;
    int enemyMask;
    bool publishing;
    readonly HoverOutlineTracker tracker = new HoverOutlineTracker();
    readonly List<Entry> entries = new List<Entry>();
    readonly Stack<Entry> pool = new Stack<Entry>();
    readonly List<Renderer> rendererScratch = new List<Renderer>();

    int reportedFrame = -1;
    Unit reportedTarget;

    int areaFrame = -1;
    SkillHitVolume area;
    readonly Collider[] overlapHits = new Collider[OverlapBufferSize];
    readonly List<OutlineCandidate<Unit>> candidates = new List<OutlineCandidate<Unit>>(OverlapBufferSize);
    readonly List<Unit> rangeTargets = new List<Unit>(SkillRangeOutlineSelection.DefaultMaxTargets);

    MonsterAggroTargetRule.FrameStamp skillIndicator;

    float nextMonsterScan;
    readonly List<MonsterBase> monsters = new List<MonsterBase>(64);
    readonly List<Unit> aggroTargets = new List<Unit>(MonsterAggroTargetRule.DefaultMaxTargets);

    /// <summary>지금 호버 외곽선을 그리는 적(없으면 null).</summary>
    public Unit HoveredEnemy => tracker.Current as Unit;

    /// <summary>지금 스킬 범위 외곽선을 그리는 적들(호버 대상 제외, 가까운 순).</summary>
    public IReadOnlyList<Unit> SkillRangeTargets => rangeTargets;

    /// <summary>지금 "나를 노림" 회색 외곽선을 그리는 몬스터들(빨강 대상 제외, 가까운 순).</summary>
    public IReadOnlyList<Unit> AggroTargets => aggroTargets;

    /// <summary>이번 프레임 스킬 인디케이터(범위 표시·조준)가 떠 있다고 보고됐는가.</summary>
    public bool SkillIndicatorActive => skillIndicator.IsSet(Time.frameCount);

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
        ReportSkillIndicatorActive();
    }

    /// <summary>
    /// [오너 로컬] 이번 프레임 스킬 인디케이터(범위 표시·조준)가 떠 있다 — 맞을 대상 보고가 없는 조준도 부른다.
    /// 부른 프레임엔 나를 노림(회색)을 끈다. 안 부른 프레임엔 자동 해제. <see cref="ReportSkillHitArea"/> 는 이것도 겸한다.
    /// </summary>
    public void ReportSkillIndicatorActive() => skillIndicator.Mark(Time.frameCount);

    /// <summary>[오너 로컬] 표시가 이번 프레임에 꺼졌다(시전·취소) — 같은 프레임의 보고도 버린다(나를 노림은 이 프레임에 복귀).</summary>
    public void ClearSkillHitArea()
    {
        areaFrame = -1;
        skillIndicator.Clear();
    }

    // 조준·스킬 틱(Update)이 보고를 마친 뒤 판정한다. 렌더는 LateUpdate 뒤라 이번 프레임 목록이 바로 그려진다.
    void LateUpdate()
    {
        bool active = owner != null && owner.IsInputSource && owner.CurrentState != PlayerActionState.Dead;
        Unit hovered = active ? ResolveHovered() : null;
        tracker.Set(hovered, out _);
        ResolveRangeTargets(active, hovered);
        ResolveAggroTargets(active, hovered);
        SyncEntries(hovered);
        Publish(active);
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

            candidates.Add(new OutlineCandidate<Unit>(unit, SqrFlatDistance(unit, origin), CursorUnitPick.IsSelectable(unit, owner)));
        }

        SkillRangeOutlineSelection.Select(candidates, hovered, skillRangeMaxTargets, rangeTargets);
    }

    // 복제된 대상 id 가 나인 몬스터. 몬스터 목록은 주기적으로만 다시 만든다(스폰된 NetworkObject 순회).
    // 인디케이터가 떠 있어도 훑기는 계속한다 — 고르기 단계에서만 걸러야 해제 프레임에 바로 돌아온다.
    void ResolveAggroTargets(bool active, Unit hovered)
    {
        aggroTargets.Clear();
        candidates.Clear();
        if (!active || aggroMaxTargets <= 0 || !owner.IsSpawned)
            return;

        if (Time.unscaledTime >= nextMonsterScan)
        {
            nextMonsterScan = Time.unscaledTime + MonsterScanInterval;
            ScanMonsters();
        }

        ulong localId = owner.NetworkObjectId;
        Vector3 origin = owner.transform.position;
        for (int i = 0; i < monsters.Count; i++)
        {
            MonsterBase monster = monsters[i];
            if (monster == null || !monster.IsSpawned)
                continue;

            ulong targetId = monster.AggroTargetId;
            if (targetId != localId)
                continue;

            bool targeting = MonsterAggroTargetRule.IsTargetingLocal(targetId, localId, CursorUnitPick.IsSelectable(monster, owner));
            candidates.Add(new OutlineCandidate<Unit>(monster, SqrFlatDistance(monster, origin), targeting));
        }

        MonsterAggroTargetRule.SelectAggro(candidates, hovered, rangeTargets, aggroMaxTargets, aggroTargets,
            SkillIndicatorActive);
    }

    void ScanMonsters()
    {
        monsters.Clear();
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || nm.SpawnManager == null)
            return;

        foreach (NetworkObject no in nm.SpawnManager.SpawnedObjectsList)
        {
            if (no != null && no.TryGetComponent(out MonsterBase monster))
                monsters.Add(monster);
        }
    }

    static float SqrFlatDistance(Unit unit, Vector3 origin)
    {
        Vector3 offset = unit.transform.position - origin;
        offset.y = 0f;
        return offset.sqrMagnitude;
    }

    // 세트에서 빠진 몬스터는 풀로, 새로 든 몬스터는 렌더러를 한 번 모은다. 색 전환은 인덱스만 바꾼다.
    void SyncEntries(Unit hovered)
    {
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            Entry entry = entries[i];
            int color = DesiredColor(entry.Unit, hovered);
            if (color < 0)
            {
                Release(i);
                continue;
            }
            entry.ColorIndex = color;
        }

        if (hovered != null && FindEntry(hovered) < 0)
            Acquire(hovered, EnemyOutlineDrawList.PrimaryColorIndex);
        for (int i = 0; i < rangeTargets.Count; i++)
            if (FindEntry(rangeTargets[i]) < 0)
                Acquire(rangeTargets[i], EnemyOutlineDrawList.PrimaryColorIndex);
        for (int i = 0; i < aggroTargets.Count; i++)
            if (FindEntry(aggroTargets[i]) < 0)
                Acquire(aggroTargets[i], EnemyOutlineDrawList.AggroColorIndex);
    }

    // -1 = 세트 밖. 빨강(호버·범위) > 회색(나를 노림).
    int DesiredColor(Unit unit, Unit hovered)
    {
        if (unit == null)
            return -1;
        if (unit == hovered || Contains(rangeTargets, unit))
            return EnemyOutlineDrawList.PrimaryColorIndex;
        if (Contains(aggroTargets, unit))
            return EnemyOutlineDrawList.AggroColorIndex;
        return -1;
    }

    static bool Contains(List<Unit> list, Unit unit)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i] == unit)
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

    void Acquire(Unit unit, int colorIndex)
    {
        Entry entry = pool.Count > 0 ? pool.Pop() : new Entry();
        entry.Unit = unit;
        entry.ColorIndex = colorIndex;
        entry.Renderers.Clear();
        EnemyOutlineRenderers.Collect(unit.transform, entry.Renderers, rendererScratch);
        entries.Add(entry);
    }

    void Release(int index)
    {
        Entry entry = entries[index];
        entry.Unit = null;
        entry.Renderers.Clear();
        entries.RemoveAt(index);
        pool.Push(entry);
    }

    void ReleaseAll()
    {
        for (int i = entries.Count - 1; i >= 0; i--)
            Release(i);
    }

    // 🔴 오너가 아닌 사본(다른 플레이어의 Player)도 이 컴포넌트를 돌린다 — 목록은 쓰던 쪽만 비운다(오너 목록을 지우지 않게).
    void Publish(bool active)
    {
        if (!active)
        {
            if (publishing)
            {
                EnemyOutlineDrawList.Clear();
                publishing = false;
            }
            return;
        }

        publishing = true;
        EnemyOutlineDrawList.Begin(outlineColor, aggroOutlineColor, outlineWidthPixels);
        for (int i = 0; i < entries.Count; i++)
        {
            Entry entry = entries[i];
            for (int r = 0; r < entry.Renderers.Count; r++)
                EnemyOutlineDrawList.Add(entry.Renderers[r], entry.ColorIndex);
        }
    }

    void OnDisable()
    {
        tracker.Set(null, out _);
        rangeTargets.Clear();
        aggroTargets.Clear();
        areaFrame = -1;
        skillIndicator.Clear();
        ReleaseAll();
        Publish(false);
    }
}
