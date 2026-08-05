using System;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class FloatingDamagePopup : MonoBehaviour
{
    enum PopupState
    {
        Active,
        Animating,
        FadingOut
    }

    [SerializeField] TMP_Text amountText;

    FloatingDamageSettings _settings;
    FloatingPopupRequest _request;
    FloatingDamageAnchor _anchor;
    Action<FloatingDamagePopup> _release;
    PopupState _state;
    int _amount;
    float _stateElapsed;
    float _punchElapsed;
    Vector3 _velocity;
    Vector3 _prefabScale;
    Vector3 _baseScale;
    Color _baseColor;
    bool _releaseRequested;

    public Unit Target => _request.target;
    public PopupKind Kind => _request.kind;
    public bool IsAccumulating => !_releaseRequested && _state == PopupState.Active;

    void Awake()
    {
        _prefabScale = transform.localScale;
    }

    public void Initialize(
        FloatingPopupRequest request,
        FloatingDamageSettings settings,
        FloatingPopupStyle style,
        Action<FloatingDamagePopup> release)
    {
        _request = request;
        _settings = settings;
        _anchor = request.target != null ? request.target.GetComponent<FloatingDamageAnchor>() : null;
        _release = release;
        _amount = Mathf.Max(0, request.amount);
        _state = PopupState.Active;
        _stateElapsed = 0f;
        _punchElapsed = 0f;
        _velocity = Vector3.zero;
        _releaseRequested = false;
        _baseColor = style.color;
        _baseScale = _prefabScale * (request.fromLocalPlayer ? settings.OwnDamageScaleMultiplier : 1f);

        if (amountText != null)
        {
            amountText.fontSize = style.fontSize;
            amountText.color = _baseColor;
        }

        transform.localScale = _baseScale;
        SnapToTarget();
        RefreshText();
    }

    public bool TryAccumulate(int amount, bool fromLocalPlayer)
    {
        if (!IsAccumulating || amount <= 0)
            return false;

        _amount += amount;
        _stateElapsed = 0f;
        _punchElapsed = 0f;

        if (fromLocalPlayer && !_request.fromLocalPlayer)
        {
            _request.fromLocalPlayer = true;
            _baseScale = _prefabScale * _settings.OwnDamageScaleMultiplier;
        }

        RefreshText();
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

        float deltaTime = Time.deltaTime;
        _stateElapsed += deltaTime;

        switch (_state)
        {
            case PopupState.Active:
                UpdateActive(deltaTime);
                break;
            case PopupState.Animating:
                UpdateAnimating(deltaTime);
                break;
            case PopupState.FadingOut:
                UpdateFadingOut(deltaTime);
                break;
        }
    }

    void LateUpdate()
    {
        Camera camera = Camera.main;
        if (camera != null)
            transform.rotation = camera.transform.rotation;
    }

    void UpdateActive(float deltaTime)
    {
        if (_request.target == null)
        {
            BeginAnimating();
            return;
        }

        SnapToTarget();
        _punchElapsed += deltaTime;
        float punchProgress = Mathf.Clamp01(_punchElapsed / _settings.ActivePunchDuration);
        float punch = Mathf.Sin(punchProgress * Mathf.PI) * (_settings.ActivePunchScale - 1f);
        transform.localScale = _baseScale * (1f + punch);

        if (_stateElapsed >= _settings.StayTimeout)
            BeginAnimating();
    }

    void BeginAnimating()
    {
        _state = PopupState.Animating;
        _stateElapsed = 0f;
        transform.localScale = _baseScale;

        Camera camera = Camera.main;
        Vector3 up = camera != null ? camera.transform.up : Vector3.up;
        Vector3 axis = camera != null ? camera.transform.forward : Vector3.forward;
        float angle = UnityEngine.Random.Range(-_settings.ScatterAngle, _settings.ScatterAngle);
        _velocity = Quaternion.AngleAxis(angle, axis) * up * _settings.InitialSpeed;
    }

    void UpdateAnimating(float deltaTime)
    {
        _velocity += Vector3.down * (_settings.Gravity * deltaTime);
        transform.position += _velocity * deltaTime;

        float progress = Mathf.Clamp01(_stateElapsed / _settings.AnimateDuration);
        Color darkColor = new Color(
            _baseColor.r * _settings.DarkenMultiplier,
            _baseColor.g * _settings.DarkenMultiplier,
            _baseColor.b * _settings.DarkenMultiplier,
            _baseColor.a);
        if (amountText != null)
            amountText.color = Color.Lerp(_baseColor, darkColor, progress);

        if (_stateElapsed >= _settings.AnimateDuration)
        {
            _state = PopupState.FadingOut;
            _stateElapsed = 0f;
        }
    }

    void UpdateFadingOut(float deltaTime)
    {
        _velocity = Vector3.Lerp(_velocity, Vector3.zero, _settings.FadeVelocityDamping * deltaTime);
        transform.position += _velocity * deltaTime;

        float progress = Mathf.Clamp01(_stateElapsed / _settings.FadeDuration);
        if (amountText != null)
        {
            Color color = amountText.color;
            color.a = Mathf.Lerp(_baseColor.a, 0f, progress);
            amountText.color = color;
        }

        if (_stateElapsed >= _settings.FadeDuration)
            RequestRelease();
    }

    void SnapToTarget()
    {
        if (_anchor != null)
        {
            transform.position = _anchor.WorldPosition;
            return;
        }

        if (_request.target != null)
            transform.position = _request.target.transform.position + _settings.OverheadWorldOffset;
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
