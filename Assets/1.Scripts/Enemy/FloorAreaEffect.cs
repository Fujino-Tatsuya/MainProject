using UnityEngine;

public enum AreaType
{
    None,
    GrowOnOverlap,
    GrowOverTime
}

/// <summary>
/// 바닥 장판의 크기 구동. 두 종류가 한 컴포넌트에 들어 있다.
///
/// <list type="bullet">
/// <item><b>GrowOnOverlap</b> — 폭탄 화염 장판. 겹칠 때마다 <see cref="OverlapGrow"/>로 계단식 증가.
/// <c>BombController</c>가 콜라이더에서 직접 찾아 부른다(풀을 타지 않는다).</item>
/// <item><b>GrowOverTime</b> — No.23 JumpAttack 예고 장판2. 지정 시간 동안 목표 크기까지 선형 성장.
/// <see cref="FloorAreaEffectSystem"/>이 <c>EffectManager</c>의 파트 드라이버로 몬다.</item>
/// </list>
///
/// ⚠️ <b>시작 크기를 <c>Awake</c>에 캐시하지 않는다.</b> 풀 인스턴스는 대출마다
/// <c>originalScale × 배율</c>로 localScale이 덮어써지는데 <c>Awake</c>는 최초 생성 때 한 번만 돈다 —
/// 캐시하면 2회차 재생부터 시작 크기가 조용히 틀어진다. 성장을 시작하는 시점에 매번 새로 잡는다.
/// </summary>
public class FloorAreaEffect : MonoBehaviour
{
    [SerializeField] bool canGrowOnOverlap;
    [SerializeField] float growAmount;
    [SerializeField] Vector3 maxScale;
    [SerializeField] AreaType floorType;

    [Tooltip("시간 성장의 시작 크기 = 목표 크기 × 이 비율. 설계상 장판2는 목표의 1/10에서 시작한다.\n" +
             "절대값이 아니라 비율인 이유: 목표 크기가 재생 시점의 배율(scale 인자)로 정해지므로 " +
             "절대값으로 두면 배율이 커질수록 성장 폭이 달라진다")]
    [SerializeField, Min(0.0001f)] float startRatio = 0.1f;

    float maxTime = 0f;
    bool _startToGrow = false;
    float _timer = 0f;
    Vector3 _startSize;

    // 히트스톱 배율. EffectManager가 SetPlayRate로 전달한다. 0 = 완전 정지.
    // Time.deltaTime을 그냥 쓰면 이펙트만 히트스톱을 무시하고 혼자 자란다.
    float _playRate = 1f;

    public bool CanGrowOnOverlap { get { return canGrowOnOverlap; } }
    public AreaType FloorType { get { return floorType; } }

    void Update()
    {
        if (!_startToGrow) return;

        // maxTime이 0 이하면 0으로 나누는 것을 방지하고 즉시 최대 크기로 완료
        if (maxTime <= 0f)
        {
            transform.localScale = maxScale;
            _timer = 1f;
            _startToGrow = false;
            return;
        }

        _timer += (Time.deltaTime * _playRate) / maxTime;

        if (_timer >= 1f)
        {
            _timer = 1f;
            _startToGrow = false;
        }

        transform.localScale = Vector3.Lerp(_startSize, maxScale, _timer);
    }

    public void OverlapGrow()
    {
        Vector3 scale = transform.localScale;

        scale.x = Mathf.Min(scale.x + growAmount, maxScale.x);
        scale.y = Mathf.Min(scale.y + growAmount, maxScale.y);
        scale.z = Mathf.Min(scale.z + growAmount, maxScale.z);

        transform.localScale = scale;
    }

    /// <summary>인스펙터에 저작된 <c>maxScale</c>까지, 현재 크기에서부터 성장한다.</summary>
    public void OverTimeGrow()
    {
        _startSize = transform.localScale;
        _timer = 0f;
        _startToGrow = true;
    }

    /// <summary>
    /// 지정 시간(duration)과 목표 크기(targetScale)로 시간 기반 성장을 (재)시작합니다.
    /// 반복 사용 시 시작 크기·타이머를 초기화하므로 매번 처음부터 다시 자랍니다.
    /// </summary>
    /// <param name="duration">시작 크기에서 목표 크기까지 자라는 데 걸리는 시간(초)</param>
    /// <param name="targetScale">성장이 끝났을 때 도달할 크기</param>
    public void StartOverTimeGrow(float duration, Vector3 targetScale)
    {
        maxTime = duration;
        maxScale = targetScale;
        _startSize = targetScale * startRatio;
        _timer = 0f;
        transform.localScale = _startSize;
        _startToGrow = true;
    }

    /// <summary>
    /// 풀에서 대출된 인스턴스의 성장 시작. <b>목표 크기는 지금의 localScale이다</b> —
    /// 풀이 대출 시점에 <c>originalScale × 배율</c>로 이미 확정해 두었으므로 읽기만 하면 된다.
    /// 그래서 드라이버가 넘겨야 하는 런타임 값은 시간 하나뿐이다.
    ///
    /// <paramref name="duration"/>은 <b>성장에 걸리는 시간</b>이다. 소멸은 별개다 —
    /// 이 이펙트는 루프로 재생되고 착지(<c>OnLanded</c> → <c>HideFloorsClientRpc</c>)에서 해제된다.
    /// </summary>
    public void BeginPooledGrow(float duration) => StartOverTimeGrow(duration, transform.localScale);

    /// <summary>히트스톱 배율. 0이면 성장이 멈춘다.</summary>
    public void SetPlayRate(float rate) => _playRate = Mathf.Max(0f, rate);

    /// <summary>성장을 그 자리에서 멈춘다. 크기는 건드리지 않는다.</summary>
    public void StopGrow() => _startToGrow = false;

    /// <summary>풀 반납 직전 초기화. 크기 복원은 풀이 한다(<c>EffectInstance.originalScale</c>).</summary>
    public void ResetForPool()
    {
        _startToGrow = false;
        _timer = 0f;
        _playRate = 1f;
        maxTime = 0f;
    }
}
