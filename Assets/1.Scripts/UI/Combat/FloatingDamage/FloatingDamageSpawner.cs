using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class FloatingDamageSpawner : MonoBehaviour
{
    public static FloatingDamageSpawner Instance { get; private set; }

    [SerializeField] FloatingDamageSettings settings;
    [SerializeField] FloatingDamagePopup popupPrefab;

    readonly Dictionary<FloatingDamageAccumulationKey, FloatingDamagePopup> _activeByKey = new();
    readonly List<FloatingDamagePopup> _livePopups = new();
    readonly FloatingDamageEmphasisGate _emphasisGate = new();
    ObjectPool<FloatingDamagePopup> _pool;
    Canvas _canvas;

    public FloatingDamageSettings Settings => settings;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("[FloatingDamage] 씬에 FloatingDamageSpawner가 둘 이상 있습니다.", this);
            enabled = false;
            return;
        }

        Instance = this;
        _canvas = CreateOverlayCanvas();
        _pool = new ObjectPool<FloatingDamagePopup>(
            CreatePopup,
            popup => popup.gameObject.SetActive(true),
            popup => popup.gameObject.SetActive(false),
            popup => Destroy(popup.gameObject),
            true,
            settings != null ? settings.MaxConcurrentPopups : 1,
            settings != null ? settings.MaxConcurrentPopups : 1);
    }

    public void Submit(FloatingPopupRequest request)
    {
        if (!isActiveAndEnabled || settings == null || popupPrefab == null ||
            request.target == null || request.amount <= 0 || IsLocalPlayerTarget(request.target))
            return;

        if (!settings.TryGetStyle(request.kind, out FloatingPopupStyle style))
        {
            Debug.LogError($"[FloatingDamage] {request.kind} 스타일이 Settings에 없습니다.", settings);
            return;
        }

        if (settings.DigitSet == null)
        {
            Debug.LogError("[FloatingDamage] Settings 에 숫자 글꼴(DigitSet)이 없습니다.", settings);
            return;
        }

        bool canAccumulate = FloatingDamageAccumulationPolicy.TryCreateKey(
            request.attackerClientId,
            request.attackType,
            request.target.GetInstanceID(),
            request.kind,
            request.hitPattern,
            out FloatingDamageAccumulationKey key);

        FloatingDamageTier tier = settings.ClassifyTier(request.amount, request.target.MaxHp, request.target.Rank);

        if (canAccumulate && _activeByKey.TryGetValue(key, out FloatingDamagePopup active) &&
            active != null && active.TryAccumulate(request.amount))
            return;

        ReclaimOldestIfFull();

        FloatingDamagePopup popup = _pool.Get();
        _livePopups.Add(popup);
        if (canAccumulate)
            _activeByKey[key] = popup;
        popup.Initialize(request, settings, style, tier, ResolveAnchor(request.target),
            _canvas, _emphasisGate, ReleasePopup);
    }

    void OnEnable()
    {
        Canvas.willRenderCanvases += ApplyLayouts;
    }

    void OnDisable()
    {
        Canvas.willRenderCanvases -= ApplyLayouts;
    }

    // 위치는 Canvas 렌더 직전에 반영한다 — 카메라가 LateUpdate 에서 움직인 뒤라 숫자가 월드에서 밀리지 않는다.
    void ApplyLayouts()
    {
        for (int i = 0; i < _livePopups.Count; i++)
        {
            if (_livePopups[i] != null)
                _livePopups[i].ApplyLayout();
        }
    }

    // 픽셀 기준(1920×1080)을 Canvas 단위로 쓰기 위한 오버레이 Canvas. 씬마다 저작하지 않도록 스포너가 직접 만든다.
    Canvas CreateOverlayCanvas()
    {
        var canvasObject = new GameObject("FloatingDamageCanvas", typeof(RectTransform));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = settings != null ? settings.CanvasSortingOrder : 0;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = settings != null ? settings.ReferenceResolution : new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;
        return canvas;
    }

    FloatingDamagePopup CreatePopup()
    {
        FloatingDamagePopup popup = Instantiate(popupPrefab, _canvas.transform);
        popup.gameObject.SetActive(false);
        return popup;
    }

    // 생성점 = 몸 중심. 저작된 앵커가 있으면 그것을 쓴다. 누적 숫자가 대상을 따라갈 때도 같은 점을 쓴다.
    internal static Vector3 ResolveAnchor(Unit target)
    {
        if (target.TryGetComponent(out FloatingDamageAnchor anchor))
            return anchor.WorldPosition;

        if (target.TryGetComponent(out Collider body) && body.enabled)
            return body.bounds.center;

        return target.transform.position + Vector3.up;
    }

    void ReclaimOldestIfFull()
    {
        while (_livePopups.Count >= settings.MaxConcurrentPopups)
        {
            FloatingDamagePopup oldest = _livePopups[0];
            if (oldest == null)
                _livePopups.RemoveAt(0);
            else
                oldest.ForceRelease();
        }
    }

    void ReleasePopup(FloatingDamagePopup popup)
    {
        if (popup == null || !_livePopups.Remove(popup))
            return;

        FloatingDamageAccumulationKey keyToRemove = default;
        bool hasKeyToRemove = false;
        foreach (KeyValuePair<FloatingDamageAccumulationKey, FloatingDamagePopup> pair in _activeByKey)
        {
            if (pair.Value != popup)
                continue;

            keyToRemove = pair.Key;
            hasKeyToRemove = true;
            break;
        }

        if (hasKeyToRemove)
            _activeByKey.Remove(keyToRemove);

        _pool.Release(popup);
    }

    static bool IsLocalPlayerTarget(Unit target)
    {
        return target is Player player && (player == Player.LocalPlayer || player.IsOwner);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
