using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

[DisallowMultipleComponent]
public sealed class FloatingDamageSpawner : MonoBehaviour
{
    public static FloatingDamageSpawner Instance { get; private set; }

    [SerializeField] FloatingDamageSettings settings;
    [SerializeField] FloatingDamagePopup popupPrefab;

    readonly Dictionary<FloatingDamageAccumulationKey, FloatingDamagePopup> _activeByKey = new();
    readonly List<FloatingDamagePopup> _livePopups = new();
    ObjectPool<FloatingDamagePopup> _pool;

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

        bool canAccumulate = FloatingDamageAccumulationPolicy.TryCreateKey(
            request.attackerClientId,
            request.attackType,
            request.target.GetInstanceID(),
            request.kind,
            request.hitPattern,
            out FloatingDamageAccumulationKey key);

        if (canAccumulate && _activeByKey.TryGetValue(key, out FloatingDamagePopup active) &&
            active != null && active.TryAccumulate(request.amount, request.fromLocalPlayer))
            return;

        ReclaimOldestIfFull();

        FloatingDamagePopup popup = _pool.Get();
        _livePopups.Add(popup);
        if (canAccumulate)
            _activeByKey[key] = popup;
        popup.Initialize(request, settings, style, ReleasePopup);
    }

    FloatingDamagePopup CreatePopup()
    {
        FloatingDamagePopup popup = Instantiate(popupPrefab, transform);
        popup.gameObject.SetActive(false);
        return popup;
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
