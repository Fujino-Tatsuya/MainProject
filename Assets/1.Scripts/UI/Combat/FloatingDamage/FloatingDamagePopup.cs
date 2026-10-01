using System;
using EuniTween;
using TMPro;
using UnityEngine;

/// <summary>
/// 데미지 숫자 하나. 기획 "Re:C 데미지 숫자 표기" v0.2 §4·§5 의 흐름을 시간 함수로 계산한다.
///
/// 좌표: 단발은 첫 타격 때의 **월드 위치에 고정**(몬스터 이동·넉백을 따라가지 않음), 누적은 대상을 따라간다. 매 프레임
/// 화면에 투영한 뒤 기획의 픽셀 오프셋을 더한다. 오버레이 Canvas(1920×1080 기준) 단위가 곧 기획 px 다.
/// 위치 반영은 스포너가 <c>Canvas.willRenderCanvases</c> 에서 <see cref="ApplyLayout"/> 로 한다 —
/// 카메라가 LateUpdate 에서 움직여도 한 프레임 밀리지 않게.
///
/// - 단발(<see cref="AttackHitPattern.Single"/>): 상승 → 최고점에서 하강하며 끝 0.25s 동안 흐려짐 → 제거.
/// - 누적(<see cref="AttackHitPattern.Multi"/>): 상승 후 유지. 타격마다 값 갱신 + 펀치.
///   마지막 타격 후 유지 → 하강하며 흐려짐. 흐려지는 중에 맞아도 누적한다(유지로 복귀).
/// </summary>
[DisallowMultipleComponent]
public sealed class FloatingDamagePopup : MonoBehaviour
{
    [SerializeField] TMP_Text amountText;

    RectTransform _rect;
    Canvas _canvas;
    RectTransform _canvasRect;
    FloatingDamageSettings _settings;
    FloatingDamageEmphasisGate _emphasisGate;
    FloatingPopupRequest _request;
    Action<FloatingDamagePopup> _release;

    Vector3 _anchorWorld;
    Vector2 _scatter;
    bool _accumulates;
    FloatingDamageTier _tier;
    FloatingDamageTierLook _look;
    float _singleDuration;
    int _amount;
    Color _baseColor;
    bool _releaseRequested;

    float _elapsed;
    float _lastHitAt;
    float _lastPunchAt;
    float _punchStartAt;
    float _settleStartAt;
    float _settleFrom;
    float _shakeStartAt;

    public bool IsAccumulating => _accumulates && !_releaseRequested;

    void Awake()
    {
        _rect = (RectTransform)transform;
    }

    public void Initialize(
        FloatingPopupRequest request,
        FloatingDamageSettings settings,
        FloatingPopupStyle style,
        FloatingDamageTier tier,
        Vector3 anchorWorld,
        Canvas canvas,
        FloatingDamageEmphasisGate emphasisGate,
        Action<FloatingDamagePopup> release)
    {
        _request = request;
        _settings = settings;
        _canvas = canvas;
        _canvasRect = (RectTransform)canvas.transform;
        _emphasisGate = emphasisGate;
        _release = release;
        _anchorWorld = anchorWorld;
        _accumulates = request.hitPattern == AttackHitPattern.Multi;
        _amount = Mathf.Max(0, request.amount);
        _baseColor = style.color;
        _releaseRequested = false;

        _elapsed = 0f;
        _lastHitAt = 0f;
        _lastPunchAt = float.NegativeInfinity;
        _punchStartAt = float.NegativeInfinity;
        _settleStartAt = float.NegativeInfinity;
        _shakeStartAt = float.NegativeInfinity;

        Vector2 scatter = settings.SpawnScatter;
        _scatter = new Vector2(
            UnityEngine.Random.Range(-scatter.x, scatter.x),
            UnityEngine.Random.Range(-scatter.y, scatter.y));

        _rect.anchorMin = Vector2.zero;
        _rect.anchorMax = Vector2.zero;
        _rect.pivot = new Vector2(0.5f, 0.5f);

        if (amountText != null)
        {
            amountText.fontSize = style.fontSize;
            amountText.color = _baseColor;
        }

        SetTier(tier);
        // 단발 숫자의 표시 시간은 생성 때의 구간으로 정한다(단발은 구간이 바뀌지 않는다).
        _singleDuration = Mathf.Max(_look.singleDuration, settings.RiseDuration + settings.FadeDuration);

        if (_look.HasSpawnPop && (tier != FloatingDamageTier.High || TryConsumeEmphasis()))
            StartSpawnPop();

        RefreshText();
        ApplyLayout();
    }

