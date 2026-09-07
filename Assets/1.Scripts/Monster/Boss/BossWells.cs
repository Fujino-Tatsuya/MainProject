using Unity.Netcode;
using UnityEngine;

// Wells(웰즈) — 23호 등에 탑승한 폭탄 투척수.
//
// 정본: Docs/tech/boss-fsm-detailed-spec.md §10 / §10.1 / §10.2.
//
// 🔴 **`MonoBehaviour` 다. `NetworkBehaviour` 로 만들면 안 된다.**
//    Wells 는 프리팹의 **중첩 NetworkObject** 인데 NGO 는 그것을 스폰하지 않는다(씬 오브젝트만 지원).
//    `NetworkBehaviour.IsServer` 는 계산 프로퍼티가 아니라 **스폰 시 대입되는 필드**라 미스폰이면
//    영원히 false 다 — 그래서 과거에 이 클래스가 `NetworkBehaviour` 였을 때
//    **모든 애니메이션 이벤트를 조용히 삼켰다**(2026-07-31 사고, `58278e9`).
//    서버 판정은 반드시 `NetworkManager.Singleton.IsServer` 로 한다.
//
// 🔴 **상태 복제도 자기가 못 한다** → 23호의 `NetworkObject` 에 실어 보내고(`TwentyThreeBoss`),
//    각 피어가 <see cref="PlayState"/> 로 로컬 애니메이터만 구동한다.
//
// ⚠️ **애니메이터 규약이 23호와 다르다.** 23호는 `State` Int 로 구동하지만 Wells 는 **전부 트리거**다.
//    Wells 의 `State` Int 는 전이 조건에 안 쓰이는 **죽은 파라미터**다(§10.2).
//    `Throw` 클립은 `hasExitTime: true` 라 끝나면 **스스로 Idle 로 돌아온다**(23호와 반대).
//
// 🔴 **클립 이벤트 이름은 fbx(SVN)에 박혀 있다** — `ThrowBombEvent` / `BombDestroyEvent`.
//    이름을 바꾸면 이벤트가 **조용히 무시된다**(애니 접근이 graceful 이라 에러도 안 난다).
[DisallowMultipleComponent]
public class BossWells : MonoBehaviour
{
    [Header("애니메이터 — 🔴 Wells 는 전부 트리거다(23호는 Int)")]
    [SerializeField]
    [Tooltip("Wells 전용 Animator(비우면 자기 계층에서 탐색). 23호 것과 다른 컨트롤러다.")]
    Animator animator;
    [SerializeField] [Tooltip("Idle → Throw 트리거.")] string throwTrigger = "IsThrow";
    [SerializeField] [Tooltip("AnyState → Groggy 트리거.")] string groggyTrigger = "IsGroggy";
    [SerializeField] [Tooltip("AnyState → Die 트리거.")] string deadTrigger = "IsDead";
    [SerializeField] [Tooltip("Groggy → Idle 복귀 트리거.")] string initTrigger = "IsInit";

    [Header("투척")]
    [SerializeField]
    [Tooltip("폭탄이 생성될 손 소켓. 비우면 이 오브젝트 위치에서 던진다.")]
    Transform bombSocket;
    [SerializeField]
    [Tooltip("투척 모션 중 손에 들고 있는 **로컬 비주얼**(NetworkObject 없는 단순 메시). " +
             "비워도 무해하다 — 폭탄 실물은 던지는 순간 서버가 스폰한다.")]
    GameObject heldBombVisual;

    /// <summary>폭탄 소켓(없으면 자기 트랜스폼). 보스가 스폰 위치·방향으로 쓴다.</summary>
    public Transform BombSocket => bombSocket != null ? bombSocket : transform;

    /// <summary>
    /// 클립 이벤트(<c>ThrowBombEvent</c>)가 서버에서 도달했을 때 호출된다. 보스가 스폰을 담당한다
    /// (SO 의 프리팹·임펄스·분산이 보스 쪽 데이터이므로).
    /// </summary>
    public System.Action ThrowRequested;

