using System;
using Unity.Netcode;
using UnityEngine;

// 2구역 진입 전 적 소개 연출(PLAN-main-radio-zone1-4 §4.2 · 명세 §4.2) — 디렉터 쪽 몫은 **복제 상태 + 스킵 표 RPC** 뿐이다(4구역과 같은 틀).
// 진행(서버)은 MapSceneZone2IntroFlow, 표시(모든 피어)는 MapSceneZone2IntroPresentation. 게이트 보류 API 는 MapSceneDirector.Zone4Intro.cs 공용.
public sealed partial class MapSceneDirector
{
    /// <summary>2구역 소개 연출 복제 상태 — 모든 피어가 이 값과 연출 데이터(SO)만으로 같은 화면을 그린다.</summary>
    public struct Zone2IntroState : INetworkSerializable, IEquatable<Zone2IntroState>
    {
        public IntroPhase Phase;
        /// <summary>연출 회차 — 시작마다 +1. 스킵 표·로컬 홀드 게이지 기준.</summary>
        public int Sequence;
        /// <summary>연출 시작 ServerTime.</summary>
        public double StartServerTime;
        /// <summary>연출 시작부터 카메라 복귀를 시작하는 시각(초). 미정 -1.</summary>
        public float ReturnAt;
        /// <summary>복귀 블렌드(초) — 자연 진행과 스킵이 다르다.</summary>
        public float ReturnSeconds;
        /// <summary>전원 스킵했다(스킵은 지금 자리에서 바로 복귀).</summary>
        public bool Skipped;
        public byte Votes;
        public byte Voters;
        /// <summary>적 무리 중심(서버가 시작 때 존2 장부 몬스터로 잰다 — 클라마다 몬스터 목록이 달라도 같은 화면).</summary>
        public Vector3 Focus;
        /// <summary>적 무리를 감싸는 반경(m) — 자동 맞춤 거리.</summary>
        public float Radius;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Phase);
            s.SerializeValue(ref Sequence);
            s.SerializeValue(ref StartServerTime);
            s.SerializeValue(ref ReturnAt);
            s.SerializeValue(ref ReturnSeconds);
            s.SerializeValue(ref Skipped);
            s.SerializeValue(ref Votes);
            s.SerializeValue(ref Voters);
            s.SerializeValue(ref Focus);
            s.SerializeValue(ref Radius);
        }

        public bool Equals(Zone2IntroState o) =>
            Phase == o.Phase && Sequence == o.Sequence && StartServerTime.Equals(o.StartServerTime) && ReturnAt.Equals(o.ReturnAt) &&
            ReturnSeconds.Equals(o.ReturnSeconds) && Skipped == o.Skipped && Votes == o.Votes && Voters == o.Voters &&
            Focus.Equals(o.Focus) && Radius.Equals(o.Radius);

        public static Zone2IntroState Empty => new Zone2IntroState { ReturnAt = -1f };

        public float Elapsed(double serverNow) => (float)(serverNow - StartServerTime);

        public bool IsReturning(double serverNow) => ReturnAt >= 0f && Elapsed(serverNow) >= ReturnAt;

        /// <summary>더 이상 스킵을 받지 않는다 — 이미 스킵했거나 복귀가 시작됐다.</summary>
        public bool IsSkipClosed(double serverNow) => Skipped || IsReturning(serverNow);
    }

    private readonly NetworkVariable<Zone2IntroState> _zone2Intro = new NetworkVariable<Zone2IntroState>(
        Zone2IntroState.Empty, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>[모든 피어] 2구역 소개 연출 상태.</summary>
    public Zone2IntroState Zone2Intro => IsSpawned ? _zone2Intro.Value : Zone2IntroState.Empty;

    /// <summary>[서버] 연출 상태를 쓴다(바뀐 때만).</summary>
    public void SetZone2IntroServer(Zone2IntroState state)
    {
        if (!IsServer || !IsSpawned || _zone2Intro.Value.Equals(state)) return;
        _zone2Intro.Value = state;
    }

    /// <summary>[서버] 스킵 표가 왔다(보낸 사람, 회차). 검증·집계는 MapSceneZone2IntroFlow 가 한다.</summary>
    public static event Action<ulong, int> Zone2IntroSkipVoted;

    /// <summary>[아무 피어] 스킵 동의(1초 홀드 완료).</summary>
    public void RequestZone2IntroSkipVote(int sequence)
    {
        if (IsSpawned) Zone2IntroSkipVoteServerRpc(sequence);
    }

    [ServerRpc(RequireOwnership = false)]
    private void Zone2IntroSkipVoteServerRpc(int sequence, ServerRpcParams p = default) =>
        Zone2IntroSkipVoted?.Invoke(p.Receive.SenderClientId, sequence);

    /// <summary>[서버] 구간 사각 영역(존 레이아웃). 몬스터가 없을 때 2구역 샷 중심으로 쓴다.</summary>
    public bool TryGetAreaRectServer(MapSceneArea area, out MapSceneRect rect)
    {
        rect = default;
        return _areas != null && _areas.TryGetArea(area, out rect);
    }

    // 모든 피어 — 연출 표시 컴포넌트(OnNetworkSpawn 에서 부른다).
    private void EnsureZone2IntroPresentation()
    {
        if (GetComponent<MapSceneZone2IntroPresentation>() == null)
            gameObject.AddComponent<MapSceneZone2IntroPresentation>();
    }
}
