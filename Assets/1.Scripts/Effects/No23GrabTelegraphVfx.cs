using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Local presentation notifications from existing RPC bodies only. No replicated state.</summary>
public static class No23GrabVfxSignals
{
    public static event Action<TwentyThreeBoss, BossAttackEntry, float> GrabTelegraphStarted;
    public static event Action<TwentyThreeBoss> GrabConeHidden;
    public static event Action<TwentyThreeBoss, Player> GrabAcquired;
    public static event Action<TwentyThreeBoss> GrabEnded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset()
    {
        GrabTelegraphStarted = null;
        GrabConeHidden = null;
        GrabAcquired = null;
        GrabEnded = null;
    }

    public static void Telegraph(TwentyThreeBoss boss, BossAttackEntry entry, float duration)
    {
        if (entry == null || entry.attackId != BossAttackId.Grab || duration <= 0f) return;
        try { GrabTelegraphStarted?.Invoke(boss, entry, duration); }
        catch (Exception exception) { Debug.LogException(exception, boss); }
    }

    public static void ConeHidden(TwentyThreeBoss boss)
    {
        try { GrabConeHidden?.Invoke(boss); }
        catch (Exception exception) { Debug.LogException(exception, boss); }
    }

    public static void Acquired(TwentyThreeBoss boss, NetworkObjectReference target)
    {
        try
        {
            Player player = target.TryGet(out NetworkObject obj) && obj != null
                ? obj.GetComponent<Player>() : null;
            GrabAcquired?.Invoke(boss, player); // A despawned target must still clear all tethers.
        }
        catch (Exception exception) { Debug.LogException(exception, boss); }
    }

    public static void Ended(TwentyThreeBoss boss)
    {
        try { GrabEnded?.Invoke(boss); }
        catch (Exception exception) { Debug.LogException(exception, boss); }
    }
}

/// <summary>Presentation geometry only; mirrors TwentyThreeBoss.InAttackCone (which ignores coneOffsetAngle).</summary>
public static class No23GrabVfxRules
{
    public static bool Contains(Vector3 origin, Vector3 forward, Vector3 point, float radius, float angle)
    {
        Vector3 delta = point - origin;
        delta.y = 0f;
        if (delta.sqrMagnitude > radius * radius) return false;
        if (delta.sqrMagnitude < 0.0001f || angle >= 360f) return true;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        return Vector3.Dot(forward.normalized, delta.normalized) >=
               Mathf.Cos(Mathf.Clamp(angle, 0f, 360f) * 0.5f * Mathf.Deg2Rad);
    }

    public static Vector3 Arrival(Vector3 origin, Vector3 forward, float grabRadius)
    {
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        return origin + forward.normalized * Mathf.Max(0.5f, Mathf.Max(0.1f, grabRadius) * 0.6f);
    }
}

/// <summary>
/// Every peer estimates targets from its local player replicas. Never moves a player or applies gameplay state.
/// Hide-cone queues pull until LateUpdate: cancellation's subsequent Stop RPC cancels it in the same frame.
/// </summary>
[DisallowMultipleComponent]
public sealed class No23GrabTelegraphVfx : MonoBehaviour
{
    [SerializeField] TwentyThreeBoss boss;
    [SerializeField] Transform handSocket;
    [SerializeField] EffectEntry targetRing;
    [SerializeField] EffectEntry pullTrail;
    [SerializeField] EffectEntry groundScuff;
    [SerializeField] EffectEntry pullTether;
    [SerializeField] EffectEntry landingSpark;
    [SerializeField] Texture2D warningTexture;
    [SerializeField, Min(0.01f)] float ringStartRadius = 1.15f;
    [SerializeField, Min(0.01f)] float ringEndRadius = 0.43f;
    [SerializeField, Min(0f)] float groundHeight = 0.035f;
    [Tooltip("손 → 플레이어 선·테더가 붙는 몸 높이(발밑 + m). 11-05 민경 — 콜라이더 중심이 바닥 쪽이라 바닥에 붙어 보였다.")]
    [SerializeField, Min(0f)] float bodyHeight = 1f;
    [Tooltip("손 → 대상 전기 테더 굵기(m) — 예고부터 끌려갈 때까지 같은 테더(11-05 민경, 예고용 MagneticLine 을 PullTether 로 대체).")]
    [SerializeField, Min(0f)] float tetherWidth = 0.1f;
    [SerializeField, Min(0.05f)] float trailSeconds = 0.4f;
    [SerializeField, Min(0.05f)] float scuffSeconds = 0.65f;
    [SerializeField, Min(0.05f)] float sparkSeconds = 0.3f;

