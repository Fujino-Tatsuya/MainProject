using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 몬스터의 Animator와 NavMeshAgent에 "시간 배율"을 동시에 적용하는 컴포넌트.
/// HitStop(타격감용 순간 정지)과 SlowMotion(슬로우모션)의 공용 기반이다.
///
/// 설계 포인트:
/// - animator.speed는 전역 Time.timeScale 위에 곱해지는 배수라, 전역 시간이 멈추면
///   배율과 무관하게 몬스터도 자동으로 멈춘다.
/// - NavMeshAgent의 speed/angularSpeed/acceleration은 절대값이므로, Awake에 캐싱한
///   "기준값 × 배율"로 계산해 적용한다.
/// - HitStop/SlowMotion의 duration 카운트다운은 Scaled(전역 Time.timeScale에 종속) 기준.
///   전역 정지 시 몬스터 정지와 카운트다운이 함께 멈췄다가 함께 재개된다.
/// </summary>
public class MonsterTimeController : MonoBehaviour
{
    [SerializeField] Animator animator;
    [SerializeField] NavMeshAgent agent;

    // Awake에 캐싱한 NavMeshAgent 기준값 (배율 1일 때의 원본 값)
    float baseSpeed;
    float baseAngularSpeed;
    float baseAcceleration;

    // 현재 적용 중인 시간 배율
    float currentScale = 1f;
    public float CurrentScale => currentScale;

    Coroutine hitStopRoutine;
    Coroutine slowMotionRoutine;

    void Awake()
    {
        if (agent != null)
        {
            baseSpeed = agent.speed;
            baseAngularSpeed = agent.angularSpeed;
            baseAcceleration = agent.acceleration;
        }
    }

    /// <summary>
    /// Animator와 NavMeshAgent에 시간 배율을 동시에 적용한다.
    /// 모든 시간 제어(HitStop/SlowMotion)는 최종적으로 이 메서드로 수렴한다.
    /// </summary>
    public void SetTimeScale(float scale)
    {
        currentScale = scale;

        if (animator != null)
            animator.speed = scale;

        if (agent != null)
        {
            agent.speed = baseSpeed * scale;
            agent.angularSpeed = baseAngularSpeed * scale;
            agent.acceleration = baseAcceleration * scale;
        }
    }

    /// <summary>배율을 1(정상 속도)로 되돌린다.</summary>
    public void ResetTimeScale() => SetTimeScale(1f);

    /// <summary>
    /// duration(초) 동안 0배로 완전 정지한 뒤 "직전 배율"로 복원한다. (타격감용)
    /// 슬로우모션 중 피격되면 HitStop 후 다시 그 슬로우모션 배율로 돌아간다.
    /// </summary>
    public void HitStop(float duration)
    {
        if (hitStopRoutine != null)
            StopCoroutine(hitStopRoutine);
        hitStopRoutine = StartCoroutine(HitStopRoutine(duration));
    }

    IEnumerator HitStopRoutine(float duration)
    {
        float restoreScale = currentScale;   // 직전 배율 기억 (슬로우모션 중이면 그 값)
        SetTimeScale(0f);

        // Scaled 기준: WaitForSeconds는 전역 Time.timeScale에 종속되어, 게임 정지 시 함께 멈춘다.
        yield return new WaitForSeconds(duration);

        SetTimeScale(restoreScale);
        hitStopRoutine = null;
    }

    /// <summary>
    /// 3단계 곡선형 슬로우모션.
    /// easeInTime 동안 1배 → targetScale로 부드럽게(SmoothStep) 진입 →
    /// holdTime 동안 targetScale 유지 →
    /// easeOutTime 동안 다시 1배로 부드럽게 복귀한다.
    /// </summary>
    /// <param name="targetScale">가장 느려졌을 때의 배율 (예: 0.2)</param>
    /// <param name="easeInTime">1배 → targetScale 진입에 걸리는 시간(초)</param>
    /// <param name="holdTime">targetScale를 유지하는 시간(초)</param>
    /// <param name="easeOutTime">targetScale → 1배 복귀에 걸리는 시간(초)</param>
    public void SlowMotion(float targetScale, float easeInTime, float holdTime, float easeOutTime)
    {
        if (slowMotionRoutine != null)
            StopCoroutine(slowMotionRoutine);
        slowMotionRoutine = StartCoroutine(SlowMotionRoutine(targetScale, easeInTime, holdTime, easeOutTime));
    }

    IEnumerator SlowMotionRoutine(float targetScale, float easeInTime, float holdTime, float easeOutTime)
    {
        // easeIn: 1배 → targetScale
        yield return Ramp(1f, targetScale, easeInTime);

        SetTimeScale(targetScale);

        // hold: targetScale 유지 (Scaled 기준 — 전역 정지 시 함께 멈춤)
        yield return new WaitForSeconds(holdTime);

        // easeOut: targetScale → 1배
        yield return Ramp(targetScale, 1f, easeOutTime);

        SetTimeScale(1f);
        slowMotionRoutine = null;
    }

    /// <summary>from → to 배율을 duration(초) 동안 SmoothStep 곡선으로 보간 적용한다.</summary>
    IEnumerator Ramp(float from, float to, float duration)
    {
        if (duration <= 0f)
        {
            SetTimeScale(to);
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            // Scaled 기준: 전역 Time.timeScale과 함께 진행/정지한다.
            elapsed += Time.deltaTime;
            float u = Mathf.Clamp01(elapsed / duration);
            SetTimeScale(Mathf.Lerp(from, to, SmoothStep(u)));
            yield return null;
        }
    }

    /// <summary>SmoothStep 이징: 3u² - 2u³ (시작·끝 기울기 0의 S곡선).</summary>
    static float SmoothStep(float u) => u * u * (3f - 2f * u);
}
