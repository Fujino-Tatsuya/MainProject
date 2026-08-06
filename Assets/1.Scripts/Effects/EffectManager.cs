using System.Collections.Generic;
using Ami.BroAudio;
using UnityEngine;

/// <summary>
/// 이펙트 재생·수명관리·풀링·히트스톱 전달의 단일 창구(싱글톤 파사드).
///
/// <b>네트워크를 전혀 모른다.</b> 순수 MonoBehaviour다 — <see cref="AudioManager"/>가 같은 문제를
/// 이미 이렇게 풀었고, NetworkBehaviour는 NetworkObject를 요구해 DontDestroyOnLoad 싱글톤과 충돌하며,
/// 무엇보다 ScriptableObject 참조는 RPC로 보낼 수 없다. 전파는 호출자(서버 판정 → OnClientPlay)의 몫이다.
///
/// <b>수명의 진실은 데이터에 있다.</b> 프리팹에서 종료를 추론하지 않고 <see cref="EffectEntry.duration"/>
/// 타이머로 회수한다. 회수 경로는 하나뿐이다(StopAction.Callback 조기 반납 없음).
///
/// 씬에 배치해 쓴다(자동 생성 없음). 카탈로그 없이 살아 있는 반쯤 초기화된 싱글톤을 만들지 않기 위해서다.
/// </summary>
[DisallowMultipleComponent]
public class EffectManager : MonoBehaviour
{
    public static EffectManager Instance { get; private set; }

    [Header("Catalog")]
    [Tooltip("모든 EffectEntry를 모아둔 EffectCatalog 에셋. 룩업 + 빌드 포함 보장 + 프리워밍 대상")]
    [SerializeField] private EffectCatalog catalog;

    [Header("Pool")]
    [Tooltip("파트 프리팹 하나당 풀이 보관하는 최대 인스턴스 수. 넘치면 반납분이 파괴된다")]
    [SerializeField, Min(1)] private int poolMaxSizePerPrefab = 128;

    /// <summary>중앙 이펙트 카탈로그. 예: EffectManager.Instance.Catalog.HitSpark</summary>
    public EffectCatalog Catalog => catalog;

    private readonly List<IEffectSystem> _drivers = new List<IEffectSystem>();
    private readonly List<ActiveEffect> _slots = new List<ActiveEffect>();
    private readonly Stack<int> _freeSlots = new Stack<int>();
    // 경고는 (대상 × 종류)당 1회. 콘솔 마비는 막되, 먼저 뜬 경고가 나중 경고를 영원히 덮지 않게 한다
    // — 특히 누수 경고(maxActiveWarn)가 튜닝 경고에 묻히면 상한을 둔 이유가 사라진다.
    private readonly HashSet<(int id, string kind)> _warned = new HashSet<(int, string)>();

    private EffectPool _pool;
    private Transform _poolRoot;
    private int _nextGeneration = 1;   // 0은 EffectHandle.None 전용

    #region 수명 주기

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Edit.LogWarning("[EffectManager] 중복 생성이 감지되어 파괴합니다.");
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // 드라이버는 컴파일 타임에 전부 알려져 있다. SO/리플렉션 등록은 간접층만 늘린다.
        // 기술을 추가할 때 고치는 곳은 여기 한 줄이다.
        _drivers.Add(new ShurikenEffectSystem());

        // 풀 루트는 매니저와 분리한다 — 매니저의 scale이 이펙트 크기에 곱해지지 않게.
        var rootObject = new GameObject("[EffectPool]");
        DontDestroyOnLoad(rootObject);
        _poolRoot = rootObject.transform;

        _pool = new EffectPool(_poolRoot, ResolveDriver, poolMaxSizePerPrefab);

