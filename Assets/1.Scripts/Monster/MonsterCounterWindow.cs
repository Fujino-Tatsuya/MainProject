using UnityEngine;

/// <summary>
/// 중간보스 인터럽트 카운터의 <b>저작값 + 창 상태</b>. 프리팹 루트에 붙인다.
///
/// 역할 분담(2026-09-08 확정):
/// <list type="bullet">
/// <item><b>이 컴포넌트</b> — 창 길이·그로기 길이를 저작하고 <see cref="CounterWindow"/> 하나를 든다.</item>
/// <item><b><c>MonsterBase</c></b> — 자세 홀드와 텔레그래프를 전 피어에 전달한다(표현).</item>
/// <item><b>몬스터 클래스</b>(SpinnerBot·GauntletBot) — 언제 열고 닫는지, 성공 시 무엇을 취소하는지.</item>
/// </list>
///
/// 🔴 <b>붙어 있지 않으면 카운터가 없는 것</b>이다 — 몬스터 코드가 이 컴포넌트 유무로 분기하므로
///    일반몹 8종과 23호에는 어떤 경로도 생기지 않는다(23호는 자기 카운터 계통을 따로 갖는다).
///
/// ⚠️ 23호의 <c>BossCounterWindupGate</c>(타이머 ∧ 애니 이벤트)를 쓰지 않는다. 그쪽은 <b>히트가
///    클립 이벤트에서 나오기 때문에</b> 두 사건의 논리곱이 필요했다. 중간보스는 자세를 얼려 두고
///    창이 끝나면 애니를 다시 굴리므로, 히트 시점은 클립이 알아서 맞춘다 — 타이머 하나면 된다.
///    (Gauntlet 의 <c>OnAttackHit</c> 는 이미 타격 시점이라 준비 신호로 쓸 수도 없다.)
/// </summary>
[DisallowMultipleComponent]
public class MonsterCounterWindow : MonoBehaviour
{
    [Header("창")]
    [SerializeField, Min(0f)]
    [Tooltip("인터럽트를 받을 수 있는 시간(초). 이 동안 자세가 정지한다. 0 = 카운터 없음.")]
    float windowDuration = 1.5f;

    [Header("성공 상자")]
    [SerializeField, Range(0f, 2f)]
    [Tooltip("카운터 성공 시 그로기 시간(초). 기본 0.5, 길어도 1 을 넘기지 않는다(팀장 확정).")]
    float groggyDuration = 0.5f;

    readonly CounterWindow _window = new CounterWindow();

    /// <summary>저작된 창 길이(초). 공격 상태 타이머에 이 값을 더해야 한다 — 애니를 멈춰도 타이머는 준다.</summary>
    public float WindowDuration => windowDuration;

    /// <summary>카운터 성공 시 그로기 길이(초).</summary>
    public float GroggyDuration => groggyDuration;

    /// <summary>창이 열려 있는가.</summary>
    public bool IsOpen => _window.IsOpen;

    /// <summary>저작된 길이로 창을 연다. 길이가 0 이면 열리지 않는다(카운터 없음).</summary>
    public void Open() => _window.Open(windowDuration);

    /// <summary>시간을 흘린다. <b>이번 틱에 만료됐으면 true</b> — 호출측이 "실패 확정"을 한 번만 처리한다.</summary>
    public bool TickAndDetectExpiry(float deltaTime) => _window.TickAndDetectExpiry(deltaTime);

    /// <summary>인터럽트를 소비한다(창 하나에 한 번만). true 면 성공 — 창은 즉시 닫힌다.</summary>
    public bool TryConsumeInterrupt() => _window.TryConsumeInterrupt();

    /// <summary>창을 비운다. 공격 취소·사망·디스폰에서 부른다. 멱등이다.</summary>
    public void Close() => _window.Close();
}
