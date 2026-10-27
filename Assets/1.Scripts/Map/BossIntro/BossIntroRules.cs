using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 보스 등장 연출(PLAN-boss-encounter-cutscene) 대열 — 순수 계산(네트워크·씬 모름, EditMode 테스트 대상).
/// 착지점에서 화면 아래(<c>south</c>)로 <c>stopDistance</c> 떨어진 정지선에 1인 = 중앙, 2인 = 가로 일자,
/// 3인 = 삼각형 꼭짓점(선두 중앙 + 좌우 반보 뒤) 또는 가로 일자. 출발점 = 정지점에서 <c>south</c> 로 <c>walkDistance</c>.
/// 🔴 전원 <b>동시에</b> 걷는다(엘리베이터처럼 한 줄로 순서대로 X — 경석 10-23).
/// </summary>
public static class BossIntroFormation
{
    /// <summary>슬롯 <paramref name="slot"/>(0 = 선두)의 정지 자리. 높이는 <paramref name="landing"/>.y 그대로(호출자가 바닥 높이로 맞춘다).</summary>
    public static Vector3 StopPoint(Vector3 landing, Vector3 south, int slot, int count,
                                    float stopDistance, float spacing, float triangleBack, bool triangleForThree)
    {
        Vector3 s = Flat(south);
        Vector3 right = Vector3.Cross(Vector3.up, s);   // 화면 기준 좌우 — 대칭이라 부호는 상관없다
        Vector2 o = Offset(slot, count, spacing, triangleBack, triangleForThree);
        return landing + s * (stopDistance + o.y) + right * o.x;
    }

    /// <summary>정지 자리에서 화면 아래로 <paramref name="walkDistance"/> — 걷기 시작하는 자리(보스방 도착 지점).</summary>
    public static Vector3 StartPoint(Vector3 stop, Vector3 south, float walkDistance) =>
        stop + Flat(south) * Mathf.Max(0f, walkDistance);

    /// <summary>대열 오프셋 — x = 좌우, y = 정지선보다 뒤(보스에서 더 멀리).</summary>
    public static Vector2 Offset(int slot, int count, float spacing, float triangleBack, bool triangleForThree)
    {
        count = Mathf.Max(1, count);
        slot = Mathf.Clamp(slot, 0, count - 1);
        if (count == 1) return Vector2.zero;

        if (count == 3 && triangleForThree)
        {
            // 선두(호스트 = slot0)가 꼭짓점, 둘이 좌우 반보 뒤.
            if (slot == 0) return Vector2.zero;
            return new Vector2(slot == 1 ? -spacing : spacing, triangleBack);
        }

        // 가로 일자 — 가운데 정렬.
        float x = (slot - (count - 1) * 0.5f) * spacing;
        return new Vector2(x, 0f);
    }

    /// <summary>바닥 걷기용 수평 단위 벡터. 0 이면 월드 −Z(고정 톱다운의 화면 아래).</summary>
    public static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.back;
    }

    /// <summary>걷기 시간(초) — 거리 ÷ 속도. 속도 0 이하면 0(즉시).</summary>
    public static float WalkDuration(float walkDistance, float walkSpeed) =>
        walkSpeed > 0f ? Mathf.Max(0f, walkDistance) / walkSpeed : 0f;

    /// <summary>충격파 밀림 진행(0~1) — 처음 빠르고 끝에 멈추는 감속(ease-out quad).</summary>
    public static float SkidEase(float t)
    {
        t = Mathf.Clamp01(t);
        return 1f - (1f - t) * (1f - t);
    }
}

/// <summary>
/// 연출 스킵 투표(서버) — 투표자 명단은 호출자가 넘긴다(보스 등장·4구역 소개 = 접속자 전원, 관전자 포함 — 경석 10-26. ⚠️ 10-23 "영구 사망 제외"는 뒤집음).
/// 1초 홀드를 채운 표는 <b>유지</b>된다(손을 떼도 취소 X, 경석 10-23). 명단에서 빠지면 그 표도 빠진다. 전원 동의(만장일치)면 스킵.
/// </summary>
public sealed class BossIntroSkipVotes
{
    private readonly HashSet<ulong> _votes = new HashSet<ulong>();

    public int Count => _votes.Count;

    /// <summary>표를 넣는다. 참가자가 아니거나 이미 넣었으면 false.</summary>
    public bool TryVote(ulong clientId, IReadOnlyList<ulong> eligible)
    {
        if (eligible == null || !Contains(eligible, clientId)) return false;
        return _votes.Add(clientId);
    }

    /// <summary>참가자 명단에서 빠진 사람의 표를 지운다. 바뀌었으면 true.</summary>
    public bool Prune(IReadOnlyList<ulong> eligible)
    {
        int before = _votes.Count;
        _votes.RemoveWhere(id => eligible == null || !Contains(eligible, id));
        return _votes.Count != before;
    }

    /// <summary>참가자 전원이 동의했다(참가자 0명이면 false).</summary>
    public bool IsUnanimous(IReadOnlyList<ulong> eligible)
    {
        if (eligible == null || eligible.Count == 0) return false;
        for (int i = 0; i < eligible.Count; i++)
            if (!_votes.Contains(eligible[i])) return false;
        return true;
    }

    public void Clear() => _votes.Clear();

    private static bool Contains(IReadOnlyList<ulong> list, ulong id)
    {
        for (int i = 0; i < list.Count; i++)
            if (list[i] == id) return true;
        return false;
    }
}

/// <summary>
/// 로컬 스킵 홀드 게이지(클라). <see cref="Tick"/> 가 홀드를 채운 그 프레임에만 true(표 1회 전송).
/// 채운 뒤에는 손을 떼도 가득 찬 채로 남는다. 채우기 전에 떼면 0 으로.
/// </summary>
public sealed class BossIntroSkipHold
{
    private float _held;

    public bool Voted { get; private set; }

    /// <summary>0~1 — 버튼 외곽선 채움 정도.</summary>
    public float Progress(float holdSeconds) => Voted ? 1f : holdSeconds > 0f ? Mathf.Clamp01(_held / holdSeconds) : 0f;

    public bool Tick(bool pressed, float deltaTime, float holdSeconds)
    {
        if (Voted) return false;
        if (!pressed)
        {
            _held = 0f;
            return false;
        }

        _held += Mathf.Max(0f, deltaTime);
        if (_held < holdSeconds) return false;
        Voted = true;
        return true;
    }

    public void Reset()
    {
        _held = 0f;
        Voted = false;
    }
}
