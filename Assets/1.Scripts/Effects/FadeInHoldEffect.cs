using UnityEngine;

/// <summary>
/// <b>지정 시간 동안 목표 색으로 변한 뒤, 해제될 때까지 그 색을 유지</b>하는 스프라이트 연출.
/// 예고 장판(텔레그래프)처럼 "차오르다가 멈춰서 기다리는" 연출이 대상이다.
///
/// <b>왜 파티클이 아닌가.</b> 파티클의 색 변화 수단인 Color over Lifetime은 <b>수명에 묶인 커브</b>다.
/// 루프를 켜면 입자가 재생성될 때마다 커브가 처음부터 다시 돌아 색이 깜빡이고, 루프를 끄면
/// 유지 시간이 Start Lifetime으로 상한이 걸린다. "무기한 유지"를 표현할 수가 없다.
/// 게다가 시간축이 프리팹에 굳어 있어 코드가 정할 수 없다 —
/// <see cref="FloorAreaEffect"/>가 크기에서 같은 이유로 코드 구동을 택한 것과 같은 판단이다.
///
/// <b>알파만 다룬다.</b> RGB는 프리팹에 저작된 값을 그대로 두므로 색 결정은 아티스트에게 남고,
/// 코드는 "얼마나 진해졌나" 하나만 책임진다.
///
/// ⚠️ <b>시작 알파를 <c>Awake</c>에 캐시하지 않는다.</b> 풀 인스턴스는 재사용되므로 최초 1회만 도는
/// <c>Awake</c>에 기록하면 2회차부터 어긋난다. 시작 알파는 <see cref="startAlpha"/>가 정본이고
/// <see cref="ResetForPool"/>이 매번 되돌린다.
///
/// ⚠️ 스프라이트는 <b>반드시 자식</b>에 두고 눕히는 회전(X -90)도 자식에 건다.
/// <c>EffectManager</c>가 대출 시 루트를 <c>SetPositionAndRotation</c>으로 덮어써서
/// 루트에 건 회전은 통째로 사라진다.
/// </summary>
[DisallowMultipleComponent]
public class FadeInHoldEffect : MonoBehaviour
{
    [Tooltip("알파를 조절할 스프라이트들. 비워두면 자식에서 자동 수집한다")]
    [SerializeField] SpriteRenderer[] targets;

    [Tooltip("재생 시작 알파")]
    [SerializeField, Range(0f, 1f)] float startAlpha = 0.15f;

    [Tooltip("도달 알파. 여기 도달한 뒤에는 해제될 때까지 그대로 유지된다")]
    [SerializeField, Range(0f, 1f)] float targetAlpha = 0.7f;

    [Tooltip("시작 색에서 목표 색까지 가는 시간(초).\n" +
             "드라이버가 런타임 시간(partDuration)을 넘겨주면 그쪽이 우선한다 — " +
             "재생 시점에 시간이 정해지는 연출용 통로다")]
    [SerializeField, Min(0.01f)] float fadeDuration = 0.6f;

    // 히트스톱 배율. EffectManager가 SetPlayRate로 전달한다. 0 = 완전 정지.
    // Time.deltaTime을 그냥 쓰면 이펙트만 히트스톱을 무시하고 혼자 진행한다.
    float _playRate = 1f;

    float _duration;
    float _timer;
    bool _fading;

    void Update()
    {
        if (!_fading) return;

        if (_duration <= 0f)
        {
            ApplyAlpha(targetAlpha);
            _fading = false;
            return;
        }

        _timer += (Time.deltaTime * _playRate) / _duration;

        if (_timer >= 1f)
        {
            _timer = 1f;
            _fading = false;   // 여기서 멈춘다 = 해제될 때까지 목표 알파 유지
        }

        ApplyAlpha(Mathf.Lerp(startAlpha, targetAlpha, _timer));
    }

    /// <summary>
    /// 풀에서 대출된 인스턴스의 변색 시작. 드라이버가 부른다.
    /// </summary>
    /// <param name="duration">
    /// 런타임으로 정해진 시간(초). 0이면 "시간을 주지 않았다"는 뜻이라
    /// 프리팹에 저작된 <see cref="fadeDuration"/>을 쓴다.
    /// </param>
    public void BeginFade(float duration)
    {
        CollectTargets();

        _duration = duration > 0f ? duration : fadeDuration;
        _timer = 0f;
        _fading = true;

        ApplyAlpha(startAlpha);
    }

    /// <summary>히트스톱 배율. 0이면 변색이 멈춘다.</summary>
    public void SetPlayRate(float rate) => _playRate = Mathf.Max(0f, rate);

    /// <summary>변색을 그 자리에서 멈춘다. <b>알파는 건드리지 않는다</b> — 오른 만큼은 남는다.</summary>
    public void StopFade() => _fading = false;

    /// <summary>
    /// 풀 반납 직전 초기화. <b>알파를 반드시 되돌린다</b> —
    /// 안 되돌리면 다음 대출자가 이미 목표 알파인 채로 시작한다.
    /// </summary>
    public void ResetForPool()
    {
        _fading = false;
        _timer = 0f;
        _playRate = 1f;
        _duration = 0f;

        CollectTargets();
        ApplyAlpha(startAlpha);
    }

    void CollectTargets()
    {
        if (targets != null && targets.Length > 0) return;

        targets = GetComponentsInChildren<SpriteRenderer>(true);
    }

    /// <summary>
    /// 알파만 덮어쓴다. <b>RGB는 프리팹에 저작된 값을 그대로 둔다</b> —
    /// 색은 아티스트 영역이고 코드가 다룰 것은 "얼마나 진해졌나" 하나다.
    /// 읽어서 알파만 바꿔 되쓰므로 풀 재사용에도 RGB가 보존된다.
    ///
    /// <c>SpriteRenderer.color</c>는 렌더러별 내장 프로퍼티라 머티리얼 인스턴스를 만들지 않는다.
    /// (<c>sharedMaterial</c>을 건드리면 에셋이 오염되고 그대로 커밋된다.)
    /// </summary>
    void ApplyAlpha(float alpha)
    {
        if (targets == null) return;

        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] == null) continue;

            Color c = targets[i].color;
            c.a = alpha;
            targets[i].color = c;
        }
    }
}
