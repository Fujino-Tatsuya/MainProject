using System;
using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// 무작위 글자가 앞에서부터 실제 문장으로 수렴하는 디코드 연출.
/// </summary>
/// <remarks>
/// 레퍼런스 영상 기준 수렴에 약 0.9~1.2초가 걸린다(<see cref="_duration"/> 기본값의 근거).
/// 셰이더가 아니라 순수 문자열 치환이므로 World Space·Overlay 어느 쪽에서도 똑같이 동작한다.
/// </remarks>
[RequireComponent(typeof(TMP_Text))]
[DisallowMultipleComponent]
public sealed class TextScramble : MonoBehaviour
{
    private const string DefaultGlyphs = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ#$%&*+=<>/\\";

    [Tooltip("수렴에 걸리는 시간(초).")]
    [SerializeField, Min(0.05f)] private float _duration = 1.0f;

    [Tooltip("글자가 바뀌는 간격(초). 작을수록 어지럽다.")]
    [SerializeField, Min(0.01f)] private float _churnInterval = 0.05f;

    [Tooltip("아직 확정되지 않은 자리에 쓸 글자 풀.")]
    [SerializeField] private string _glyphs = DefaultGlyphs;

    [Tooltip("타임스케일 영향을 받지 않는다.")]
    [SerializeField] private bool _unscaledTime = true;

    [Tooltip("활성화될 때 현재 텍스트로 자동 재생한다.")]
    [SerializeField] private bool _playOnEnable = true;

    private TMP_Text _text;
    private Coroutine _routine;
    private StringBuilder _builder;

    /// <summary>수렴이 끝났을 때 한 번 불린다.</summary>
    public event Action Completed;

    public bool IsPlaying => _routine != null;

    private void Awake()
    {
        _text = GetComponent<TMP_Text>();
        _builder = new StringBuilder();
    }

    private void OnEnable()
    {
        if (_playOnEnable && _text != null)
            Play(_text.text);
    }

    private void OnDisable()
    {
        StopRoutine();
    }

    /// <summary>현재 표시 중인 문장으로 다시 재생한다.</summary>
    public void Replay()
    {
        if (_text != null)
            Play(_text.text);
    }

    /// <summary>목표 문장으로 디코드 연출을 재생한다.</summary>
    public void Play(string target)
    {
        if (_text == null)
            return;

        StopRoutine();

        if (string.IsNullOrEmpty(target))
        {
            _text.text = target;
            Completed?.Invoke();
            return;
        }

        if (!isActiveAndEnabled)
        {
            // 비활성이면 코루틴이 안 돈다. 최종 상태로 바로 확정한다.
            _text.text = target;
            Completed?.Invoke();
            return;
        }

        _routine = StartCoroutine(Run(target));
    }

    /// <summary>진행 중인 연출을 즉시 끝내고 목표 문장을 확정한다.</summary>
    public void Complete(string target)
    {
        StopRoutine();

        if (_text != null)
            _text.text = target;

        Completed?.Invoke();
    }

    private IEnumerator Run(string target)
    {
        string glyphs = string.IsNullOrEmpty(_glyphs) ? DefaultGlyphs : _glyphs;
        float duration = Mathf.Max(0.05f, _duration);
        float elapsed = 0f;
        float churnTimer = 0f;
        int settled = -1;

        while (elapsed < duration)
        {
            float t = elapsed / duration;
            int targetSettled = Mathf.FloorToInt(t * target.Length);

            churnTimer -= _unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            if (targetSettled != settled || churnTimer <= 0f)
            {
                settled = targetSettled;
                churnTimer = _churnInterval;
                _text.text = Compose(target, settled, glyphs);
            }

            yield return null;
            elapsed += _unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        }

        _routine = null;
        _text.text = target;
        Completed?.Invoke();
    }

    private string Compose(string target, int settled, string glyphs)
    {
        _builder.Clear();

        for (var i = 0; i < target.Length; i++)
        {
            char c = target[i];

            // 공백은 자리 표시가 흐트러지지 않게 그대로 둔다.
            if (i < settled || char.IsWhiteSpace(c))
                _builder.Append(c);
            else
                _builder.Append(glyphs[UnityEngine.Random.Range(0, glyphs.Length)]);
        }

        return _builder.ToString();
    }

    private void StopRoutine()
    {
        if (_routine == null)
            return;

        StopCoroutine(_routine);
        _routine = null;
    }
}