    enum Phase { Idle, Telegraph, PendingPull, Pull }
    sealed class Target
    {
        public Player player;
        public Vector3 lastFoot;
        public Vector3 lastBody;
        public EffectHandle ring;
        public EffectHandle line;
        public EffectHandle tether;
        public bool arrived;
    }
    sealed class Burst
    {
        public EffectHandle handle;
        public int kind; // 0 trajectory, 1 scuff, 2 arrival
        public Vector3 from;
        public Vector3 to;
        public float start;
        public float seconds;
    }

    readonly Dictionary<Player, Target> _targets = new Dictionary<Player, Target>();
    readonly HashSet<Player> _inside = new HashSet<Player>();
    readonly List<Player> _remove = new List<Player>();
    readonly List<Burst> _bursts = new List<Burst>();
    readonly List<GameObject> _instances = new List<GameObject>();
    EffectManager _manager;
    Phase _phase;
    BossAttackEntry _entry;
    float _started;
    float _duration;
    float _pullStarted;
    CanvasGroup _warning;
    Sprite _warningSprite;
    bool _warnedMissingManager;

    void Awake()
    {
        if (boss == null) boss = GetComponent<TwentyThreeBoss>();
        if (handSocket == null)
        {
            GrabController grab = GetComponent<GrabController>();
            if (grab != null) handSocket = grab.GrabSocket;
        }
    }

    void OnEnable()
    {
        No23GrabVfxSignals.GrabTelegraphStarted += OnTelegraph;
        No23GrabVfxSignals.GrabConeHidden += OnConeHidden;
        No23GrabVfxSignals.GrabAcquired += OnAcquired;
        No23GrabVfxSignals.GrabEnded += OnEnded;
        if (boss != null) boss.Died += Clear;
    }

    void OnDisable()
    {
        No23GrabVfxSignals.GrabTelegraphStarted -= OnTelegraph;
        No23GrabVfxSignals.GrabConeHidden -= OnConeHidden;
        No23GrabVfxSignals.GrabAcquired -= OnAcquired;
        No23GrabVfxSignals.GrabEnded -= OnEnded;
        if (boss != null) boss.Died -= Clear;
        Clear();
    }

    void OnDestroy()
    {
        if (_warningSprite != null) Destroy(_warningSprite);
    }

    void OnTelegraph(TwentyThreeBoss source, BossAttackEntry entry, float duration)
    {
        if (source != boss) return;
        Clear();
        _entry = entry;
        _duration = Mathf.Max(0.01f, duration); // Includes 0.6/1.0s chained-Grab variants from the existing RPC.
        _started = Time.time;
        _phase = Phase.Telegraph;
        RefreshTargets();
    }

    void OnConeHidden(TwentyThreeBoss source)
    {
        if (source != boss || _phase != Phase.Telegraph) return;
        // Do not re-query after RPC delivery: host restraint may already have moved the player this frame.
        // lastFoot/lastBody retain the previous completed presentation frame's positions and membership.
        _phase = Phase.PendingPull;
        SetWarning(false, 0f);
    }

    void OnAcquired(TwentyThreeBoss source, Player player)
    {
        if (source != boss) return;
        bool showArrival = _phase == Phase.Pull || _phase == Phase.PendingPull;
        bool alreadyArrived = player != null && _targets.TryGetValue(player, out Target state) && state.arrived;
        ClearTargets(); // Existing GrabbedElectric now owns the acquired player's electricity too.
        _phase = Phase.Idle;
        SetWarning(false, 0f);
        if (showArrival && !alreadyArrived && player != null)
            SpawnBurst(landingSpark, 2, player.transform.position, player.transform.position, sparkSeconds);
    }

    void OnEnded(TwentyThreeBoss source) { if (source == boss) Clear(); }

    void LateUpdate()
    {
        if (boss == null || !boss.IsSpawned || boss.CurrentHealth <= 0)
        {
            Clear();
            return;
        }
        if (_phase == Phase.Telegraph)
        {
            RefreshTargets();
            DrawTelegraph();
            // A missing transition/stop must not leak warning loops. This never starts a pull by timeout.
            if (Time.time - _started > _duration + 2f) Clear();
        }
        else if (_phase == Phase.PendingPull) BeginPull();
        else if (_phase == Phase.Pull) DrawPull();
        TickBursts();
    }