    /// <summary>같은 누적 단위의 추가 타격. 값은 항상 즉시 갱신하고, 연출만 간격 규칙을 따른다.</summary>
    public bool TryAccumulate(int amount, FloatingDamageTier hitTier)
    {
        if (!IsAccumulating || amount <= 0)
            return false;

        _amount += amount;
        _lastHitAt = _elapsed;
        RefreshText();

        // 크기는 지금까지 받은 개별 타격 중 가장 높은 구간을 유지한다 — 누적액으로는 올리지 않는다.
        if (hitTier > _tier)
        {
            SetTier(hitTier);
            if (hitTier == FloatingDamageTier.High && _look.HasSpawnPop && TryConsumeEmphasis())
            {
                StartSpawnPop();
                return true;
            }
        }

        TryStartUpdatePunch();
        return true;
    }

    public void ForceRelease()
    {
        RequestRelease();
    }

    void Update()
    {
        if (_releaseRequested || _settings == null)
            return;

        _elapsed += Time.deltaTime;
        if (_elapsed >= EndTime())
            RequestRelease();
    }

    /// <summary>화면 위치·크기·불투명도를 현재 시각 기준으로 반영한다. 스포너가 Canvas 렌더 직전에 부른다.</summary>
    public void ApplyLayout()
    {
        if (_releaseRequested || _settings == null || _canvas == null)
            return;

        // 누적 숫자는 대상을 따라간다(은희 2026-10-01 — 기획 §4 "따라가지 않음"과 다름, 단발만 고정).
        // 대상이 사라지거나 꺼지면 마지막 위치에 남는다.
        if (_accumulates && _request.target != null && _request.target.isActiveAndEnabled)
            _anchorWorld = FloatingDamageSpawner.ResolveAnchor(_request.target);

        Camera camera = Camera.main;
        Vector3 screen = camera != null ? camera.WorldToScreenPoint(_anchorWorld) : Vector3.zero;
        if (camera == null || screen.z <= 0f)
        {
            SetAlpha(0f);
            return;
        }

        float scaleFactor = Mathf.Max(0.0001f, _canvas.scaleFactor);
        Vector2 position = new Vector2(screen.x / scaleFactor, screen.y / scaleFactor);
        position.x += _scatter.x + ShakeOffset();
        position.y += _settings.SpawnOffset + _scatter.y + VerticalOffset();

        Vector2 size = _canvasRect.rect.size;
        Vector2 padding = _settings.ScreenEdgePadding;
        position.x = Mathf.Clamp(position.x, padding.x, Mathf.Max(padding.x, size.x - padding.x));
        position.y = Mathf.Clamp(position.y, padding.y, Mathf.Max(padding.y, size.y - padding.y));

        _rect.anchoredPosition = position;
        _rect.localScale = Vector3.one * CurrentScale();
        SetAlpha(CurrentAlpha());
    }

    float EndTime()
    {
        return _accumulates ? FadeStartTime() + _settings.FadeDuration : _singleDuration;
    }

    float FadeStartTime()
    {
        if (!_accumulates)
            return _singleDuration - _settings.FadeDuration;

        return Mathf.Max(_settings.RiseDuration, _lastHitAt + _settings.AccumulateHold);
    }

