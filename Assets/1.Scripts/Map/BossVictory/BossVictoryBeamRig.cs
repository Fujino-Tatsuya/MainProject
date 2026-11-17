using UnityEngine;

/// <summary>
/// 보스 처치 연출 전용 레이저 이펙트 프리팹의 루트(할 일 302). 거너 프로필 <c>gunnerQ.victoryBeamPrefab</c> 에 넣으면
/// <see cref="GunnerBeamView"/> 가 발사 때 꺼내 매 프레임 <see cref="Drive"/> 로 위치·길이·페이드를 맞춘다(표시 전용, 각 피어 — 새 RPC 없음).
///
/// 저작 규칙(은희 — 이펙트 모양은 프리팹·머티리얼에서 자유롭게):
///  · 루트 = 총구(레이저 시작점), 루트 +Z = 레이저 방향. 코드가 루트를 총구에 놓고 끝점을 바라보게 돌린다.
///  · <b>Body</b> = +Z 방향 1m 로 만든다 — 코드가 Body 의 Z 스케일 = 저작 Z × 길이(m). 굵기(X·Y)는 저작 값 그대로.
///  · <b>Muzzle</b> = 총구 고정(늘이지 않음) — 총구 섬광·플레어.
///  · <b>End</b> = 코드가 레이저 끝점으로 옮김 — 끝 플레어·착탄·관통 이펙트.
///  · 색·흐름은 머티리얼 값 그대로. 코드는 <b>밝기·페이드</b>만 덮는다 — 렌더러 머티리얼의 첫 색 칸(_Color → _BaseColor → _TintColor)에
///    저작 색 × 밝기(rgb), 알파 × 페이드(셰이더가 알파로 사라지게 — 뼈대 셰이더는 rgb × 알파). 색 칸이 없는 렌더러는 건너뛴다.
///  · 셰이더가 <c>_BeamLength</c>(m)·<c>_BeamTime</c>(초, 멈춤 동안 멈춤) 칸을 가지면 코드가 매 프레임 넣는다(뼈대 셰이더 BossVictoryGunnerBeam 이 씀).
///    셰이더가 _Time 을 쓰면 멈춤 화면에서도 계속 흐른다.
///  · 파티클은 루프로 만들어도 된다 — 발사 때 처음부터 재생, 레이저 끝에 오브젝트를 끈다. 멈춤(<c>victoryBeamFreeze</c>)이면 Pause.
/// </summary>
[DisallowMultipleComponent]
public sealed class BossVictoryBeamRig : MonoBehaviour
{
    [Tooltip("+Z 1m 로 만든 몸통 — Z 스케일 = 레이저 길이(m). 비우면 길이 늘이기 없음.")]
    [SerializeField] private Transform body;
    [Tooltip("총구 고정(늘이지 않음) — 총구 섬광·플레어. 위치는 저작 그대로(루트 = 총구).")]
    [SerializeField] private Transform muzzle;
    [Tooltip("레이저 끝점으로 옮기는 그룹 — 끝 플레어·착탄.")]
    [SerializeField] private Transform end;
    [Tooltip("_BeamTime 시계를 실제 시간으로(처치 연출 시간 배율 무시). 끄면 게임 시간(파티클과 같음).")]
    [SerializeField] private bool unscaledTime;

    public Transform Body => body;
    public Transform Muzzle => muzzle;
    public Transform End => end;

    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int TintColorId = Shader.PropertyToID("_TintColor");
    static readonly int BeamLengthId = Shader.PropertyToID("_BeamLength");
    static readonly int BeamTimeId = Shader.PropertyToID("_BeamTime");

    struct Slot
    {
        public Renderer renderer;
        public int colorId;      // 0 = 색 칸 없음
        public Color home;
        public bool hasLength, hasTime;
    }

    Slot[] _slots;
    ParticleSystem[] _systems;
    Vector3 _bodyHome = Vector3.one;
    MaterialPropertyBlock _block;
    float _clock;
    bool _frozen;

    void Awake() => Collect();

