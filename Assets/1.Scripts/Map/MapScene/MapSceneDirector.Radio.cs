using System;
using Unity.Collections;
using Unity.Netcode;

// 메인 1~4구역 일반 무전(PLAN-main-radio-zone1-4 §4.1) — 디렉터 쪽 몫은 **복제 상태 + 서버 재생 API** 뿐이다.
// 사건 판단은 흐름 파일(MapSceneZoneFlow·MapSceneBossApproachGuide), 표시는 모든 피어의 MapSceneRadioPresentation.
// 대기 줄은 없다 — 나중 줄이 앞 줄을 대신한다(명세 §8.1 "의미 없어진 대사 폐기"). 늦게 붙은 클라는 아직 표시 시간 안인 줄만 본다.
public sealed partial class MapSceneDirector
{
    /// <summary>지금 무전 한 줄 — 문구 id + 시작 ServerTime(사건 시각 + 지연). id 가 비면 무전 없음.</summary>
    public struct RadioState : INetworkSerializable, IEquatable<RadioState>
    {
        public FixedString64Bytes LineId;
        public double StartServerTime;
        /// <summary>재생마다 +1 — 같은 id 를 다시 받는 일은 없지만 표시가 "새 줄"을 알아보는 기준.</summary>
        public int Sequence;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref LineId);
            s.SerializeValue(ref StartServerTime);
            s.SerializeValue(ref Sequence);
        }

        public bool Equals(RadioState o) => LineId.Equals(o.LineId) && StartServerTime.Equals(o.StartServerTime) && Sequence == o.Sequence;
    }

    private readonly NetworkVariable<RadioState> _radio = new NetworkVariable<RadioState>(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly MapSceneRadioLog _radioLog = new MapSceneRadioLog();

    /// <summary>[모든 피어] 지금 무전 줄.</summary>
    public RadioState Radio => IsSpawned ? _radio.Value : default;

    /// <summary>
    /// [서버] 무전 한 줄을 튼다(파티 전체). 이번 판에 이미 튼 id 면 무시하고 false. 시작 시각 = 지금 + 데이터 지연(<see cref="MapSceneRadioData"/>).
    /// </summary>
    public bool PlayRadioServer(string id)
    {
        if (!RequireServerActive(nameof(PlayRadioServer)) || !_radioLog.TryPlay(id)) return false;
        MapSceneRadioLineTiming timing = MapSceneRadioData.Load().TimingOf(id);
        double start = NetworkManager.ServerTime.Time + timing.delaySeconds;
        _radio.Value = new RadioState { LineId = new FixedString64Bytes(id), StartServerTime = start, Sequence = _radio.Value.Sequence + 1 };
        Edit.Log($"[Radio] 무전 '{id}' — {timing.delaySeconds:0.##}초 뒤 시작.", this);
        return true;
    }

    /// <summary>[서버] 이번 판에 이미 튼 무전인가.</summary>
    public bool HasPlayedRadioServer(string id) => _radioLog.HasPlayed(id);

    // 모든 피어 — 무전 표시 컴포넌트(OnNetworkSpawn 에서 부른다).
    private void EnsureRadioPresentation()
    {
        if (GetComponent<MapSceneRadioPresentation>() == null)
            gameObject.AddComponent<MapSceneRadioPresentation>();
    }
}