    float VerticalOffset()
    {
        float rise = _settings.RiseDistance *
                     Easing.Evaluate(_settings.RiseEase, _elapsed / _settings.RiseDuration);
        if (_elapsed <= _settings.RiseDuration)
            return rise;

        // 단발: 최고점에서 끝까지 완만하게 하강. 누적: 유지 후 사라지는 동안만 하강.
        float fallStart = _accumulates ? FadeStartTime() : _settings.RiseDuration;
        float fallDuration = _accumulates ? _settings.FadeDuration : _singleDuration - _settings.RiseDuration;
        if (_elapsed <= fallStart)
            return rise;

        float fall = Easing.Evaluate(_settings.FallEase, (_elapsed - fallStart) / Mathf.Max(0.0001f, fallDuration));
        return _settings.RiseDistance - _settings.FallDistance * fall;
    }

    float CurrentAlpha()
    {
        float fade = (_elapsed - FadeStartTime()) / _settings.FadeDuration;
        return _baseColor.a * (1f - Mathf.Clamp01(fade));
    }

    float CurrentScale()
    {
        float scale = _look.holdScale;

        float settle = Progress(_settleStartAt, _look.spawnSettleDuration);
        if (settle < 1f)
            scale = Mathf.LerpUnclamped(_settleFrom, _look.holdScale,
                Easing.Evaluate(_settings.SpawnSettleEase, settle));

        float punch = Progress(_punchStartAt, _settings.UpdatePunchDuration);
        if (punch < 1f)
            scale *= 1f + (_settings.UpdatePunchScale - 1f) * Easing.Punch(_settings.UpdatePunchEase, punch);

        return scale;
    }

    // 좌우로 짧게 1회 — sin 한 주기라 +max → -max → 0 으로 끝난다. 숫자에만 적용(카메라 아님).
    float ShakeOffset()
    {
        float shake = Progress(_shakeStartAt, _settings.ShakeDuration);
        return shake < 1f ? _settings.ShakeDistance * Mathf.Sin(shake * Mathf.PI * 2f) : 0f;
    }

    // 시작 전이거나 끝났으면 1 이상(= 비활성).
    float Progress(float startAt, float duration)
    {
        if (float.IsNegativeInfinity(startAt) || duration <= 0f)
            return 1f;

        return (_elapsed - startAt) / duration;
    }

    void SetTier(FloatingDamageTier tier)
    {
        _tier = tier;
        _look = _settings.GetLook(tier);
        if (amountText != null)
            amountText.fontStyle = _look.bold ? FontStyles.Bold : FontStyles.Normal;
    }

    void StartSpawnPop()
    {
        _settleFrom = _look.spawnScale;
        _settleStartAt = _elapsed;
        _punchStartAt = float.NegativeInfinity;
        if (_look.shake)
            _shakeStartAt = _elapsed;
    }

    void TryStartUpdatePunch()
    {
        // 간격 안의 적중은 값만 갱신하고 확대는 생략한다.
        if (_elapsed - _lastPunchAt < _settings.UpdatePunchMinInterval)
            return;

        _lastPunchAt = _elapsed;
        _punchStartAt = _elapsed;
    }

    bool TryConsumeEmphasis()
    {
        int targetId = _request.target != null ? _request.target.GetInstanceID() : 0;
        return _emphasisGate == null ||
               _emphasisGate.TryConsume(_request.attackerClientId, targetId, Time.time, _settings.HighEmphasisInterval);
    }

    void SetAlpha(float alpha)
    {
        if (amountText == null)
            return;

        Color color = _baseColor;
        color.a = alpha;
        amountText.color = color;
    }

    void RefreshText()
    {
        if (amountText != null)
            amountText.SetText("{0}", _amount);
    }

    void RequestRelease()
    {
        if (_releaseRequested)
            return;

        _releaseRequested = true;
        _release?.Invoke(this);
    }
}