    bool EnsureManager()
    {
        if (_manager != null) return true;
        _manager = EffectManager.Instance;
        if (_manager == null && !_warnedMissingManager)
        {
            _warnedMissingManager = true;
            Debug.LogWarning("[No23GrabTelegraphVfx] EffectManager missing; presentation will retry.", this);
        }
        return _manager != null;
    }

    static bool Alive(Player player) => player != null && player.IsSpawned && BossPatternTargets.IsAlive(player);

    void RefreshTargets()
    {
        _inside.Clear();
        // Active is registered on every peer. BossPatternTargets.AllPlayers is server-only, so do not use it here.
        IReadOnlyList<PlayerLifeCycleController> players = PlayerLifeCycleController.Active;
        float radius = _entry != null && _entry.coneRadius > 0f ? _entry.coneRadius : 8f;
        float angle = _entry != null && _entry.coneAngle > 0f ? _entry.coneAngle : 90f;
        for (int i = 0; i < players.Count; i++)
        {
            Player player = players[i] != null ? players[i].GetComponent<Player>() : null;
            if (!Alive(player) || !No23GrabVfxRules.Contains(boss.transform.position,
                    boss.transform.forward, player.transform.position, radius, angle)) continue;
            _inside.Add(player);
            if (!_targets.TryGetValue(player, out Target state))
            {
                state = new Target { player = player };
                _targets.Add(player, state);
            }
            state.lastFoot = player.transform.position;
            state.lastBody = Body(player.transform);
            if (EnsureManager())
            {
                if (!state.ring.IsSet) state.ring = Loop(targetRing);
                if (!state.tether.IsSet) state.tether = Loop(pullTether);   // 예고부터 테더(옛 MagneticLine 대체)
            }
        }
        _remove.Clear();
        foreach (Player player in _targets.Keys) if (!_inside.Contains(player)) _remove.Add(player);
        for (int i = 0; i < _remove.Count; i++)
        {
            ReleaseTarget(_targets[_remove[i]]);
            _targets.Remove(_remove[i]);
        }
    }

    EffectHandle Loop(EffectEntry entry) => entry != null && EnsureManager()
        ? _manager.PlayLooping(entry, transform.position, Quaternion.identity) : EffectHandle.None;

    Vector3 Hand => handSocket != null ? handSocket.position : transform.position + Vector3.up * 2f;

    void DrawTelegraph()
    {
        float t = Mathf.Clamp01((Time.time - _started) / _duration);
        float radius = Mathf.Lerp(ringStartRadius, ringEndRadius, t);
        bool localTarget = false;
        foreach (Target state in _targets.Values)
        {
            DrawRing(state.ring, state.lastFoot + Vector3.up * groundHeight, radius, 0.65f + 0.35f * t);
            DrawLine(state.tether, Hand, state.lastBody, tetherWidth, Mathf.Lerp(0.12f, 0.85f, t));   // 예고 동안 점점 진해짐
            localTarget |= state.player == Player.LocalPlayer;
        }
        SetWarning(localTarget, t);
    }

    void BeginPull()
    {
        _phase = Phase.Pull;
        _pullStarted = Time.time;
        float grabRadius = boss.Data != null ? boss.Data.grabRadius : 2.2f;
        Vector3 arrival = No23GrabVfxRules.Arrival(transform.position, transform.forward, grabRadius);
        foreach (Target state in _targets.Values)
        {
            Release(ref state.ring);
            Release(ref state.line);
            if (!Alive(state.player)) { Release(ref state.tether); continue; }
            SpawnBurst(pullTrail, 0, state.lastBody, arrival + Vector3.up, trailSeconds);
            SpawnBurst(groundScuff, 1, state.lastFoot, arrival, scuffSeconds);
            if (!state.tether.IsSet) state.tether = Loop(pullTether);   // 예고 테더를 그대로 이어 간다
        }
        DrawPull();
    }

    void DrawPull()
    {
        float grabRadius = boss.Data != null ? boss.Data.grabRadius : 2.2f;
        Vector3 arrival = No23GrabVfxRules.Arrival(transform.position, transform.forward, grabRadius);
        foreach (Target state in _targets.Values)
        {
            if (!Alive(state.player)) { ReleaseTarget(state); continue; }
            Vector3 foot = state.player.transform.position;
            DrawLine(state.tether, Hand, Body(state.player.transform), tetherWidth, 1f);
            Vector3 gap = foot - arrival; gap.y = 0f;
            if (!state.arrived && gap.sqrMagnitude <= 0.55f * 0.55f)
            {
                state.arrived = true;
                SpawnBurst(landingSpark, 2, foot, foot, sparkSeconds);
            }
        }
        float speed = boss.Data != null ? Mathf.Clamp(boss.Data.grabCycleSpeed, 0.25f, 3f) : 1f;
        float lifetime = boss.Data != null ? boss.Data.grabPullDuration / speed : 1.65f;
        if (Time.time - _pullStarted > lifetime + 0.75f) Clear();
    }

