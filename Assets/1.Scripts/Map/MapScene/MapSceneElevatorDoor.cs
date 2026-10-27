using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 보스 진입 엘리베이터 문을 G10 과 함께 연다(S5) — <b>모든 피어</b>, 비네트워크. 열림 여부는 디렉터 복제 게이트(<see cref="MapSceneDirector.IsGateOpen"/>)라
/// 늦은 클라도 같은 그림이 된다(이미 열렸으면 애니 없이 끝 프레임으로).
///
/// · 문 = <c>ZoneS_typeBossEnter</c> 안 <c>object_MV_bossroomenter</c> 의 Animator(컨트롤러 <c>MV_BossroomEnter_Doors</c> — 4.Animations, git).
///   컨트롤러에 파라미터·전이가 없다(기본 상태 Closed, Open·Close 는 따로 떠 있는 상태) → 애셋을 고치지 않고 <see cref="Animator.Play(string,int,float)"/> 로 Open 을 직접 재생한다.
///   공용 존 프리팹·컨트롤러 무수정 — 절차 맵은 기존대로 Closed.
/// · 존은 각 피어가 맵 생성 때 스폰한다(디렉터 스폰과 순서 무관) → 문을 못 찾으면 잠깐 뒤 다시 찾는다.
/// · <see cref="MapSceneStageMarker"/> 가 Play 중 자기 루트에 붙인다(배리어와 같은 방식).
/// </summary>
[DisallowMultipleComponent]
public sealed class MapSceneElevatorDoor : MonoBehaviour
{
    public const string DoorControllerName = "MV_BossroomEnter_Doors";
    public const string OpenStateName = "Open";
    const float RetryInterval = 0.5f;

    static readonly int OpenStateHash = Animator.StringToHash(OpenStateName);

    readonly List<Animator> _doors = new List<Animator>();
    bool _sawClosed;   // 이 피어에서 G10 이 닫혀 있는 것을 본 적이 있다 → 열 때 애니 재생(아니면 늦은 클라 — 끝 프레임)
    bool _opened;
    bool _warned;
    float _retry;

    public bool IsOpened => _opened;

    void Update()
    {
        if (_opened) return;
        _retry -= Time.unscaledDeltaTime;
        if (_retry > 0f) return;
        _retry = RetryInterval;

        MapSceneDirector d = MapSceneDirector.Instance;
        if (d == null || !d.IsTutorialActive) return;   // 튜토리얼이 아니면 손대지 않는다

        bool g10 = d.IsGateOpen(MapSceneGate.G10_BossEnterAndPad);
        if (!MapSceneBossPathRules.ShouldOpenElevatorDoor(true, g10))
        {
            _sawClosed = true;
            return;
        }

        if (!FindDoors())
        {
            if (_warned) return;
            _warned = true;
            Edit.LogWarning($"[Tutorial] 엘리베이터 문 — '{DoorControllerName}' Animator 를 못 찾았다(보스 진입 존 스폰 전?). {RetryInterval}초 뒤 다시 찾는다.", this);
            return;
        }

        foreach (Animator a in _doors)
        {
            // 보스 엘리베이터 연출(BossElevatorRig)이 같은 문을 쥔다 — 대기 포즈를 열림으로 알려 두고,
            // 연출 재생 중이면 손대지 않는다(리그가 끝·리셋 때 Open 끝 프레임으로 돌려놓는다).
            BossElevatorRig rig = a.GetComponentInParent<BossElevatorRig>();
            if (rig != null)
            {
                rig.IdleDoorOpen = true;
                if (rig.IsPlaying) continue;
            }

            // 늦은 클라(처음 봤을 때 이미 열림) = 끝 프레임으로. Open 클립은 반복하지 않아 끝에서 멈춘다.
            a.Play(OpenStateHash, 0, _sawClosed ? 0f : 1f);
        }
        _opened = true;
        Edit.Log($"[Tutorial] 엘리베이터 문 열림 — {_doors.Count}개 ({(_sawClosed ? "애니" : "늦은 반영, 끝 프레임")}).", this);
    }

    bool FindDoors()
    {
        _doors.Clear();
        foreach (Animator a in FindObjectsByType<Animator>(FindObjectsSortMode.None))
        {
            RuntimeAnimatorController c = a.runtimeAnimatorController;
            if (c == null || c.name != DoorControllerName) continue;
            if (!a.HasState(0, OpenStateHash))
            {
                Edit.LogWarning($"[Tutorial] 엘리베이터 문 '{a.name}' 에 '{OpenStateName}' 상태가 없다 — 건너뛴다.", a);
                continue;
            }
            _doors.Add(a);
        }
        return _doors.Count > 0;
    }
}
