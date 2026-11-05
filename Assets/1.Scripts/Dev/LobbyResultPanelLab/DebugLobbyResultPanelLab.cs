#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 할 일 215 로비 결과창 애니메이션 확인 씬(DebugLobbyResultPanelLab — 빌드 목록에 없다)의 구동기.
/// 네트워크 없이 가짜 <see cref="SessionResult"/> 를 채우고 <see cref="LobbyResultPanel"/> 를 열고 닫는다(판을 돌 필요 없음).
/// 키·버튼은 화면 왼쪽 위 도움말 그대로. ESC 는 패널 자체 닫기. 끝나면(OnDestroy) 가짜 결과를 비운다.
/// 씬은 저작 메뉴 <c>Tools/Debug/215 로비 결과창 테스트 씬 만들기</c> 로 다시 만든다(<c>LobbyResultPanelLabSceneBuilder</c>).
/// </summary>
[DisallowMultipleComponent]
public sealed class DebugLobbyResultPanelLab : MonoBehaviour
{
    [SerializeField] private LobbyResultPanel panel;
    [Tooltip("천천히 돌려 패널 뒤 배경이 움직이게 한다(201 배경 블러 확인용).")]
    [SerializeField] private Transform[] spinners;
    [SerializeField] private float spinDegreesPerSecond = 30f;
    [Tooltip("'닫는 도중 다시 열기' — 닫기 시작 뒤 이 시간(초, unscaled)에 다시 연다. 기본 닫기 0.14초의 절반쯤.")]
    [SerializeField] private float reopenDelay = 0.07f;
    [SerializeField] private bool openOnStart = true;
    [SerializeField] private int startPlayerCount = 3;

    private int _playerCount;
    private int _variant;
    private string _lastClose = "-";
    private bool _slowMotion;

    private void Start()
    {
        if (panel == null)
            panel = FindFirstObjectByType<LobbyResultPanel>();
        if (panel != null)
            panel.Closed += OnClosed;

        _playerCount = Mathf.Clamp(startPlayerCount, 1, 3);
        if (openOnStart)
            OpenWithNewResult(_playerCount, 0);
    }

    private void OnDestroy()
    {
        if (panel != null)
            panel.Closed -= OnClosed;
        if (_slowMotion)
            Time.timeScale = 1f;
        SessionResult.Clear(); // 가짜 결과가 다음 Play 로 새지 않게
    }

    private void Update()
    {
        float dt = Time.deltaTime; // 배경은 슬로모션을 따른다(패널 연출은 unscaled 라 따르지 않는다)
        if (spinners != null)
        {
            foreach (Transform spinner in spinners)
            {
                if (spinner != null)
                    spinner.Rotate(0f, spinDegreesPerSecond * dt, 0f, Space.World);
            }
        }

        Keyboard kb = Keyboard.current;
        if (kb == null || panel == null)
            return;

        if (kb.digit1Key.wasPressedThisFrame) OpenWithNewResult(1, _variant);
        if (kb.digit2Key.wasPressedThisFrame) OpenWithNewResult(2, _variant);
        if (kb.digit3Key.wasPressedThisFrame) OpenWithNewResult(3, _variant);
        if (kb.oKey.wasPressedThisFrame) panel.Show();
        if (kb.cKey.wasPressedThisFrame) panel.Hide();
        if (kb.rKey.wasPressedThisFrame) StartCoroutine(CloseThenReopen());
        if (kb.vKey.wasPressedThisFrame) OpenWithNewResult(_playerCount, _variant + 1);
        if (kb.tKey.wasPressedThisFrame) ToggleSlowMotion();
    }

    private void OpenWithNewResult(int playerCount, int variant)
    {
        _playerCount = Mathf.Clamp(playerCount, 1, 3);
        _variant = variant;
        SessionResult.Capture(variant % 3 == 2 ? SessionOutcome.Wiped : SessionOutcome.Cleared,
            754.4f + variant * 61f, BuildPlayers(_playerCount, variant));
        panel.Show(); // 열려 있으면 다시 채우고 카운트업을 처음부터
    }

