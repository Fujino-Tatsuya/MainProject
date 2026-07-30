using Unity.Netcode;

/// <summary>
/// 네트워크 이상 감지. 1단계는 호스트 단독 실행이라 원격 비교 대상이 없어 최소 검사만 한다:
/// - 세션이 리슨 중이었다가 예기치 않게 종료되면 Critical.
/// - 클라이언트 비정상 접속 해제(DisconnectReason 존재) 보고.
/// 호스트-클라 상태/위치 desync 실측은 2단계(MPPM 다중 인스턴스)에서 활성화한다.
/// NGO 내부 오류(RPC 실패 등)는 Debug.LogError로 나오므로 LogErrorDetector가 함께 잡는다.
/// </summary>
public sealed class NetworkDesyncDetector : IQADetector
{
    public string Name => "Network";

    private bool _wasListening;
    private bool _subscribed;

    public void OnSessionStart(QARecorder recorder)
    {
        _wasListening = false;
        recorder.Add(QASeverity.Info, Name,
            "네트워크 감지기 시작(호스트 단독 모드 — 전체 desync 실측은 2단계 MPPM에서 활성)");
    }

    public void Tick(QARecorder recorder, float deltaTime)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null)
            return;

        if (!_subscribed && nm.IsListening)
        {
            nm.OnClientDisconnectCallback += OnClientDisconnect;
            _subscribed = true;
        }

        if (nm.IsListening)
        {
            _wasListening = true;
        }
        else if (_wasListening)
        {
            // 리슨 중이었는데 꺼짐 — 세션이 죽음.
            _wasListening = false;
            recorder.Add(QASeverity.Critical, Name, "네트워크 세션이 예기치 않게 종료됨(리슨 중단)");
        }

        _recorder = recorder;
    }

    public void OnSessionEnd(QARecorder recorder)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm != null && _subscribed)
            nm.OnClientDisconnectCallback -= OnClientDisconnect;
        _subscribed = false;
        _recorder = null;
    }

    private QARecorder _recorder;

    private void OnClientDisconnect(ulong clientId)
    {
        if (_recorder == null)
            return;

        NetworkManager nm = NetworkManager.Singleton;
        string reason = nm != null ? nm.DisconnectReason : null;
        if (!string.IsNullOrEmpty(reason))
        {
            _recorder.Add(QASeverity.Error, Name,
                $"클라이언트 {clientId} 비정상 접속 해제: {reason}");
        }
    }
}
