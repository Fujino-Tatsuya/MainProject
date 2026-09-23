using TMPro;
using UnityEngine;

/// <summary>
/// PRESS ANY KEY 처럼 알파를 주기적으로 깜빡이는 텍스트.
/// </summary>
/// <remarks>
/// 타임스케일을 안 타므로 타이틀·일시정지 화면에서도 동작한다.
/// 컴포넌트를 끄면 알파를 1로 되돌려 놓는다 — 꺼진 채 투명하게 남는 사고를 막는다.
/// </remarks>
[RequireComponent(typeof(TMP_Text))]
[DisallowMultipleComponent]
public sealed class BlinkingText : MonoBehaviour
{
    [Tooltip("한 번 깜빡이는 데 걸리는 시간(초).")]
    [SerializeField, Min(0.05f)] private float _period = 1.2f;

    [Tooltip("가장 흐릴 때의 알파.")]
    [SerializeField, Range(0f, 1f)] private float _minAlpha = 0.15f;

    [Tooltip("가장 밝을 때의 알파.")]
    [SerializeField, Range(0f, 1f)] private float _maxAlpha = 1f;

    [Tooltip("켜면 부드럽게(사인), 끄면 딱딱 끊어지게(구형파) 깜빡인다. CRT 톤에는 끈 쪽이 어울린다.")]
    [SerializeField] private bool _smooth;

    private TMP_Text _text;
    private float _elapsed;

    private void Awake()
    {
        _text = GetComponent<TMP_Text>();
    }

    private void OnEnable()
    {
        _elapsed = 0f;
    }

    private void OnDisable()
    {
        if (_text != null)
            _text.alpha = 1f;
    }

    private void Update()
    {
        if (_text == null)
            return;

        _elapsed += Time.unscaledDeltaTime;

        float phase = Mathf.Repeat(_elapsed / Mathf.Max(0.05f, _period), 1f);
        float t = _smooth
            ? 0.5f - 0.5f * Mathf.Cos(phase * Mathf.PI * 2f)
            : (phase < 0.5f ? 1f : 0f);

        _text.alpha = Mathf.Lerp(_minAlpha, _maxAlpha, t);
    }
}
