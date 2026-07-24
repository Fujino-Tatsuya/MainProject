using UnityEngine;

public enum AreaType
{
    None,
    GrowOnOverlap,
    GrowOverTime
}

public class FloorAreaEffect : MonoBehaviour
{
    [SerializeField] bool canGrowOnOverlap;
    [SerializeField] float growAmount;
    [SerializeField] Vector3 maxScale;
    [SerializeField] AreaType floorType;
    //[SerializeField] float maxTime;
    float maxTime = 0f;
    bool _startToGrow = false;
    float _timer = 0f;
    Vector3 _startSize;

    public bool CanGrowOnOverlap { get { return canGrowOnOverlap; } }
    public AreaType FloorType { get { return floorType; } }

    private void Awake()
    {
        _startSize = transform.localScale;
    }

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

        _timer += Time.deltaTime / maxTime;

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

    public void OverTimeGrow()
    {
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
        _timer = 0f;
        transform.localScale = _startSize;
        _startToGrow = true;
    }

}
