using UnityEngine;

/// <summary>
/// 몬스터 상태이상 머리 위 루프 이펙트(팀장 10-26 — 팔라딘 Q 밀림·경직 동안 기절 이펙트).
/// 플레이어용 <see cref="StatusEffectVfxPresenter"/>(민경 10-20)와 같은 표 형식(<see cref="StatusEffectVfxSet"/>)을 쓰는 몬스터판이다.
///
/// - 프리팹에 붙이지 않는다 — <see cref="MonsterBase"/> 스폰 때 <see cref="TryInstall"/> 가 런타임에 붙인다(체력바와 같은 방식).
///   표는 <c>Resources/MonsterStatusEffectVfxSet</c> 하나 — 플레이어 표와 따로 둬서 몬스터 쪽 자리·배율을 따로 맞춘다.
/// - 켜고 끄기 = <see cref="MonsterStatusEffect.EffectsChanged"/>(복제된 활성 상태 변화)·상태 전이(사망) — 전 피어에서 나므로 <b>추가 네트워크 없음</b>.
/// - 머리 위 자리는 루트 자식으로 만들고, 머리 뼈("Head")가 있으면 매 프레임 그 위로 따라간다(없으면 몸 콜라이더 꼭대기 고정).
///   몸·발밑 자리는 아직 쓰지 않아 몸통 중심·루트로 둔다.
/// - 자리마다 우선순위 최고 1개, 다른 자리끼리는 동시(플레이어판과 같은 규칙).
///
/// 🔴 몬스터에 기절(Stunned)을 거는 곳은 지금 팔라딘 Q 밀림·경직(<c>MonsterBase.TryEnterKnockback/ExitKnockback</c>)과
///    고정 터렛 경직뿐이다 — 평타 경직(Hit)은 상태이상을 걸지 않으므로 평타마다 별이 뜨지 않는다.
///    중간보스 간파 성공 그로기(1초)는 상태이상이 아니지만 같은 기절 줄로 보여 준다. 취약 8초(<see cref="MonsterBase.IsMidBossVulnerable"/>)는
///    하늘색으로만 보이고 별은 안 띄운다(팀장 10-26 — 별은 실제로 멈춰 있을 때만).
/// </summary>
[DisallowMultipleComponent]
public sealed class MonsterStatusEffectVfxPresenter : MonoBehaviour
{
    public const string ResourcePath = "MonsterStatusEffectVfxSet";

    const int AnchorCount = 3;
    const float FallbackHeadHeight = 2f;   // 콜라이더·렌더러가 없을 때 머리 높이(m)
    const string HeadBoneName = "Head";
    const float HeadTopPad = 0.4f;         // 머리 뼈(대개 머리 중심) → 머리 꼭대기 근사(m). 그 위에 표의 offset 이 더해진다

    static bool _warnedMissingSet;

    MonsterBase _monster;
    MonsterStatusEffect _status;
    StatusEffectVfxSet _set;
    Transform _overhead;
    Transform _body;
    Transform _headBone;      // 있으면 머리 위 자리가 매 프레임 따라간다
    float _minHeadHeight;     // 루트 기준 최소 머리 위 높이(스폰 때 잰 콜라이더 꼭대기)
    readonly StatusEffectVfxSet.Row[] _shownRows = new StatusEffectVfxSet.Row[AnchorCount];
    readonly EffectHandle[] _handles = new EffectHandle[AnchorCount];

    /// <summary>
    /// 몬스터에 붙인다(멱등 — 풀 재사용 때는 있는 것을 돌려준다). 23호(<c>rank: Boss</c>)·상태이상 컴포넌트가 없는 몹은 붙이지 않는다.
    /// </summary>
    public static MonsterStatusEffectVfxPresenter TryInstall(MonsterBase monster, MonsterStatusEffect status, Collider bodyCollider)
    {
        if (monster == null || status == null || monster.Rank == MonsterRank.Boss)
            return null;

        if (monster.TryGetComponent(out MonsterStatusEffectVfxPresenter existing))
        {
            existing.Refresh();
            return existing;
        }

        var set = Resources.Load<StatusEffectVfxSet>(ResourcePath);
        if (set == null)
        {
            if (!_warnedMissingSet)
            {
                _warnedMissingSet = true;
                Debug.LogWarning($"[MonsterStatusVfx] Resources/{ResourcePath} 가 없어 몬스터 상태이상 이펙트를 붙이지 않는다.");
            }
            return null;
        }

        float headY = MeasureHeadY(monster, bodyCollider);

        // AddComponent 가 OnEnable 을 바로 부른다 — 그때는 필드가 비어 있어 아무것도 안 하고, 구독·첫 계산은 아래 Bind 가 한다.
        // (스폰된 NetworkObject 를 껐다 켜서 순서를 미루면 NetworkBehaviour 전부가 OnDisable 을 탄다 — 쓰지 말 것.)
        var presenter = monster.gameObject.AddComponent<MonsterStatusEffectVfxPresenter>();
        presenter.Bind(monster, status, set,
            CreateAnchor(monster.transform, "StatusVfx_Overhead", headY),
            CreateAnchor(monster.transform, "StatusVfx_Body", (monster.transform.position.y + headY) * 0.5f),
            FindHeadBone(monster.transform), headY - monster.transform.position.y);
        return presenter;
    }