    /// <summary>투척 주기가 만료됐다(서버). 보스가 받아서 투척 애니를 전 피어에 브로드캐스트한다.</summary>
    public System.Action ThrowCycleElapsed;

    BossWellsState _state = BossWellsState.Idle;
    public BossWellsState State => _state;

    // 서버 전용 주기 — 🔴 **23호 상태와 무관하게 자기 주기로 살포한다**(정본 §10).
    //    23호가 밀어주는 것은 그로기·사망 억제뿐이다.
    float _cycleInterval = 6f;
    float _cycle;
    bool _suppressed;   // 보스가 밀어준 억제(그로기/사망). 🔴 Wells 가 보스를 폴링하지 않는다 —
                        //    폴링하면 순서 의존이 생긴다(정본 §10).

    void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        ShowHeldBomb(false);
    }

    /// <summary>투척 주기(초)를 설정한다(보스가 SO 값으로 스폰 시 1회).</summary>
    public void ConfigureCycle(float interval)
    {
        _cycleInterval = Mathf.Max(0.1f, interval);
        _cycle = _cycleInterval;
    }

    /// <summary>보스가 그로기·사망을 밀어준다. 억제 중에는 폭탄 주기가 멈춘다.</summary>
    public void SetSuppressed(bool suppressed)
    {
        if (_suppressed == suppressed) return;
        _suppressed = suppressed;
        // 억제가 풀리면 주기를 처음부터 — 그로기 직후 바로 던지는 것을 막는다.
        if (!suppressed) _cycle = _cycleInterval;
    }

    void Update()
    {
        // 🔴 NetworkBehaviour 가 아니므로 이 Update 는 base 를 가리지 않는다.
        //    (보스 파생에 Update 를 선언하면 MonsterBase.Update 를 가려 FSM 이 통째로 멈춘다.)
        if (!IsServerRuntime() || _suppressed) return;

        _cycle -= Time.deltaTime;
        if (_cycle > 0f) return;

        _cycle = _cycleInterval;

        // 🔴 투척 체인 진단(2026-08-13). "폭탄이 한 개도 안 나온다"의 끊긴 지점을 가른다.
        //    체인 = ①주기 만료 → ②보스가 Throw 상태 브로드캐스트 → ③클립의 ThrowBombEvent
        //    → ④보스가 폭탄 스폰. 각 단계가 자기 이름을 남기므로 **안 찍히는 로그가 곧 범인**이다.
        //    (같은 지점에서 두 번 추측이 빗나가면 진단을 심는다 — 교훈 #24·#72.)
        Debug.Log($"[Wells/진단] ① 주기 만료 — 구독자 {(ThrowCycleElapsed != null ? "있음" : "🔴없음")} " +
                  $"· 주기 {_cycleInterval}s · 억제 {_suppressed}", this);

        ThrowCycleElapsed?.Invoke();
    }

    // ─── 상태 → 로컬 애니메이터 (모든 피어) ────────────────────────────
    public void PlayState(BossWellsState next)
    {
        _state = next;

        // ② 상태 브로드캐스트 도달. 애니메이터가 없거나 컨트롤러가 비면 여기서 조용히 끝나므로
        //    **그 사실을 찍는다** — 이 줄이 "Throw" 로 찍히는데 ③이 안 오면 클립 쪽 문제다.
        if (next == BossWellsState.Throw)
            Debug.Log($"[Wells/진단] ② Throw 상태 수신 — animator={(animator != null ? "있음" : "🔴없음")} " +
                      $"· controller={(animator != null && animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "🔴없음")} " +
                      $"· 활성={isActiveAndEnabled} · 트리거='{throwTrigger}'", this);

        if (animator == null || animator.runtimeAnimatorController == null) return;

        switch (next)
        {
            case BossWellsState.Throw:
                ShowHeldBomb(true);
                SafeTrigger(throwTrigger);
                break;

            case BossWellsState.Groggy:
                ShowHeldBomb(false);
                SafeTrigger(groggyTrigger);
                break;

            case BossWellsState.Dead:
                ShowHeldBomb(false);
                SafeTrigger(deadTrigger);
                break;

            case BossWellsState.Idle:
                // Groggy 에서 돌아올 때만 의미가 있다. Throw 는 hasExitTime 으로 스스로 Idle 로 간다.
                SafeTrigger(initTrigger);
                break;
        }
    }

    // ─── 애니메이션 이벤트 (fbx/SVN 에 박힌 이름 — 바꾸지 말 것) ─────────
    // 각 피어의 애니메이터가 클립을 재생하므로 이 메서드는 **모든 피어에서** 불린다.
    // 폭탄 스폰은 서버만, 비주얼 정리는 전 피어가 한다.
    public void ThrowBombEvent()
    {
        ShowHeldBomb(false); // 손에서 사라지는 연출은 모든 피어

        // ③ 클립 이벤트 도달. 이 줄이 안 찍히면 클립이 재생되지 않은 것이다
        //    (컨트롤러 전이 조건 · 트리거 이름 불일치 · 애니메이터 컬링을 의심).
        Debug.Log($"[Wells/진단] ③ ThrowBombEvent 도달 — 서버={IsServerRuntime()} " +
                  $"· 구독자 {(ThrowRequested != null ? "있음" : "🔴없음")}", this);

        if (!IsServerRuntime()) return;
        ThrowRequested?.Invoke();
    }

    // 레거시에서 들고 있던 폭탄을 정리하던 이벤트. 비주얼 정리만 한다(실물 폭탄은 자기 수명이 있다).
    public void BombDestroyEvent() => ShowHeldBomb(false);

    void ShowHeldBomb(bool visible)
    {
        if (heldBombVisual != null && heldBombVisual.activeSelf != visible)
            heldBombVisual.SetActive(visible);
    }

    void SafeTrigger(string param)
    {
        if (string.IsNullOrEmpty(param)) return;
        AnimatorControllerParameter[] ps = animator.parameters;
        for (int i = 0; i < ps.Length; i++)
        {
            if (ps[i].name != param) continue;
            animator.SetTrigger(param);
            return;
        }
        // 없으면 조용히 넘긴다 — 계약 검증은 보스가 스폰 시 한 번에 LogError 로 낸다.
    }

    /// <summary>
    /// 애니메이터 계약 검증. 🔴 접근이 graceful 이라 이름이 틀려도 에러가 안 나므로,
    /// 보스가 스폰 시 한 번 호출해 죽은 설정값을 드러낸다.
    /// </summary>
    public void ValidateContract(string ownerName)
    {
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            Debug.LogWarning($"{ownerName}/Wells: Animator 컨트롤러가 없어 계약 검증을 건너뛴다.", this);
            return;
        }

        RequireParam(ownerName, throwTrigger, nameof(throwTrigger));
        RequireParam(ownerName, groggyTrigger, nameof(groggyTrigger));
        RequireParam(ownerName, deadTrigger, nameof(deadTrigger));
        RequireParam(ownerName, initTrigger, nameof(initTrigger));

        if (bombSocket == null)
            Debug.LogWarning(
                $"{ownerName}/Wells: bombSocket 이 비어 있어 Wells 원점에서 폭탄이 나간다(손이 아니다).", this);
    }

    void RequireParam(string ownerName, string param, string field)
    {
        if (string.IsNullOrEmpty(param)) return;

        AnimatorControllerParameter[] ps = animator.parameters;
        for (int i = 0; i < ps.Length; i++)
            if (ps[i].name == param) return;

        Debug.LogError(
            $"{ownerName}/Wells: {field}=\"{param}\" 파라미터가 Wells 애니메이터에 없다 — 조용히 무시된다.", this);
    }

    // 🔴 NetworkBehaviour.IsServer 를 쓰면 안 된다(미스폰 중첩 NetworkObject 라 영원히 false).
    static bool IsServerRuntime() =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
}
