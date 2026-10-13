using UnityEngine;

/// <summary>
/// 공격 직전 부위 발광(PLAN-monster-tells M2) — 주황 → 빨강 → 공격(타격 순간 꺼짐). Chomp 입 · Humanoid 도끼.
///
/// 🔴 <b>표시 전용, 모든 피어</b>(서버 가드 없음). 네트워크를 따로 쓰지 않는다:
///    공격 시작 = 복제되는 몬스터 상태(<see cref="MonsterBase.State"/> == Attack), 진행 = 각 피어 애니메이터의 현재 상태·정규화 시간.
///    그래서 클라에서도 자기 화면의 애니와 정확히 맞는다.
/// 🔴 발광 = 대상 렌더러에 <b>재질 슬롯을 하나 덧붙여</b> 같은 메시를 가산으로 한 번 더 그린다(<c>Monster/AttackGlow</c>).
///    원래 재질을 건드리지 않아 피격 섬광(HitFlash)과 부딪히지 않고, 빌드에서 빠질 수 있는 FlatKit 에미션 변형에도 기대지 않는다.
///    꺼져 있을 때는 슬롯을 떼어 추가 드로우가 없다.
/// 타이밍(애니 상태 이름 기준):
///    예비동작 상태 = 주황 → (진행 <see cref="redAt"/>) 빨강 / 타격 상태 = 빨강 유지 → 정규화 시간 <see cref="hitNormalizedTime"/>(OnAttackHit 위치)에서 꺼짐.
///    Attack 상태를 벗어나면 무조건 끈다(해제를 한 곳에서 — 연타 몹의 깜빡임·남은 발광 방지).
/// </summary>
[DisallowMultipleComponent]
public sealed class MonsterAttackTell : MonoBehaviour
{
    [Header("발광할 부위")]
    [Tooltip("발광시킬 렌더러(입·도끼). 비우면 아래 이름으로 자식에서 찾는다 — 중첩 아트 프리팹 안이면 이름으로 둘 것.")]
    [SerializeField] Renderer target;
    [Tooltip("target 이 비었을 때 찾을 자식 오브젝트 이름(예: G_SlendertBot_Weapon · ChompBot_Jaw).")]
    [SerializeField] string targetName;

    [Header("색 (Play 중 바로 반영)")]
    [Tooltip("공격 시작 — 주황.")]
    [SerializeField, ColorUsage(false, true)] Color warnColor = new Color(1f, 0.42f, 0.02f) * 2.2f;
    [Tooltip("곧 맞는다 — 빨강.")]
    [SerializeField, ColorUsage(false, true)] Color dangerColor = new Color(1f, 0.04f, 0.02f) * 2.6f;
    [Tooltip("면 전체 밝기(0 = 테두리만 빛남, 1 = 면 전체).")]
    [SerializeField, Range(0f, 1f)] float fill = 0.45f;
    [Tooltip("테두리 집중도(클수록 가장자리만).")]
    [SerializeField, Range(0.5f, 8f)] float rimPower = 2f;

    [Header("타이밍 (애니메이터 상태 이름 — 레이어 0)")]
    [Tooltip("예비동작 상태 이름. 이 동안 주황 → 빨강.")]
    [SerializeField] string anticipationState = "AttackAnticipation";
    [Tooltip("타격 상태 이름. 빨강 유지 후 hitNormalizedTime 에서 끈다.")]
    [SerializeField] string strikeState = "AttackStrike";
    [Tooltip("예비동작 진행 중 이 비율에서 완전히 빨강(0~1). 남은 구간은 빨강 유지.")]
    [SerializeField, Range(0.05f, 1f)] float redAt = 0.65f;
    [Tooltip("타격 상태에서 OnAttackHit 이벤트의 정규화 시간 — 여기서 끈다. (Chomp Attack 0.357 · Humanoid AttackStrike 0.102)")]
    [SerializeField, Range(0f, 1f)] float hitNormalizedTime = 0.1f;

    static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
    static readonly int FillId = Shader.PropertyToID("_Fill");
    static readonly int RimPowerId = Shader.PropertyToID("_RimPower");

    MonsterBase _owner;
    Animator _animator;
    Material _glow;
    Material[] _baseMaterials;
    bool _attached;
    int _anticipationHash, _strikeHash;
    bool _warnedMissing;

    void Awake()
    {
        _owner = GetComponentInParent<MonsterBase>();
        _animator = GetComponentInChildren<Animator>(true);
        _anticipationHash = Animator.StringToHash(anticipationState);
        _strikeHash = Animator.StringToHash(strikeState);
    }

    void OnValidate()
    {
        _anticipationHash = Animator.StringToHash(anticipationState);
        _strikeHash = Animator.StringToHash(strikeState);
    }

    bool ResolveTarget()
    {
        if (target != null) return true;
        if (!string.IsNullOrEmpty(targetName))
        {
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
                if (r.name == targetName) { target = r; return true; }
        }
        if (!_warnedMissing)
        {
            _warnedMissing = true;
            Debug.LogWarning($"[AttackTell] {name}: 발광 대상 렌더러를 못 찾았다(target 비었고 이름 '{targetName}' 없음) — 발광 없이 공격한다.", this);
        }
        return false;
    }

    void LateUpdate()
    {
        if (_owner == null || _animator == null) return;

        if (_owner.State != MonsterState.Attack) { Detach(); return; }

        // 진행도: 예비동작 = 주황→빨강, 타격 = 빨강 → 히트에서 끔, 그 밖(트리거 직후 전이 중) = 주황.
        AnimatorStateInfo info = _animator.GetCurrentAnimatorStateInfo(0);
        Color c;
        if (info.shortNameHash == _anticipationHash)
        {
            float t = Mathf.Clamp01(info.normalizedTime / redAt);
            c = Color.Lerp(warnColor, dangerColor, t);
        }
        else if (info.shortNameHash == _strikeHash)
        {
            if (info.normalizedTime >= hitNormalizedTime) { Detach(); return; }
            c = dangerColor;
        }
        else
        {
            c = warnColor;
        }
        Apply(c);
    }

    void Apply(Color c)
    {
        if (!_attached && !Attach()) return;
        _glow.SetColor(GlowColorId, c);
        _glow.SetFloat(FillId, fill);
        _glow.SetFloat(RimPowerId, rimPower);
    }

    bool Attach()
    {
        if (!ResolveTarget()) return false;
        if (_glow == null)
        {
            Material src = Resources.Load<Material>("Telegraph/MA_AttackGlow");
            if (src != null) _glow = new Material(src) { name = "AttackGlow (Instance)" };
            else
            {
                Shader s = Shader.Find("Monster/AttackGlow");
                if (s == null) return false;
                _glow = new Material(s) { name = "AttackGlow (Instance)" };
            }
        }
        // 🔴 덧붙인 슬롯은 마지막 서브메시에 붙는다 — 대상은 서브메시 1개짜리 렌더러여야 한다(도끼·분리한 턱).
        _baseMaterials = target.sharedMaterials;
        var mats = new Material[_baseMaterials.Length + 1];
        _baseMaterials.CopyTo(mats, 0);
        mats[mats.Length - 1] = _glow;
        target.sharedMaterials = mats;
        _attached = true;
        return true;
    }

    void Detach()
    {
        if (!_attached) return;
        _attached = false;
        if (target != null && _baseMaterials != null) target.sharedMaterials = _baseMaterials;
    }

    void OnDisable() => Detach();

    void OnDestroy()
    {
        Detach();
        if (_glow != null) Destroy(_glow);
    }
}
