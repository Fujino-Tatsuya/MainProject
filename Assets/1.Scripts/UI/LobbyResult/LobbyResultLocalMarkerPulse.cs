using EuniTween;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로비 결과창 "나" 행 강조(<c>Marker_Local</c>) 은은한 맥동(할 일 222) — 표시 전용.
/// 켜질 때(= <see cref="ResultPlayerRowView.Bind"/> 가 로컬 행만 켬) 가장 밝고 크게 시작해, <c>period</c> 마다
/// 투명도 1 ↔ <c>minAlpha</c> 와 테 바깥 여백 +<c>outsetBreath</c> ↔ 0 을 오간다(EuniTween 이징, unscaled).
/// 자기 Image 색 알파와 자기 RectTransform 여백만 바꾼다 — 행 숫자 카운트업·패널 펀치(부모 크기)와 겹쳐도 서로 안 건드린다.
/// <c>pulse</c> 를 끄면 저작 값 그대로 멈춘다. 연결 = <c>Tools/UI/Authoring/Lobby Result Panel</c>(행 프리팹 재생성).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public sealed class LobbyResultLocalMarkerPulse : MonoBehaviour
{
    [Tooltip("맥동 켜기. 끄면 저작 값(가장 밝고 큰 모습) 그대로.")]
    [SerializeField] private bool pulse = true;
    [Tooltip("한 번 밝음→어두움→밝음 주기(초, unscaled). 0 이하면 맥동 없음.")]
    [SerializeField] private float period = 1.6f;
    [Tooltip("가장 어두울 때 투명도 배율(저작 색 알파에 곱한다). 1 = 투명도 맥동 없음.")]
    [Range(0f, 1f)]
    [SerializeField] private float minAlpha = 0.55f;
    [Tooltip("가장 밝을 때 테가 행 밖으로 더 나오는 정도(UI px, 사방). 0 = 크기 맥동 없음.")]
    [SerializeField] private float outsetBreath = 3f;
    [Tooltip("맥동 이징(EuniTween) — 밝음→어두움 절반에 적용, 돌아오는 절반은 거울.")]
    [SerializeField] private Ease ease = Ease.InOutSine;

    private Image _image;
    private RectTransform _rect;
    private bool _captured;
    private float _baseAlpha;
    private Vector2 _baseOffsetMin;
    private Vector2 _baseOffsetMax;
    private float _elapsed;

    private void OnEnable()
    {
        Capture();
        _elapsed = 0f;
        Apply();
    }

    private void OnDisable()
    {
        if (_captured)
            ApplyDim(0f); // 저작 값으로 — 다음에 켜질 때 처음부터
    }

    private void Update()
    {
        _elapsed += Time.unscaledDeltaTime;
        Apply();
    }

    private void Apply()
    {
        bool active = pulse && period > 0f;
        ApplyDim(active ? LobbyResultPanelAnimMath.PingPongPulse(_elapsed, period, ease) : 0f);
    }

    // dim 0 = 저작 값(가장 밝음 + 여백 최대), 1 = 가장 어두움 + 여백 기본.
    private void ApplyDim(float dim)
    {
        Color color = _image.color;
        color.a = _baseAlpha * Mathf.Lerp(1f, minAlpha, dim);
        _image.color = color;

        float extra = outsetBreath * (1f - dim);
        _rect.offsetMin = _baseOffsetMin - new Vector2(extra, extra);
        _rect.offsetMax = _baseOffsetMax + new Vector2(extra, extra);
    }

    private void Capture()
    {
        if (_captured)
            return;
        _captured = true;
        _image = GetComponent<Image>();
        _rect = (RectTransform)transform;
        _baseAlpha = _image.color.a;
        _baseOffsetMin = _rect.offsetMin;
        _baseOffsetMax = _rect.offsetMax;
    }
}