    void Bind(MonsterBase monster, MonsterStatusEffect status, StatusEffectVfxSet set, Transform overhead, Transform body,
              Transform headBone, float minHeadHeight)
    {
        _monster = monster;
        _status = status;
        _set = set;
        _overhead = overhead;
        _body = body;
        _headBone = headBone;
        _minHeadHeight = minHeadHeight;
        FollowHead();
        _status.EffectsChanged += Refresh;
        Refresh();
    }

    // 머리 뼈(이름 "Head" — 로봇 팩 공통: GauntletBot·SpinnerBot·WallBot 등). 없으면 null → 고정 높이 자리를 쓴다.
    // 🔴 왜 뼈를 따라가나(경석 10-26 Play): 몸 콜라이더는 몹마다 2m 캡슐 그대로인데 GauntletBot 은 골반만 1.7m 인 큰 모델이라
    //    콜라이더 꼭대기 자리는 가슴 속이었다 — 별이 몸에 묻혀 중간보스에서만 안 보였다.
    static Transform FindHeadBone(Transform root)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (string.Equals(t.name, HeadBoneName, System.StringComparison.OrdinalIgnoreCase))
                return t;
        return null;
    }

    void LateUpdate() => FollowHead();

    // 머리 위 자리를 머리 뼈 위(HeadTopPad)로 옮긴다. 애니메이션이 뼈를 움직인 뒤(LateUpdate)라 숙이거나 밀려도 따라간다.
    // 높이는 스폰 때 잰 콜라이더 꼭대기 밑으로는 내리지 않는다 — 머리가 작은 몹에서 별이 몸에 겹치지 않게.
    void FollowHead()
    {
        if (_headBone == null || _overhead == null) return;
        Vector3 head = _headBone.position;
        float y = Mathf.Max(head.y + HeadTopPad, transform.position.y + _minHeadHeight);
        _overhead.position = new Vector3(head.x, y, head.z);
    }

    // 몸 콜라이더 꼭대기(월드 y). 스폰 직후 콜라이더가 꺼져 있으면 bounds 가 0 이라 렌더러로 넘어간다.
    static float MeasureHeadY(MonsterBase monster, Collider bodyCollider)
    {
        if (bodyCollider != null && bodyCollider.enabled && bodyCollider.bounds.size.y > 0.01f)
            return bodyCollider.bounds.max.y;

        Renderer[] renderers = monster.GetComponentsInChildren<Renderer>();
        bool found = false;
        Bounds bounds = default;
        foreach (Renderer r in renderers)
        {
            if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
            if (!found) { bounds = r.bounds; found = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return found ? bounds.max.y : monster.transform.position.y + FallbackHeadHeight;
    }

    static Transform CreateAnchor(Transform root, string anchorName, float worldY)
    {
        var go = new GameObject(anchorName);
        Transform t = go.transform;
        t.SetParent(root, false);
        t.position = new Vector3(root.position.x, worldY, root.position.z);
        t.rotation = root.rotation;
        return t;
    }

    void OnEnable()
    {
        if (_status != null)
            _status.EffectsChanged += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        if (_status != null)
            _status.EffectsChanged -= Refresh;
        ReleaseAll(immediate: false);
    }

    // 디스폰·씬 언로드 — 따라갈 대상이 사라지므로 outro 없이 바로 반납한다.
    void OnDestroy() => ReleaseAll(immediate: true);

    /// <summary>[전 피어] 지금 활성 상태로 다시 계산한다. 사망·휴면이면 전부 끈다.</summary>
    public void Refresh()
    {
        if (!isActiveAndEnabled || _monster == null)
            return;

        var wanted = new StatusEffectVfxSet.Row[AnchorCount];
        bool alive = _monster.State != MonsterState.Dead;
        if (alive && _status != null && _set != null)
        {
            StatusEffectType active = _status.Active;
            // 중간보스 간파 성공 그로기(상태이상이 아니라 FSM 상태다)도 기절 이펙트로 보여 준다(팀장 10-26).
            // 취약 8초 전체가 아니다 — 취약은 하늘색만, 별은 실제로 멈춰 있는 그로기·Q 경직 때만.
            if (_monster.Rank == MonsterRank.MidBoss && _monster.State == MonsterState.Groggy)
                active |= StatusEffectType.Stunned;
            foreach (StatusEffectVfxSet.Row row in _set.Rows)
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
            if (wanted[slot] == _shownRows[slot])
                continue;
            Release(slot, immediate: false);
            if (wanted[slot] != null)
                Play(slot, wanted[slot]);
        }
    }

    void Play(int slot, StatusEffectVfxSet.Row row)
    {
        if (!EffectManager.TryGet(out EffectManager effects, this))
            return;

        _handles[slot] = effects.PlayLooping(row.effect, ResolveAnchor(row.anchor), row.offset, row.scale);
        _shownRows[slot] = row;
    }

    Transform ResolveAnchor(StatusEffectVfxSet.Anchor anchor)
    {
        switch (anchor)
        {
            case StatusEffectVfxSet.Anchor.Overhead: return _overhead;
            case StatusEffectVfxSet.Anchor.Body: return _body;
            default: return transform;
        }
    }

    void Release(int slot, bool immediate)
    {
        if (_handles[slot].IsSet && EffectManager.Instance != null)
        {
            if (immediate)
                EffectManager.Instance.ReleaseImmediate(_handles[slot]);
            else
                EffectManager.Instance.Release(_handles[slot]);
        }
        _handles[slot] = EffectHandle.None;
        _shownRows[slot] = null;
    }

    void ReleaseAll(bool immediate)
    {
        for (int slot = 0; slot < AnchorCount; slot++)
            Release(slot, immediate);
    }
}
