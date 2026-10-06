using System;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 게임 시작 연출(PLAN-title-monitor T1): 중앙 모니터 **화면만** 처음 만든 CRT 꺼짐 연출로 꺼지고(전체 화면은 그대로) →
/// 꺼짐이 끝나면 카메라가 그 검은 화면으로 천천히 다가가며 → 검정 페이드 → 로비(로비는 진입 시 페이드 인).
/// (10-03 1판은 꺼짐과 접근을 동시에 했다 — 팀장 10-04 Play: "꺼진 뒤에 다가가야 한다"로 순차로 바꿈.)
/// </summary>
/// <remarks>
/// ⚠️ 09-23 팀장 "모니터처럼 딱 꺼지게(페이드 없음)" 를 10-03 요청이 바꿨다. 종료(EXIT)는 그대로 전체 화면 꺼짐(<see cref="TitlePowerOff"/>).
/// 🔴 로비 전환은 <see cref="TitleSceneManager.StartGame"/> 하나로 — 그게 검정 페이드 완료 후 GoToLobby 를 부른다.
///    <c>StartGameImmediate</c> 를 페이드 콜백으로 쓰면 <c>IsTransitioning</c> 잠금에 걸려 로비로 안 간다(Codex 10-03 확인).
/// 🔴 카메라 제어권을 여기서 가져간다 — Brain 해제는 메뉴 도착 1프레임 뒤라, 그 전에 시작하면 Brain 이 켜진 채 카메라를 덮어쓴다.
/// </remarks>
[DisallowMultipleComponent]
public sealed class TitleStartDive : MonoBehaviour
{
    [Header("시간 (초) — 순서: 모니터 꺼짐(카메라 고정) → 끝나면 천천히 다가감 + 검정 페이드 → 로비")]
    [Tooltip("모니터 화면만 꺼지는 시간 — 처음 만든 CRT 꺼짐 연출(Title/CRTOff)과 같은 모양·같은 1.1초. 이 동안 카메라는 모니터를 보고 멈춰 있다.")]
    [SerializeField, Min(0.05f)] private float _powerOffDuration = 1.1f;

    [Tooltip("꺼짐이 끝난 뒤 카메라가 검은 화면으로 천천히 다가가는 시간.")]
    [SerializeField, Min(0.1f)] private float _diveDuration = 2.8f;

    [Tooltip("다가가기 시작한 뒤 몇 초에 검정 페이드(=로비 전환)를 시작하나. 페이드 길이는 TitleSceneManager 의 페이드 설정(1.5초)을 따른다.\n" +
             "카메라가 멈추기 전에 화면이 다 검어지도록 잡는다.")]
    [SerializeField, Min(0f)] private float _fadeStartAt = 1.0f;

    [Header("카메라")]
    [Tooltip("화면 중심 앞 몇 m 에서 멈추나. 근접 클리핑보다 커야 화면을 뚫고 지나가지 않는다.")]
    [SerializeField, Min(0.01f)] private float _stopDistance = 0.12f;

    [Tooltip("가속 곡선 지수. 1 = 등속, 클수록 처음이 느리고 끝이 빠르다.")]
    [SerializeField, Range(1f, 4f)] private float _easePower = 2.2f;

    public bool Playing { get; private set; }

    /// <summary>
    /// 연출을 시작한다. 필요한 참조가 없으면 false — 호출측이 예전 경로(전체 화면 꺼짐)로 간다.
    /// </summary>
    public bool TryPlay(TitleMonitorDisplay monitor, CinemachineBrain brain, TitleSceneManager sceneManager)
    {
        Camera cam = brain != null ? brain.GetComponent<Camera>() : Camera.main;
        if (Playing || monitor == null || monitor.ScreenRenderer == null || cam == null || sceneManager == null)
            return false;

        StartCoroutine(Run(monitor, brain, cam, sceneManager));
        return true;
    }

    private IEnumerator Run(TitleMonitorDisplay monitor, CinemachineBrain brain, Camera cam, TitleSceneManager sceneManager)
    {
        Playing = true;

        // 카메라 제어권 — Brain 이 이번 프레임까지 Near 를 반영했으므로 지금 끄고 자세를 기록한다.
        if (brain != null && brain.enabled)
            brain.enabled = false;

        Transform camT = cam.transform;
        Vector3 fromPos = camT.position;
        Quaternion fromRot = camT.rotation;

        // 화면 중심 = 렌더러 bounds 중심(FBX 피벗은 메시 중심이 아니다). 방향 = 카메라 → 화면 중심.
        Vector3 center = monitor.ScreenRenderer.bounds.center;
        Vector3 dir = center - fromPos;
        float dist = dir.magnitude;
        if (dist < 1e-3f) dir = camT.forward; else dir /= dist;
        float stop = Mathf.Max(_stopDistance, cam.nearClipPlane * 1.5f);
        Vector3 toPos = center - dir * stop;
        Quaternion toRot = Quaternion.LookRotation(dir, Vector3.up);

        // ① 모니터만 꺼진다 — 카메라는 모니터를 본 채 멈춰 있다(팀장 10-04: 꺼짐이 끝난 뒤에 다가간다).
        yield return monitor.PowerOff(_powerOffDuration);

        // ② 검은 화면으로 천천히 다가가며 검정 페이드 → 로비.
        bool fadeRequested = false;
        float t = 0f;
        while (t < _diveDuration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / _diveDuration);
            float e = Mathf.Pow(k, _easePower);
            camT.SetPositionAndRotation(Vector3.LerpUnclamped(fromPos, toPos, e),
                                        Quaternion.Slerp(fromRot, toRot, Mathf.SmoothStep(0f, 1f, k)));

            if (!fadeRequested && t >= _fadeStartAt)
            {
                fadeRequested = true;
                RequestLobby(sceneManager);
            }
            yield return null;
        }

        camT.SetPositionAndRotation(toPos, toRot);
        if (!fadeRequested) RequestLobby(sceneManager);
        // 씬 전환은 TitleSceneManager 가 페이드 완료 뒤에 한다 — 여기서 기다릴 것은 없다.
    }

    private static void RequestLobby(TitleSceneManager sceneManager)
    {
        Debug.Log("[TitleStartDive] 검정 페이드 → 로비");
        sceneManager.StartGame();
    }
}
