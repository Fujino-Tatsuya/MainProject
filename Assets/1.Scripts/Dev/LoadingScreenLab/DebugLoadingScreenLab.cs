#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// 할 일 290 로딩 화면 확인 씬(DebugLoadingScreenLab — 빌드 목록에 없다)의 구동기.
/// ▶ 하면 실제 <c>2.LoadingScene</c> 을 덧붙여 열고(이미 열려 있으면 그대로), 네트워크 없이 <see cref="NetworkLoadingScreenView"/> 의
/// 진행률·단계를 조작한다. 기본 = 자동 재생(0→100% 를 반복, 단계도 진행률 따라). 키·버튼은 화면 오른쪽 위 도움말.
/// 씬에서 고친 배치(하프톤 등)는 2.LoadingScene 을 고친 뒤 이 씬에서 ▶ 하면 바로 보인다(Play 중 수정은 사라진다).
/// 씬은 저작 메뉴 <c>Tools/Debug/290 로딩 화면 테스트 씬 만들기</c> 로 다시 만든다.
/// </summary>
[DisallowMultipleComponent]
public sealed class DebugLoadingScreenLab : MonoBehaviour
{
    public const string LoadingScenePath = "Assets/0.Scenes/MainFlow/2.LoadingScene.unity";

    [Tooltip("자동 재생 — 0→100% 에 걸리는 시간(초, unscaled).")]
    [SerializeField] private float autoDuration = 6f;
    [Tooltip("자동 재생 — 100% 에서 머무는 시간(초). 끝나면 0% 부터 다시.")]
    [SerializeField] private float holdAtEnd = 1.5f;

    private static readonly NetworkLoadingPhase[] Phases =
    {
        NetworkLoadingPhase.LoadingScene, NetworkLoadingPhase.LoadingGame, NetworkLoadingPhase.WaitingForPlayers,
        NetworkLoadingPhase.Activating, NetworkLoadingPhase.Ready
    };

    private NetworkLoadingScreenView _view;
    private float _progress;
    private bool _auto = true;
    private bool _autoPhase = true;
    private float _autoTime;
    private NetworkLoadingPhase _phase = NetworkLoadingPhase.LoadingScene;
    private bool _showGui = true;
    private string _status = "2.LoadingScene 여는 중…";

    private IEnumerator Start()
    {
#if UNITY_EDITOR
        if (!SceneManager.GetSceneByPath(LoadingScenePath).isLoaded)
        {
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                LoadingScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
        }
#endif
        float waited = 0f;
        while ((_view = FindFirstObjectByType<NetworkLoadingScreenView>()) == null)
        {
            waited += Time.unscaledDeltaTime;
            if (waited > 5f)
            {
                _status = "⚠ NetworkLoadingScreenView 를 못 찾았다(2.LoadingScene 확인)";
                yield break;
            }
            yield return null;
        }

        _status = "준비됨";
        ApplyProgress(0f);
        ApplyPhase(NetworkLoadingPhase.LoadingScene);
    }

    private void Update()
    {
        if (_view == null) return;

        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.spaceKey.wasPressedThisFrame) _auto = !_auto;
            if (kb.rKey.wasPressedThisFrame) Restart();
            if (kb.hKey.wasPressedThisFrame) _showGui = !_showGui;
            if (kb.leftArrowKey.wasPressedThisFrame) Manual(_progress - 0.05f);
            if (kb.rightArrowKey.wasPressedThisFrame) Manual(_progress + 0.05f);
            if (kb.digit1Key.wasPressedThisFrame) ManualPhase(0);
            if (kb.digit2Key.wasPressedThisFrame) ManualPhase(1);
            if (kb.digit3Key.wasPressedThisFrame) ManualPhase(2);
            if (kb.digit4Key.wasPressedThisFrame) ManualPhase(3);
            if (kb.digit5Key.wasPressedThisFrame) ManualPhase(4);
        }

        if (_auto)
        {
            float duration = Mathf.Max(0.1f, autoDuration);
            _autoTime += Time.unscaledDeltaTime;
            if (_autoTime > duration + Mathf.Max(0f, holdAtEnd)) _autoTime = 0f;
            ApplyProgress(_autoTime / duration);
        }

        if (_autoPhase) ApplyPhase(PhaseFor(_progress));
    }

    /// <summary>자동 단계 — 진행률 구간(실제 흐름 순서). 100% = 전원 준비.</summary>
    private static NetworkLoadingPhase PhaseFor(float progress)
    {
        if (progress >= 1f) return NetworkLoadingPhase.Ready;
        if (progress >= 0.85f) return NetworkLoadingPhase.Activating;
        if (progress >= 0.6f) return NetworkLoadingPhase.WaitingForPlayers;
        if (progress >= 0.3f) return NetworkLoadingPhase.LoadingGame;
        return NetworkLoadingPhase.LoadingScene;
    }

    private void Restart()
    {
        _auto = true;
        _autoPhase = true;
        _autoTime = 0f;
        ApplyProgress(0f);
    }

    private void Manual(float progress)
    {
        _auto = false;
        ApplyProgress(progress);
    }

    private void ManualPhase(int index)
    {
        _autoPhase = false;
        ApplyPhase(Phases[index]);
    }

    private void ApplyProgress(float progress)
    {
        _progress = Mathf.Clamp01(progress);
        _view.SetProgress(_progress);
    }

    private void ApplyPhase(NetworkLoadingPhase phase)
    {
        _phase = phase;
        _view.SetPhase(phase);
    }

    private void OnGUI()
    {
        if (!_showGui) return;

        GUILayout.BeginArea(new Rect(Screen.width - 450f, 10f, 440f, 250f), GUI.skin.box);
        GUILayout.Label("290 로딩 화면 테스트");
        GUILayout.Label("Space = 자동 재생 켜기/끄기 · R = 처음부터(자동) · ←/→ = 진행률 ±5%");
        GUILayout.Label("1~5 = 단계(씬 로딩/게임 로딩/플레이어 대기/시작/전원 준비) · H = 이 창 숨기기");
        if (_view == null)
        {
            GUILayout.Label(_status);
            GUILayout.EndArea();
            return;
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label($"진행률 {Mathf.RoundToInt(_progress * 100f)}%", GUILayout.Width(90f));
        float slider = GUILayout.HorizontalSlider(_progress, 0f, 1f);
        if (!Mathf.Approximately(slider, _progress)) Manual(slider);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button(_auto ? "자동 재생 끄기" : "자동 재생 켜기")) _auto = !_auto;
        if (GUILayout.Button("처음부터")) Restart();
        if (GUILayout.Button(_autoPhase ? "단계 자동 ✓" : "단계 자동")) _autoPhase = !_autoPhase;
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        string[] labels = { "씬", "게임", "대기", "시작", "준비" };
        for (int i = 0; i < Phases.Length; i++)
        {
            if (GUILayout.Button(labels[i])) ManualPhase(i);
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label($"자동 길이 {autoDuration:0.0}초", GUILayout.Width(110f));
        autoDuration = GUILayout.HorizontalSlider(autoDuration, 1f, 20f);
        GUILayout.EndHorizontal();

        GUILayout.Label($"단계 {_phase} · 자동 {(_auto ? "켬" : "끔")} · 단계 자동 {(_autoPhase ? "켬" : "끔")}");
        GUILayout.EndArea();
    }
}
#endif
