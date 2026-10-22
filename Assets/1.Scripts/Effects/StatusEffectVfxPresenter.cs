using UnityEngine;

/// <summary>
/// 플레이어 상태이상 루프 이펙트 재생기(2026-10-20 민경). 각 플레이어 Variant 의 <c>VFX/States</c> 에 붙인다.
/// 무엇을 재생할지는 <see cref="StatusEffectVfxSet"/>(공용 SO) 가, 어디에 붙일지는 이 컴포넌트의 자리 트랜스폼이 정한다.
///
/// - 켜고 끄기 = <see cref="StatusEffectController.EffectsChanged"/>(상태이상 목록 변화) · <see cref="PlayerLifeCycleController.LifeStateChanged"/>
///   (쓰러짐·부활) 를 받을 때마다 다시 계산한다. 둘 다 전 피어에서 나므로 <b>추가 네트워크 없음</b>.
///   처음 동기화(늦은 접속)·재활성에는 이벤트가 없으므로 <see cref="OnEnable"/> 에서 한 번 직접 읽는다.
/// - <b>살아 있을 때만</b> 보인다(머리 위 상태 글자와 같은 기준). 자리마다 우선순위 최고 1개, 다른 자리끼리는 동시.
/// - 루프 핸들은 이 컴포넌트가 직접 든다 — 바뀐 자리만 Release(엔트리 outro 로 흩어짐), 꺼질 때·파괴될 때 반납.
/// </summary>
[DisallowMultipleComponent]
public sealed class StatusEffectVfxPresenter : MonoBehaviour
{
    [Tooltip("상태 → 이펙트 표(플레이어 공용 PlayerStatusEffectVfxSet).")]
    [SerializeField] private StatusEffectVfxSet set;

    [Header("자리 — 캐릭터마다 지정")]
    [Tooltip("머리 위 — Armature/VFX/TopHead.")]
    [SerializeField] private Transform overhead;
    [Tooltip("몸 중심. 비어 있으면 이 자리의 줄은 건너뛴다.")]
    [SerializeField] private Transform body;
    [Tooltip("발밑. 비어 있으면 플레이어 루트.")]
    [SerializeField] private Transform feet;

    private const int AnchorCount = 3;

    private StatusEffectController statusEffects;
    private PlayerLifeCycleController lifeCycle;
    private Transform unitRoot;
    private readonly StatusEffectVfxSet.Row[] shownRows = new StatusEffectVfxSet.Row[AnchorCount];
    private readonly EffectHandle[] handles = new EffectHandle[AnchorCount];
    private readonly bool[] warnedMissingAnchor = new bool[AnchorCount];

    private void Awake()
    {
        Unit unit = GetComponentInParent<Unit>();
        unitRoot = unit != null ? unit.transform : transform.root;
        statusEffects = unit != null ? unit.StatusEffects : GetComponentInParent<StatusEffectController>();
        lifeCycle = GetComponentInParent<PlayerLifeCycleController>();

        if (statusEffects == null)
            Edit.LogWarning($"[StatusEffectVfxPresenter] '{name}' 위에서 StatusEffectController 를 찾지 못했다 — 상태이상 연출이 나오지 않는다.", this);
    }

    private void OnEnable()
    {
        if (statusEffects != null)
            statusEffects.EffectsChanged += Refresh;
        if (lifeCycle != null)
            lifeCycle.LifeStateChanged += HandleLifeStateChanged;
        Refresh();
    }

    private void OnDisable()
    {
        if (statusEffects != null)
            statusEffects.EffectsChanged -= Refresh;
        if (lifeCycle != null)
            lifeCycle.LifeStateChanged -= HandleLifeStateChanged;
        ReleaseAll(immediate: false);
    }

    // 디스폰·씬 언로드 — 따라갈 대상이 사라지므로 outro 없이 바로 반납한다.
    private void OnDestroy() => ReleaseAll(immediate: true);

    private void HandleLifeStateChanged(PlayerLifeState previous, PlayerLifeState current) => Refresh();

    private void Refresh()
    {
        if (!isActiveAndEnabled)
            return;

        var wanted = new StatusEffectVfxSet.Row[AnchorCount];
        bool alive = lifeCycle == null || lifeCycle.State == PlayerLifeState.Alive;
        if (alive && statusEffects != null && set != null)
        {
            StatusEffectType active = statusEffects.ActiveEffects;
            foreach (StatusEffectVfxSet.Row row in set.Rows)
            {
                if (row == null || row.effect == null || (active & row.status) == 0)
                    continue;
                int slot = (int)row.anchor;
                if (wanted[slot] == null || row.priority > wanted[slot].priority)
                    wanted[slot] = row;
            }
        }

        for (int slot = 0; slot < AnchorCount; slot++)
        {
            if (wanted[slot] == shownRows[slot])
                continue;
            Release(slot, immediate: false);
            if (wanted[slot] != null)
                Play(slot, wanted[slot]);
        }
    }

    private void Play(int slot, StatusEffectVfxSet.Row row)
    {
        Transform anchor = ResolveAnchor(row.anchor);
        if (anchor == null)
        {
            if (!warnedMissingAnchor[slot])
            {
                warnedMissingAnchor[slot] = true;
                Edit.LogWarning($"[StatusEffectVfxPresenter] '{name}' 에 '{row.anchor}' 자리가 비어 '{row.status}' 연출을 건너뛴다.", this);
            }
            return;
        }

        if (!EffectManager.TryGet(out EffectManager effects, this))
            return;

        handles[slot] = effects.PlayLooping(row.effect, anchor, row.offset, row.scale);
        shownRows[slot] = row;
    }

    private Transform ResolveAnchor(StatusEffectVfxSet.Anchor anchor)
    {
        switch (anchor)
        {
            case StatusEffectVfxSet.Anchor.Overhead: return overhead;
            case StatusEffectVfxSet.Anchor.Body: return body;
            default: return feet != null ? feet : unitRoot;
        }
    }

    private void Release(int slot, bool immediate)
    {
        if (handles[slot].IsSet && EffectManager.Instance != null)
        {
            if (immediate)
                EffectManager.Instance.ReleaseImmediate(handles[slot]);
            else
                EffectManager.Instance.Release(handles[slot]);
        }
        handles[slot] = EffectHandle.None;
        shownRows[slot] = null;
    }

    private void ReleaseAll(bool immediate)
    {
        for (int slot = 0; slot < AnchorCount; slot++)
            Release(slot, immediate);
    }
}
