using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 우측 상단 성능 표시(PLAN-settings S17) — 설정 <see cref="GameplayConfig.Performance"/> 가 켜져 있을 때만 보인다.
/// 전투 HUD 와 분리된 런타임 오버레이다 — 씬·프리팹에 두지 않고 첫 씬 로드 뒤 스스로 만들어 씬을 넘어 산다.
/// - FPS: 0.5초 평균(시간 배율 무관).
/// - 핑: 클라이언트 = 트랜스포트 RTT(ms) · 호스트 = <c>HOST</c> · 세션 밖 = 표시 안 함.
/// 문구는 숫자·단위뿐이라 번역하지 않는다.
/// </summary>
public sealed class PerformanceOverlay : MonoBehaviour
{
    const float SampleSeconds = 0.5f;
    const float Margin = 8f;
    const float FontSize = 18f;

    TMP_Text _text;
    GameObject _canvasGo;
    float _elapsed;
    int _frames;
    float _fps;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<PerformanceOverlay>() != null) return;
        var go = new GameObject(nameof(PerformanceOverlay));
        DontDestroyOnLoad(go);
        go.AddComponent<PerformanceOverlay>();
    }

    void Awake()
    {
        _canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
        _canvasGo.transform.SetParent(transform, false);
        var canvas = _canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;   // 모든 UI 위 — 입력은 받지 않는다(레이캐스터 없음)

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        var rt = (RectTransform)textGo.transform;
        rt.SetParent(_canvasGo.transform, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.one;   // 우측 상단
        rt.anchoredPosition = new Vector2(-Margin, -Margin);
        rt.sizeDelta = new Vector2(300f, 30f);

        var tmp = textGo.GetComponent<TextMeshProUGUI>();
        tmp.fontSize = FontSize;
        tmp.alignment = TextAlignmentOptions.TopRight;
        tmp.raycastTarget = false;
        tmp.color = new Color(1f, 1f, 1f, 0.85f);
        tmp.outlineWidth = 0.2f;
        tmp.outlineColor = new Color32(0, 0, 0, 200);
        _text = tmp;
    }

    void Update()
    {
        _frames++;
        _elapsed += Time.unscaledDeltaTime;
        if (_elapsed < SampleSeconds) return;

        _fps = _frames / _elapsed;
        _frames = 0;
        _elapsed = 0f;
        Refresh();
    }

    void Refresh()
    {
        PerformanceDisplay mode = GameplayConfig.Performance;
        bool show = mode != PerformanceDisplay.Off;
        if (_canvasGo.activeSelf != show) _canvasGo.SetActive(show);
        if (!show) return;

        string line = $"{Mathf.RoundToInt(_fps)} FPS";
        if (mode == PerformanceDisplay.FpsAndPing)
        {
            string ping = PingText();
            if (ping != null) line += "   " + ping;
        }
        _text.text = line;
    }

    static string PingText()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) return null;
        if (nm.IsServer) return "HOST";
        if (!nm.IsConnectedClient || nm.NetworkConfig.NetworkTransport == null) return null;
        ulong rtt = nm.NetworkConfig.NetworkTransport.GetCurrentRtt(NetworkManager.ServerClientId);
        return $"{rtt} ms";
    }
}