    void Collect()
    {
        if (_slots != null) return;
        if (body != null) _bodyHome = body.localScale;
        _systems = GetComponentsInChildren<ParticleSystem>(true);
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        _slots = new Slot[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer r = renderers[i];
            Material m = r.sharedMaterial;
            var s = new Slot { renderer = r };
            if (m != null)
            {
                s.colorId = m.HasProperty(ColorId) ? ColorId : m.HasProperty(BaseColorId) ? BaseColorId : m.HasProperty(TintColorId) ? TintColorId : 0;
                if (s.colorId != 0) s.home = m.GetColor(s.colorId);
                s.hasLength = m.HasProperty(BeamLengthId);
                s.hasTime = m.HasProperty(BeamTimeId);
            }
            _slots[i] = s;
        }
    }

    /// <summary>발사 — 켜고 파티클을 처음부터 재생, 시계 0.</summary>
    public void Begin()
    {
        Collect();
        _clock = 0f;
        _frozen = false;
        gameObject.SetActive(true);
        for (int i = 0; i < _systems.Length; i++)
        {
            ParticleSystem ps = _systems[i];
            if (ps == null) continue;
            ps.Clear(false);
            ps.Play(false);
        }
    }

    /// <summary>매 프레임 — 총구 start → 끝 endPoint 로 놓고, 밝기·페이드(0~1)·멈춤을 적용.</summary>
    public void Drive(Vector3 start, Vector3 endPoint, float fade, float brightness, bool frozen)
    {
        Collect();
        Vector3 d = endPoint - start;
        float length = d.magnitude;
        Quaternion rot = length > 1e-4f ? Quaternion.LookRotation(d / length) : transform.rotation;
        transform.SetPositionAndRotation(start, rot);
        if (body != null) body.localScale = BodyScale(_bodyHome, length);
        if (end != null) end.SetPositionAndRotation(endPoint, rot);

        SetFrozen(frozen);
        if (!_frozen) _clock += unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

        _block ??= new MaterialPropertyBlock();
        for (int i = 0; i < _slots.Length; i++)
        {
            Slot s = _slots[i];
            if (s.renderer == null || (s.colorId == 0 && !s.hasLength && !s.hasTime)) continue;
            s.renderer.GetPropertyBlock(_block);
            if (s.colorId != 0) _block.SetColor(s.colorId, Look(s.home, brightness, fade));
            if (s.hasLength) _block.SetFloat(BeamLengthId, length);
            if (s.hasTime) _block.SetFloat(BeamTimeId, _clock);
            s.renderer.SetPropertyBlock(_block);
        }
    }

    /// <summary>레이저 끝 — 끈다(멈춘 파티클은 재개해 두고 끈다).</summary>
    public void Stop()
    {
        SetFrozen(false);
        if (this != null) gameObject.SetActive(false);
    }

    void SetFrozen(bool frozen)
    {
        if (_frozen == frozen) return;
        _frozen = frozen;
        for (int i = 0; i < _systems.Length; i++)
        {
            ParticleSystem ps = _systems[i];
            if (ps == null) continue;
            if (frozen) ps.Pause(false);
            else if (ps.isPaused) ps.Play(false);
        }
    }

    /// <summary>몸통 스케일 — 굵기(X·Y) = 저작 그대로, Z = 저작 Z × 길이(m).</summary>
    public static Vector3 BodyScale(Vector3 home, float length) => new Vector3(home.x, home.y, home.z * Mathf.Max(0f, length));

    /// <summary>색 = 저작 색 × 밝기(rgb), 알파 × 페이드 — 페이드는 알파에만(셰이더가 rgb × 알파를 하면 두 번 곱해져 페이드² 로 너무 빨리 꺼진다).
    /// 뼈대 셰이더·URP 가산(SrcAlpha One)·알파 블렌드 모두 알파로 사라진다. 알파를 안 쓰는 셰이더(One One 에 rgb 만)는 페이드가 안 먹는다.</summary>
    public static Color Look(Color home, float brightness, float fade)
    {
        float k = Mathf.Max(0f, brightness);
        return new Color(home.r * k, home.g * k, home.b * k, home.a * Mathf.Clamp01(fade));
    }
}