    void SpawnBurst(EffectEntry entry, int kind, Vector3 from, Vector3 to, float seconds)
    {
        if (entry == null || !EnsureManager()) return;
        EffectHandle handle = _manager.PlayRecallable(entry, from, Quaternion.identity,
            durationOverride: seconds);
        _bursts.Add(new Burst { handle = handle, kind = kind, from = from, to = to,
            start = Time.time, seconds = seconds });
    }

    void TickBursts()
    {
        for (int i = _bursts.Count - 1; i >= 0; i--)
        {
            Burst burst = _bursts[i];
            float t = Mathf.Clamp01((Time.time - burst.start) / burst.seconds);
            if (t >= 1f) { EffectHandle h = burst.handle; Release(ref h); _bursts.RemoveAt(i); continue; }
            if (_manager == null) continue;
            _manager.GetInstances(burst.handle, _instances);
            for (int j = 0; j < _instances.Count; j++)
            {
                if (!_instances[j].TryGetComponent(out No23GrabFxView view)) continue;
                if (burst.kind == 0) view.Segment(burst.from, burst.to, 0.07f, 1f - t);
                else if (burst.kind == 1) view.Scuff(burst.from + Vector3.up * groundHeight, burst.to, 1f - t);
                else view.Spark(burst.from + Vector3.up * 0.25f, t);
            }
        }
    }

    void DrawRing(EffectHandle handle, Vector3 center, float radius, float alpha)
    {
        if (_manager == null) return;
        _manager.GetInstances(handle, _instances);
        for (int i = 0; i < _instances.Count; i++)
            if (_instances[i].TryGetComponent(out No23GrabFxView view)) view.Ring(center, radius, alpha);
    }

    // 몸 높이 점 — 발밑 + bodyHeight(콜라이더 중심 대신, 바닥에 붙지 않게).
    Vector3 Body(Transform t) => t.position + Vector3.up * bodyHeight;

    void DrawLine(EffectHandle handle, Vector3 from, Vector3 to, float width, float alpha)
    {
        if (_manager == null) return;
        _manager.GetInstances(handle, _instances);
        for (int i = 0; i < _instances.Count; i++)
            if (_instances[i].TryGetComponent(out No23GrabFxView view)) view.Segment(from, to, width, alpha);
    }

    void Release(ref EffectHandle handle)
    {
        if (_manager != null && handle.IsSet) _manager.ReleaseImmediate(handle);
        handle = EffectHandle.None;
    }

    void ReleaseTarget(Target state)
    {
        Release(ref state.ring); Release(ref state.line); Release(ref state.tether);
    }

    void ClearTargets()
    {
        foreach (Target state in _targets.Values) ReleaseTarget(state);
        _targets.Clear();
    }

    void Clear()
    {
        ClearTargets();
        for (int i = 0; i < _bursts.Count; i++) { EffectHandle h = _bursts[i].handle; Release(ref h); }
        _bursts.Clear();
        _instances.Clear();
        _inside.Clear();
        _phase = Phase.Idle;
        SetWarning(false, 0f);
    }

    void SetWarning(bool visible, float progress)
    {
        if (visible && _warning == null && warningTexture != null)
        {
            var obj = new GameObject("No23 Grab — local warning", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasGroup));
            obj.layer = 5;
            obj.transform.SetParent(transform, false);
            Canvas canvas = obj.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            _warning = obj.GetComponent<CanvasGroup>();
            _warning.blocksRaycasts = false;
            _warning.interactable = false;
            var edge = new GameObject("Edge", typeof(RectTransform), typeof(Image));
            edge.layer = 5;
            edge.transform.SetParent(obj.transform, false);
            RectTransform rect = edge.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
            _warningSprite = Sprite.Create(warningTexture, new Rect(0f, 0f, warningTexture.width,
                warningTexture.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            Image image = edge.GetComponent<Image>();
            image.sprite = _warningSprite;
            image.color = new Color(1f, 0.06f, 0.09f, 1f);
            image.raycastTarget = false;
        }
        if (_warning != null)
            _warning.alpha = visible ? 0.16f + 0.18f * (0.5f + 0.5f * Mathf.Sin(progress * Mathf.PI * 6f)) : 0f;
    }
}