        if (catalog == null)
        {
            // 경고가 아니라 에러다. 카탈로그가 없으면 Instance는 살아 있는데 아무것도 못 하는 싱글톤이 된다.
            Debug.LogError("[EffectManager] EffectCatalog가 연결되지 않았습니다. " +
                           "EffectManager 프리팹의 인스펙터에서 연결하세요.", this);
        }
        else
        {
            Prewarm();
        }
    }

    private void OnDestroy()
    {
        if (Instance != this) return;

        _pool?.Dispose();
        if (_poolRoot != null) Destroy(_poolRoot.gameObject);
        Instance = null;
    }

    private void Prewarm()
    {
        IReadOnlyList<EffectEntry> entries = catalog.All;

        for (int i = 0; i < entries.Count; i++)
        {
            EffectEntry entry = entries[i];
            if (entry.prewarmCount <= 0) continue;

            PrewarmParts(entry.parts, entry.prewarmCount);
            PrewarmParts(entry.outroParts, entry.prewarmCount);
        }
    }

    private void PrewarmParts(EffectPart[] parts, int count)
    {
        if (parts == null) return;

        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i] != null && parts[i].prefab != null) _pool.Prewarm(parts[i].prefab, count);
        }
    }

    #endregion

    #region 재생

    /// <summary>
    /// 원샷 재생. <see cref="EffectEntry.duration"/> 후 자동으로 풀에 반납된다.
    /// 반환값이 없는 것은 의도다 — 끌 것이 없고, 핸들을 발급하면 버려도 무해한 핸들이 생겨
    /// 루프 핸들(버리면 풀이 고갈된다)과 실패 모드가 뒤섞인다.
    /// </summary>
    public void Play(EffectEntry entry, Vector3 position, Quaternion rotation)
    {
        if (!CanPlay(entry)) return;

        ActiveEffect active = AcquireSlot(entry);
        active.looping = false;
        active.attached = false;
        active.follow = null;
        active.position = position;
        active.rotation = rotation;
        active.offset = Vector3.zero;
        active.life = entry.duration;
        active.lifeCounting = true;

        if (entry.duration <= 0f)
        {
            WarnOnce(entry, "duration", $"[EffectManager] '{entry.name}'의 duration이 0이라 재생 즉시 반납된다. " +
                            "EffectDurationProbe로 실측해 채울 것.");
        }
        else if (entry.duration < entry.LongestPartDelay)
        {
            WarnOnce(entry, "duration", $"[EffectManager] '{entry.name}'의 duration({entry.duration:F2}s)이 " +
                            $"가장 늦은 파트의 delay({entry.LongestPartDelay:F2}s)보다 짧다. " +
                            "그 파트는 발화되기 전에 반납된다.");
        }

        Schedule(active, entry.parts, false);
        FirePending(active, 0f);   // delay 0인 파트는 이번 프레임에 바로 터진다
        WarnIfTooManyActive(entry);
    }

    /// <summary>원샷 재생(회전 없음).</summary>
    public void Play(EffectEntry entry, Vector3 position) => Play(entry, position, Quaternion.identity);

    /// <summary>
    /// 루프 재생. <b>호출자가 반드시 <see cref="Release"/>로 끝내야 한다</b> — 안 그러면 풀이 고갈된다.
    /// <paramref name="follow"/>가 null이면 <paramref name="offset"/>을 월드 좌표로 보고 그 자리에 고정한다.
    /// null이 아니면 매 프레임 그 대상을 따라간다(SetParent를 쓰지 않으므로 대상의 scale이 곱해지지 않고,
    /// 대상이 파괴돼도 풀 인스턴스가 딸려 죽지 않는다).
    /// </summary>
    public EffectHandle PlayLooping(EffectEntry entry, Transform follow, Vector3 offset = default)
    {
        return follow != null
            ? PlayLoopingCore(entry, follow, offset, follow.rotation)
            : PlayLoopingCore(entry, null, offset, Quaternion.identity);
    }

    /// <summary>루프 재생을 월드 좌표·회전에 고정한다. (설계 API에 대한 편의 오버로드)</summary>
    public EffectHandle PlayLooping(EffectEntry entry, Vector3 position, Quaternion rotation)
    {
        return PlayLoopingCore(entry, null, position, rotation);
    }

    private EffectHandle PlayLoopingCore(EffectEntry entry, Transform follow, Vector3 offsetOrPosition,
                                         Quaternion rotation)
    {
        if (!CanPlay(entry)) return EffectHandle.None;

        ActiveEffect active = AcquireSlot(entry);
        active.looping = true;
        active.attached = follow != null;
        active.follow = follow;
        active.rotation = rotation;

        if (follow != null)
        {
            active.offset = offsetOrPosition;
            active.position = follow.position;
        }
        else
        {
            active.offset = Vector3.zero;
            active.position = offsetOrPosition;   // 추종 대상이 없으면 offset이 곧 월드 좌표다
        }

        active.lifeCounting = false;              // Release() 전까지는 수명을 세지 않는다

        Schedule(active, entry.parts, false);
        FirePending(active, 0f);
        WarnIfTooManyActive(entry);

        return new EffectHandle(active.slot, active.generation);
    }

    #endregion

    #region 해제

    /// <summary>
    /// 루프 이펙트를 해제한다. <b>해제 ≠ 즉시 반납</b>이다:
    /// ① outroParts 발화 → ② parts의 루프 시스템에 StopEmitting(뚝 끊기지 않게)
    /// → ③ <see cref="EffectEntry.outroDuration"/> 후 반납.
    /// 이미 반납된 핸들이거나 이미 해제된 핸들이면 조용한 no-op이다.
    /// </summary>
    public void Release(EffectHandle handle)
    {
        if (!TryResolve(handle, out ActiveEffect active)) return;
        if (active.released) return;

        active.released = true;

        // 아직 안 터진 본편 파트는 취소한다. 해제 중에 L_Loop가 뒤늦게 켜지면 안 된다.
        for (int i = active.pending.Count - 1; i >= 0; i--)
        {
            if (!active.pending[i].isOutro) active.pending.RemoveAt(i);
        }

        // outro를 먼저 예약·발화하고, 그 전에 있던 인스턴스에만 StopEmitting을 건다.
        int beforeOutro = active.instances.Count;
        Schedule(active, active.entry.outroParts, true);
        FirePending(active, 0f);

        for (int i = 0; i < beforeOutro; i++)
        {
            GameObject instance = active.instances[i].go;
            DriverOf(instance)?.Stop(instance, false);
        }

        if (active.entry.outroDuration < active.entry.LongestOutroDelay)
        {
            WarnOnce(active.entry, "outroDuration", $"[EffectManager] '{active.entry.name}'의 outroDuration" +
                                   $"({active.entry.outroDuration:F2}s)이 가장 늦은 outro 파트의 " +
                                   $"delay({active.entry.LongestOutroDelay:F2}s)보다 짧다. 그 파트는 발화되지 않는다.");
        }

        active.life = active.entry.outroDuration;
        active.lifeCounting = true;
    }

    /// <summary>outro 없이 즉시 반납한다. 씬 전환·강제 정리용.</summary>
    public void ReleaseImmediate(EffectHandle handle)
    {
        if (!TryResolve(handle, out ActiveEffect active)) return;
        Recycle(active);
    }

    #endregion

    #region 히트스톱

    /// <summary>
    /// <paramref name="target"/>을 추종 중인 모든 이펙트에 재생 속도 배율을 전달한다. 0 = 정지.
    ///
    /// 이 프로젝트에는 <c>Time.timeScale</c> 할당이 한 군데도 없다 — 히트스톱은 몬스터별
    /// <see cref="MonsterTimeController"/>가 Animator/NavMeshAgent만 조절하는 방식이라
    /// <b>이펙트가 자동으로 멈추는 경로가 존재하지 않는다.</b> 이 배선이 없으면 몬스터는 얼고
    /// 이펙트만 계속 돌아 타격감이 반쯤 무효화된다.
    /// </summary>
    public void SetPlayRateForTarget(Transform target, float rate)
    {
        if (target == null) return;

        for (int i = 0; i < _slots.Count; i++)
        {
            ActiveEffect active = _slots[i];
            if (!active.inUse || active.follow != target) continue;

            active.playRate = rate;
            for (int p = 0; p < active.instances.Count; p++)
            {
                GameObject instance = active.instances[p].go;
                DriverOf(instance)?.SetPlayRate(instance, rate);
            }
        }
    }

    #endregion

    #region 갱신

    private void Update()
    {
        float deltaTime = Time.deltaTime;

        for (int i = 0; i < _slots.Count; i++)
        {
            ActiveEffect active = _slots[i];
            if (!active.inUse) continue;

            // 추종 대상이 사라졌다 — 즉시 회수한다. SetParent를 안 쓰기 때문에 인스턴스는 멀쩡히 살아 있고,
            // 풀에 null이 남지 않는다(결정 7이 막으려던 바로 그 사고).
            if (active.attached && active.follow == null)
            {
                Recycle(active);
                continue;
            }

            // 히트스톱으로 얼어붙은 이펙트는 수명도 함께 얼어야 한다.
            // 그렇지 않으면 정지한 채로 시간만 흘러 눈앞에서 사라진다.
            float step = deltaTime * active.playRate;

            FirePending(active, step);
            UpdateFollow(active);

            if (!active.lifeCounting) continue;

            active.life -= step;
            if (active.life <= 0f) Recycle(active);
        }
    }

    private void UpdateFollow(ActiveEffect active)
    {
        if (active.follow == null) return;

        active.position = active.follow.position;
        active.rotation = active.follow.rotation;

        for (int i = 0; i < active.instances.Count; i++)
        {
            SpawnedPart spawned = active.instances[i];
            if (spawned.go == null) continue;

            spawned.go.transform.SetPositionAndRotation(
                WorldPosition(active, spawned.offset), active.rotation);
        }
    }

    #endregion

    #region 파트 발화 / 회수

    private void Schedule(ActiveEffect active, EffectPart[] parts, bool isOutro)
    {
        if (parts == null) return;

        for (int i = 0; i < parts.Length; i++)
        {
            EffectPart part = parts[i];
            if (part == null || part.IsEmpty) continue;

            active.pending.Add(new PendingPart { part = part, remaining = part.delay, isOutro = isOutro });
        }
    }

    /// <summary>대기 중인 파트의 시계를 <paramref name="step"/>만큼 돌리고, 때가 된 파트를 발화한다.</summary>
    private void FirePending(ActiveEffect active, float step)
    {
        for (int i = active.pending.Count - 1; i >= 0; i--)
        {
            PendingPart pending = active.pending[i];
            pending.remaining -= step;

            if (pending.remaining > 0f)
            {
                active.pending[i] = pending;
                continue;
            }

            active.pending.RemoveAt(i);
            Fire(active, pending.part);
        }
    }

    private void Fire(ActiveEffect active, EffectPart part)
    {
        Vector3 worldPosition = WorldPosition(active, part.offset);

        if (part.prefab != null)
        {
            GameObject instance = _pool.Rent(part.prefab);
            instance.transform.SetPositionAndRotation(worldPosition, active.rotation);
            instance.SetActive(true);

            IEffectSystem driver = DriverOf(instance);
            if (driver != null)
            {
                driver.Play(instance);
                if (!Mathf.Approximately(active.playRate, 1f)) driver.SetPlayRate(instance, active.playRate);
            }

            active.instances.Add(new SpawnedPart { go = instance, offset = part.offset });
        }

        // AudioManager가 없어도 죽지 않는다 — VFXScene에서 사운드 없이 실험할 수 있어야 한다.
        if (part.sound.IsValid() && AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayOneShot(part.sound, worldPosition);
        }
    }

    private void Recycle(ActiveEffect active)
    {
        for (int i = 0; i < active.instances.Count; i++)
        {
            GameObject instance = active.instances[i].go;
            if (instance == null) continue;

            DriverOf(instance)?.ResetForPool(instance);
            _pool.Return(instance);
        }

        active.instances.Clear();
        active.pending.Clear();
        active.entry = null;
        active.follow = null;
        active.attached = false;
        active.looping = false;
        active.released = false;
        active.lifeCounting = false;
        active.playRate = 1f;
        active.inUse = false;      // 세대는 다음 대출에서 새로 발급된다 → stale 핸들은 여기서 죽는다

        _freeSlots.Push(active.slot);
    }

    private static Vector3 WorldPosition(ActiveEffect active, Vector3 partOffset)
    {
        return active.position + active.rotation * (active.offset + partOffset);
    }

    #endregion

    #region 슬롯 / 드라이버

    private bool CanPlay(EffectEntry entry)
    {
        if (entry == null)
        {
            Debug.LogError("[EffectManager] null EffectEntry 재생 시도.", this);
            return false;
        }

        if (_pool == null)
        {
            Debug.LogError($"[EffectManager] 초기화 전에 '{entry.name}' 재생이 시도됐다.", this);
            return false;
        }

        return true;
    }

    private ActiveEffect AcquireSlot(EffectEntry entry)
    {
        ActiveEffect active;

        if (_freeSlots.Count > 0)
        {
            active = _slots[_freeSlots.Pop()];
        }
        else
        {
            active = new ActiveEffect { slot = _slots.Count };
            _slots.Add(active);
        }

        active.inUse = true;
        active.generation = _nextGeneration++;
        active.entry = entry;
        active.playRate = 1f;
        active.released = false;

        return active;
    }

    private bool TryResolve(EffectHandle handle, out ActiveEffect active)
    {
        active = null;
        if (!handle.IsSet || handle.slot < 0 || handle.slot >= _slots.Count) return false;

        ActiveEffect candidate = _slots[handle.slot];
        if (!candidate.inUse || candidate.generation != handle.generation) return false;

        active = candidate;
        return true;
    }

    private static IEffectSystem DriverOf(GameObject instance)
    {
        var id = instance.GetComponent<EffectInstance>();
        return id != null ? id.driver : null;
    }

    /// <summary>
    /// 인스턴스를 몰 드라이버를 런타임 탐색으로 정한다.
    /// 둘 이상이 손을 들면 = 프리팹 안에서 기술을 혼용한 것이다(금지 규칙).
    /// </summary>
    private IEffectSystem ResolveDriver(GameObject instance)
    {
        IEffectSystem found = null;

        for (int i = 0; i < _drivers.Count; i++)
        {
            if (!_drivers[i].CanDrive(instance)) continue;

            if (found != null)
            {
                Debug.LogError($"[EffectManager] '{instance.name}'을(를) 드라이버 두 개가 몰겠다고 한다 " +
                               $"({found.GetType().Name} / {_drivers[i].GetType().Name}). " +
                               "프리팹 하나에 단일 기술만 넣어야 한다. 먼저 손든 쪽을 쓴다.", instance);
                break;
            }

            found = _drivers[i];
        }

        if (found == null)
        {
            Edit.LogWarning($"[EffectManager] '{instance.name}'을(를) 몰 드라이버가 없다. " +
                            "ParticleSystem이 들어 있는지 확인할 것. 위치만 잡히고 아무것도 재생되지 않는다.", instance);
        }

        return found;
    }

    private void WarnIfTooManyActive(EffectEntry entry)
    {
        int count = 0;
        for (int i = 0; i < _slots.Count; i++)
        {
            if (_slots[i].inUse && _slots[i].entry == entry) count++;
        }

        if (count <= entry.maxActiveWarn) return;

        // 상한을 넘겨도 재생은 계속한다. 상한의 목적은 성능이 아니라 반납 누락(누수)을 그날 잡는 것이다.
        WarnOnce(entry, "maxActive", $"[EffectManager] '{entry.name}'의 동시 활성 수가 {count}개로 " +
                        $"maxActiveWarn({entry.maxActiveWarn})를 넘었다. " +
                        "루프 이펙트의 Release() 누락일 가능성이 높다. (재생은 계속한다)");
    }

    private void WarnOnce(Object context, string kind, string message)
    {
        if (context != null && !_warned.Add((context.GetInstanceID(), kind))) return;
        Edit.LogWarning(message, context);
    }

    #endregion

    #region 디버그 (VFXScene 검증용)

    /// <summary>지금 살아 있는 이펙트 수.</summary>
    public int ActiveEffectCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < _slots.Count; i++) if (_slots[i].inUse) count++;
            return count;
        }
    }

    /// <summary>이 프리팹으로 만들어진 인스턴스 총수. 연속 발화 후에도 늘지 않아야 한다.</summary>
    public int PoolCountAll(GameObject prefab) => _pool != null ? _pool.CountAll(prefab) : 0;

    /// <summary>지금 대출 중인 인스턴스 수.</summary>
    public int PoolCountActive(GameObject prefab) => _pool != null ? _pool.CountActive(prefab) : 0;

    #endregion

    #region 내부 데이터

    /// <summary>재생 중인 이펙트 하나. 슬롯으로 재사용된다.</summary>
    private class ActiveEffect
    {
        public int slot;
        public int generation;
        public bool inUse;

        public EffectEntry entry;
        public bool looping;
        public bool released;      // Release() 호출됨 → outro 진행 중

        public bool lifeCounting;
        public float life;         // 남은 수명(초)
        public float playRate = 1f;

        public Transform follow;   // null = 월드 고정
        public bool attached;      // 추종 대상을 지정하고 시작했는가 (대상 소멸 감지용)
        public Vector3 offset;     // 추종 지점 기준 오프셋
        public Vector3 position;   // 현재 기준 위치(월드)
        public Quaternion rotation;

        public readonly List<SpawnedPart> instances = new List<SpawnedPart>();
        public readonly List<PendingPart> pending = new List<PendingPart>();
    }

    private struct SpawnedPart
    {
        public GameObject go;
        public Vector3 offset;
    }

    private struct PendingPart
    {
        public EffectPart part;
        public float remaining;
        public bool isOutro;
    }

    #endregion
}
