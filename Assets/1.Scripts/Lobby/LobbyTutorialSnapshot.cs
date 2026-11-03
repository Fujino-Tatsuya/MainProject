using System;
using Unity.Netcode;

/// <summary>
/// 로비 튜토리얼 복제 스냅샷(할 일 157 PLAN §2.4) — 단계 전이와 시각을 한 덩어리로 써서 클라가 "새 단계 + 옛 시각" 조합을 보지 않게 한다.
/// 문구 키·집결 지점·경로·대사 ID 는 <see cref="StepIndex"/> 로 단계표 SO 에서 읽는다(같은 빌드 전제).
/// <see cref="RunId"/> 0 = 아직 시작 전(진행기 없음).
/// </summary>
public struct LobbyTutorialSnapshot : INetworkSerializable, IEquatable<LobbyTutorialSnapshot>
{
    /// <summary>회차 — 진행기 스폰(로비 로드)마다 +1. 이전 회차 콜백을 거른다.</summary>
    public int RunId;
    /// <summary>전이마다 +1.</summary>
    public int Revision;
    /// <summary>단계표 행 번호.</summary>
    public byte StepIndex;
    /// <summary>이 단계에 들어온 서버 시각(NetworkManager.ServerTime.Time).</summary>
    public double EnteredAtServerTime;
    /// <summary>대사 단계의 시작·끝 서버 시각. 대사 아니면 0.</summary>
    public double TimerStartServerTime;
    public double TimerEndServerTime;
    /// <summary>출격 확정을 내보냈다(회차마다 1회).</summary>
    public bool LaunchCommitted;

    public bool IsStarted => RunId != 0;

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref RunId);
        s.SerializeValue(ref Revision);
        s.SerializeValue(ref StepIndex);
        s.SerializeValue(ref EnteredAtServerTime);
        s.SerializeValue(ref TimerStartServerTime);
        s.SerializeValue(ref TimerEndServerTime);
        s.SerializeValue(ref LaunchCommitted);
    }

    public bool Equals(LobbyTutorialSnapshot o) =>
        RunId == o.RunId && Revision == o.Revision && StepIndex == o.StepIndex
        && EnteredAtServerTime.Equals(o.EnteredAtServerTime)
        && TimerStartServerTime.Equals(o.TimerStartServerTime)
        && TimerEndServerTime.Equals(o.TimerEndServerTime)
        && LaunchCommitted == o.LaunchCommitted;

    public override bool Equals(object obj) => obj is LobbyTutorialSnapshot o && Equals(o);

    public override int GetHashCode() => (RunId * 397) ^ (Revision * 31) ^ StepIndex;

    public override string ToString() =>
        $"run={RunId} rev={Revision} step={StepIndex} at={EnteredAtServerTime:F2} timer={TimerStartServerTime:F2}~{TimerEndServerTime:F2} launch={LaunchCommitted}";
}