    private IEnumerator CloseThenReopen()
    {
        if (!panel.IsOpen)
            panel.Show();
        yield return null; // 열린 한 프레임 뒤에 닫기 시작
        panel.Hide();
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, reopenDelay));
        panel.Show();
    }

    private void ToggleSlowMotion()
    {
        _slowMotion = !_slowMotion;
        Time.timeScale = _slowMotion ? 0.1f : 1f;
    }

    private void OnClosed(LobbyResultCloseReason reason) => _lastClose = reason.ToString();

    /// <summary>가짜 행 — 큰 숫자(백만 단위) 포함. variant 마다 값이 바뀐다. 로컬 clientId 가 없어 "나" 강조는 없다.</summary>
    private static List<PlayerSessionStats> BuildPlayers(int count, int variant)
    {
        int[][] table =
        {
            //          Damage,  Kills, Counters, Deaths, Healing, DamageTaken
            new[] { 1234567, 37, 6, 1, 1240, 98410 },
            new[] { 418750, 52, 2, 0, 0, 5310 },
            new[] { 399400, 29, 11, 0, 34600, 4120 },
        };

        var players = new List<PlayerSessionStats>(count);
        for (int i = 0; i < count; i++)
        {
            int[] row = table[(i + variant) % table.Length];
            float scale = 1f + 0.37f * (variant % 4);
            players.Add(new PlayerSessionStats
            {
                ClientId = (ulong)i,
                CharacterId = i,
                Damage = Mathf.RoundToInt(row[0] * scale),
                Kills = row[1] + variant,
                Counters = row[2] + variant % 3,
                Deaths = (row[3] + variant) % 3,
                Healing = Mathf.RoundToInt(row[4] * scale),
                DamageTaken = Mathf.RoundToInt(row[5] * scale)
            });
        }
        return players;
    }

    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10f, 10f, 460f, 300f), GUI.skin.box);
        GUILayout.Label("215 로비 결과창 애니 테스트");
        GUILayout.Label("1/2/3 = 플레이어 수 바꿔 열기 · O = 열기 · C = 닫기 · ESC = 패널 닫기");
        GUILayout.Label("R = 닫는 도중 다시 열기 · V = 값 바꿔 다시 열기(숫자·시간 카운트업·1위 왕관 점프·반짝임 재확인)");
        GUILayout.Label("T = 슬로모션 0.1 토글(패널 연출은 unscaled 라 그대로여야 함)");
        if (panel == null)
        {
            GUILayout.Label("⚠ 씬에 LobbyResultPanel 이 없다");
            GUILayout.EndArea();
            return;
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("열기")) panel.Show();
        if (GUILayout.Button("닫기")) panel.Hide();
        if (GUILayout.Button("닫는 도중 열기")) StartCoroutine(CloseThenReopen());
        if (GUILayout.Button("값 바꿔 열기")) OpenWithNewResult(_playerCount, _variant + 1);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        for (int n = 1; n <= 3; n++)
        {
            if (GUILayout.Button($"{n}명"))
                OpenWithNewResult(n, _variant);
        }
        if (GUILayout.Button(_slowMotion ? "슬로모션 끄기" : "슬로모션 0.1")) ToggleSlowMotion();
        GUILayout.EndHorizontal();

        GUILayout.Label($"열림 {panel.IsOpen} · 플레이어 {_playerCount}명 · 값 세트 {_variant} · 마지막 닫힘 {_lastClose} · timeScale {Time.timeScale:0.##}");
        GUILayout.EndArea();
    }

#if UNITY_EDITOR
    public void Setup(LobbyResultPanel target, Transform[] spinTargets)
    {
        panel = target;
        spinners = spinTargets;
    }
#endif
}
#endif
