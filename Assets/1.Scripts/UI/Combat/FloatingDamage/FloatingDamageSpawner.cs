using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

[DisallowMultipleComponent]
public sealed class FloatingDamageSpawner : MonoBehaviour
{
    readonly struct PopupKey
    {
        readonly int _targetId;
        readonly PopupKind _kind;

        public PopupKey(Unit target, PopupKind kind)
        {
            _targetId = target.GetInstanceID();
            _kind = kind;
        }

        public override bool Equals(object obj)
        {
            return obj is PopupKey other && _targetId == other._targetId && _kind == other._kind;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (_targetId * 397) ^ (int)_kind;
            }
        }
    }

    public static FloatingDamageSpawner Instance { get; private set; }
    public static bool RequiresAttributedDamageRpc =>
        Instance != null &&
        Instance.settings != null &&
        Instance.settings.DisplayFilter != FloatingDamageDisplayFilter.AllDamage;

    [SerializeField] FloatingDamageSettings settings;
    [SerializeField] FloatingDamagePopup popupPrefab;

    readonly Dictionary<PopupKey, FloatingDamagePopup> _activeByTargetAndKind = new();
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

        PopupKey key = new PopupKey(request.target, request.kind);
        if (_activeByTargetAndKind.TryGetValue(key, out FloatingDamagePopup active) &&
            active != null && active.TryAccumulate(request.amount, request.fromLocalPlayer))
            return;

        ReclaimOldestIfFull();

        FloatingDamagePopup popup = _pool.Get();
        _livePopups.Add(popup);
        _activeByTargetAndKind[key] = popup;
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

        PopupKey keyToRemove = default;
        bool hasKeyToRemove = false;
        foreach (KeyValuePair<PopupKey, FloatingDamagePopup> pair in _activeByTargetAndKind)
        {
            if (pair.Value != popup)
                continue;

            keyToRemove = pair.Key;
            hasKeyToRemove = true;
            break;
        }

        if (hasKeyToRemove)
            _activeByTargetAndKind.Remove(keyToRemove);

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
