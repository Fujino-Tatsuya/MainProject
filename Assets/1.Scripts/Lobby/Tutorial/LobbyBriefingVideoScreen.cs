using Ami.BroAudio;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// [전 피어, 표시 전용] 로비 브리핑 영상 화면(할 일 292) — 단계표 Kind=Video 행 동안 전체 화면으로 영상을 튼다.
/// 바인더(<see cref="LobbyTutorialUiBinder"/>)가 파사드 값으로 <see cref="Show"/>·<see cref="Hide"/> 를 부른다(157 상태를 읽는 곳은 바인더 한 곳).
/// <list type="bullet">
/// <item>오브젝트·캔버스·RenderTexture 는 처음 부를 때 런타임 생성(씬·프리팹 무수정). HUD·튜토리얼 UI(50)·연출 오버레이(450) 위, 포드 연출(500)·게임 메뉴(900) 아래(460).</item>
/// <item>재생 위치 = 서버 시각 − 서버가 실은 시작 시각(늦은 참가자 = 중간부터). 로컬 재생이 1.5초 넘게 벌어지면 다시 맞춘다. 끝은 서버가 정한다(영상 길이 타이머 → 다음 행).</item>
/// <item>소리 = VideoPlayer → AudioSource. 크기 = 마스터 × 음악 볼륨(설정값, 매 프레임), 백그라운드 음소거는 <see cref="AudioListener.volume"/> 이라 그대로 탄다.
/// 영상 동안 로비 BGM 은 연출 무음(<see cref="AudioManager.BeginCinematicBgm"/>)으로 끄고, 끝나면 원래 곡이 다시 들어온다.</item>
/// <item>건너뛰기 = Esc(게임 메뉴 대신 이 화면이 가져감, <see cref="IsEscapeClaimed"/>) → <see cref="LobbyBriefing.RequestSkip"/>. 1인 1표·취소 없음.
/// 오른쪽 아래 키캡 Esc + '건너뛰기 n/m'(타이틀·보스 스킵 위젯과 같은 키캡·글자색).</item>
/// </list>
/// 상세: Docs/tech/lobby-training.md "브리핑 영상".
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyBriefingVideoScreen : MonoBehaviour
{
    private const string Tag = "[LobbyBriefingVideo]";
    public const int CanvasSortingOrder = 460;
    private const float FadeInSeconds = 0.25f;
    private const float FadeOutSeconds = 0.35f;
    private const float ResyncCheckInterval = 1f;
    private const float EndMarginSeconds = 0.1f;   // 이보다 끝에 가까우면 재생하지 않는다(늦게 붙은 피어가 마지막 프레임만 번쩍이지 않게)

    private static LobbyBriefingVideoScreen s_instance;
    private static int s_escapeClaimedFrame = -1;

    /// <summary>이번 프레임 Esc 를 영상 건너뛰기가 가져갔다 — 게임 메뉴(<see cref="GameMenuController"/>)가 열지 않는다.</summary>
    public static bool IsEscapeClaimed => s_escapeClaimedFrame == Time.frameCount;

    /// <summary>영상 화면이 떠 있다(페이드 아웃 중 제외).</summary>
    public static bool IsShowing => s_instance != null && s_instance._active;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_instance = null;
        s_escapeClaimedFrame = -1;
    }

    // 화면
    private Canvas _canvas;
    private CanvasGroup _group;
    private RawImage _image;
    private AspectRatioFitter _fitter;
    private RenderTexture _rt;
    private VideoPlayer _player;
    private AudioSource _audio;
    private GameObject _skipRoot;
    private TMP_Text _skipLabel;
    private RectTransform _keycap;

    // 상태
    private bool _active;
    private int _runId;
    private int _revision = -1;
    private VideoClip _clip;
    private double _startServerTime;
    private bool _prepared;
    private bool _started;
    private bool _voted;
    private bool _bgmHeld;
    private float _resyncTimer;
    private bool _warnedNoClip;

    // ---------------- API(바인더) ----------------

    /// <summary>
    /// 영상 단계 표시 — 매 프레임 불러도 된다. (회차, revision) 이 바뀌면 새로 준비하고, 시작 시각(서버가 실으면 0 → 값)만 바뀌면 그 값으로 위치를 맞춘다.
    /// </summary>
    public static void Show(VideoClip clip, double startServerTime, int runId, int revision)
    {
        if (s_instance == null)
            s_instance = Create();
        s_instance.ShowInternal(clip, startServerTime, runId, revision);
    }

    /// <summary>영상 단계를 떠났다(다음 행·회차 초기화·씬 종료) — 페이드 아웃 뒤 정지·정리. 떠 있지 않으면 아무것도 안 한다.</summary>
    public static void Hide()
    {
        if (s_instance != null)
            s_instance.HideInternal();
    }

    private static LobbyBriefingVideoScreen Create()
    {
        var go = new GameObject("LobbyBriefingVideoScreen");
        var screen = go.AddComponent<LobbyBriefingVideoScreen>();
        screen.Build();
        return screen;
    }

    // ---------------- 수명 ----------------

    private void OnDestroy()
    {
        StopAndRelease();
        ReleaseBgm();
        if (s_instance == this)
            s_instance = null;
    }

    private void Update()
    {
        if (_canvas == null)
            return;

        float dt = Time.unscaledDeltaTime;
        if (!_active)
        {
            // 페이드 아웃 → 끝나면 정지·정리.
            if (_group.alpha > 0f)
            {
                _group.alpha = Mathf.MoveTowards(_group.alpha, 0f, dt / FadeOutSeconds);
                ApplyVolume(_group.alpha);
                if (_group.alpha > 0f)
                    return;
            }

            if (_canvas.gameObject.activeSelf)
            {
                StopAndRelease();
                _canvas.gameObject.SetActive(false);
            }

            return;
        }

        _group.alpha = Mathf.MoveTowards(_group.alpha, 1f, dt / FadeInSeconds);
        ApplyVolume(1f);
        TickPlayback(dt);
        TickSkip();
    }

    // ---------------- 표시 ----------------

    private void ShowInternal(VideoClip clip, double startServerTime, int runId, int revision)
    {
        if (!_active || runId != _runId || revision != _revision || clip != _clip)
            Begin(clip, runId, revision);
        _startServerTime = startServerTime;
    }

    private void Begin(VideoClip clip, int runId, int revision)
    {
        StopAndRelease();
        _active = true;
        _runId = runId;
        _revision = revision;
        _clip = clip;
        _prepared = false;
        _started = false;
        _voted = false;
        _resyncTimer = 0f;

        _canvas.gameObject.SetActive(true);
        HoldBgm();

        if (clip == null)
        {
            if (!_warnedNoClip)
            {
                _warnedNoClip = true;
                Debug.LogWarning($"{Tag} 영상 행에 VideoClip 이 없다 — 검은 화면 + 건너뛰기만 띄운다(길이는 행 dialogueSeconds). " +
                                 "SVN Assets/50.Art/Video/Briefing 을 업데이트했는지 확인.", this);
            }

            _image.texture = null;
            _image.enabled = false;   // 텍스처 없는 RawImage 는 흰 화면 — 검은 바탕만 남긴다
            return;
        }

        int w = Mathf.Max(16, (int)clip.width);
        int h = Mathf.Max(16, (int)clip.height);
        _rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
        {
            name = "LobbyBriefingVideo", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
        };
        _rt.Create();
        ClearBlack(_rt);
        _image.texture = _rt;
        _image.enabled = true;
        _fitter.aspectRatio = (float)w / h;

        _player.clip = clip;
        _player.targetTexture = _rt;
        _player.controlledAudioTrackCount = 1;
        _player.EnableAudioTrack(0, true);
        _player.SetTargetAudioSource(0, _audio);
        _player.Prepare();
        Debug.Log($"[SceneFlow] {Tag} 영상 준비 {clip.name}({clip.length:0.#}초) — 회차 {runId} rev {revision}");
    }

    private void HideInternal()
    {
        if (!_active)
            return;

        _active = false;
        ReleaseBgm();
        if (_skipRoot != null)
            _skipRoot.SetActive(false);
        Debug.Log($"[SceneFlow] {Tag} 영상 단계 끝 — 회차 {_runId} rev {_revision}{(_started ? $", 재생 위치 {_player.time:0.#}초" : ", 재생 전")}");
    }

    private void TickPlayback(float dt)
    {
        if (_clip == null || !_prepared)
            return;

        double length = _clip.length;
        double target = LobbyBriefingVideoRules.PlaybackTime(ServerNow(), _startServerTime, length);
        if (target < 0d)
            return;   // 서버가 아직 시작 시각을 싣지 않았다

        if (!_started)
        {
            if (target >= length - EndMarginSeconds)
                return;   // 거의 끝 — 틀지 않고 다음 행을 기다린다

            _player.time = target;
            _player.Play();
            _started = true;
            Debug.Log($"[SceneFlow] {Tag} 재생 시작 {target:0.##}초부터");
            return;
        }

        _resyncTimer -= dt;
        if (_resyncTimer > 0f || !_player.isPlaying)
            return;

        _resyncTimer = ResyncCheckInterval;
        if (target < length - 0.5d && LobbyBriefingVideoRules.NeedsResync(_player.time, target))
        {
            Debug.Log($"[SceneFlow] {Tag} 재생 위치 다시 맞춤 {_player.time:0.##} → {target:0.##}초");
            _player.time = target;
        }
    }

    private void TickSkip()
    {
        var briefing = LobbyBriefing.Active;
        bool visible = briefing != null;
        if (_skipRoot.activeSelf != visible)
            _skipRoot.SetActive(visible);
        if (!visible)
            return;

        string count = LobbyBriefingVideoRules.CountText(briefing.SkipVoteCount, briefing.SkipVoterCount);
        string label = string.IsNullOrEmpty(count) ? UiStrings.BossIntroSkip.Text : $"{UiStrings.BossIntroSkip.Text} {count}";
        if (_skipLabel.text != label)
        {
            _skipLabel.text = label;
            PlaceKeycap();
        }

        GameMenuController menu = GameMenuController.Instance;
        if (menu != null && menu.IsOpen)
            return;   // 메뉴가 열려 있으면 Esc 는 메뉴 닫기

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame)
            return;

        s_escapeClaimedFrame = Time.frameCount;
        if (_voted)
            return;   // 1인 1표 — 취소 없음

        _voted = true;
        briefing.RequestSkip(_runId, _revision);
        Debug.Log($"[SceneFlow] {Tag} 건너뛰기 동의 보냄 — 회차 {_runId} rev {_revision}");
    }

    // 마스터 × 음악(설정 창 값). 백그라운드 음소거는 AudioListener 가 맡는다.
    private void ApplyVolume(float fade)
    {
        if (_audio == null)
            return;

        AudioManager manager = AudioManager.Instance;
        float master = manager != null ? manager.GetVolume(BroAudioType.All) : SoundConfig.GetVolume(BroAudioType.All);
        float music = manager != null ? manager.GetVolume(BroAudioType.Music) : SoundConfig.GetVolume(BroAudioType.Music);
        _audio.volume = Mathf.Clamp01(master * music * Mathf.Clamp01(fade));
    }

    private void HoldBgm()
    {
        if (_bgmHeld)
            return;

        AudioManager manager = AudioManager.Instance;
        if (manager == null)
            return;

        manager.BeginCinematicBgm(this);
        _bgmHeld = true;
    }

    private void ReleaseBgm()
    {
        if (!_bgmHeld)
            return;

        _bgmHeld = false;
        AudioManager manager = AudioManager.Instance;
        if (manager != null)
            manager.EndCinematicBgm(this);
    }

    private void StopAndRelease()
    {
        if (_player != null)
        {
            _player.Stop();
            _player.clip = null;
            _player.targetTexture = null;
        }

        if (_image != null)
            _image.texture = null;

        if (_rt != null)
        {
            _rt.Release();
            Destroy(_rt);
            _rt = null;
        }

        _prepared = false;
        _started = false;
    }

    private static double ServerNow()
    {
        NetworkManager nm = NetworkManager.Singleton;
        return nm != null && nm.IsListening ? nm.ServerTime.Time : 0d;
    }

    private static void ClearBlack(RenderTexture rt)
    {
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        GL.Clear(true, true, Color.black);
        RenderTexture.active = prev;
    }

    // ---------------- 만들기 ----------------

    private void Build()
    {
        // 캔버스는 자식 — 켜고 끌 때 이 컴포넌트(Update)·VideoPlayer 는 계속 돈다.
        var canvasGo = new GameObject("Canvas", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = CanvasSortingOrder;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        _group = canvasGo.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.blocksRaycasts = false;
        _group.interactable = false;

        // 검은 바탕(영상 비율이 화면과 다를 때 남는 칸) → 영상(비율 유지, 화면 안에 맞춤).
        Image back = CinematicOverlay.NewImage(canvasGo.transform, "Background", null, Color.black);
        CinematicOverlay.Stretch(back.rectTransform);

        var videoGo = new GameObject("Video", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
        videoGo.transform.SetParent(canvasGo.transform, false);
        _image = videoGo.GetComponent<RawImage>();
        _image.raycastTarget = false;
        _image.color = Color.white;
        CinematicOverlay.Stretch(_image.rectTransform);
        _fitter = videoGo.GetComponent<AspectRatioFitter>();
        _fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        _fitter.aspectRatio = 16f / 9f;

        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.spatialBlend = 0f;
        _audio.loop = false;

        _player = gameObject.AddComponent<VideoPlayer>();
        _player.playOnAwake = false;
        _player.isLooping = false;
        _player.waitForFirstFrame = true;
        _player.source = VideoSource.VideoClip;
        _player.renderMode = VideoRenderMode.RenderTexture;
        _player.aspectRatio = VideoAspectRatio.Stretch;   // RT 가 클립 크기 — 비율은 AspectRatioFitter 가 맞춘다
        _player.audioOutputMode = VideoAudioOutputMode.AudioSource;
        // 타이틀 벽 영상(254)에서 켜면 0.5초 근처에서 멈춘 기록이 있어 끈다.
        _player.skipOnDrop = false;
        _player.prepareCompleted += _ => _prepared = true;
        _player.errorReceived += (_, msg) => Debug.LogWarning($"{Tag} 영상 재생 오류: {msg}", this);

        BuildSkip(canvasGo.transform);
        gameObject.SetActive(true);
        _canvas.gameObject.SetActive(false);
    }

    // 키캡 = 글자(오른쪽 정렬) 바로 왼쪽, 간격 16.
    private void PlaceKeycap()
    {
        if (_keycap == null || _skipLabel == null)
            return;
        float width = _skipLabel.GetPreferredValues(_skipLabel.text).x;
        _keycap.anchoredPosition = new Vector2(-(width + 16f * TitleSkipScale), 1f * TitleSkipScale);
    }

    // 타이틀 Esc 건너뛰기(TitleSkipHint, 1.TitleScene 의 SkipHint_Root)와 같은 크기 — 그 루트 높이 58.835 ÷ 기준 높이 72 = 배율.
    // 같은 1920×1080·0.5 캔버스라 캔버스 단위 값이 그대로 같은 화면 크기다. 타이틀 씬 값(키캡 48×40·글자 22)을 바꾸면 여기도 맞춘다.
    private const float TitleSkipScale = 58.835f / CinematicOverlay.SkipPillHeight;
    private const float TitleKeycapWidth = 48f;
    private const float TitleKeycapHeight = 40f;
    private const float TitleKeyFontSize = 22f;
    private const float TitleLabelFontSize = 22f;

    // 오른쪽 아래 [Esc] 건너뛰기 n/m — 타이틀 Esc 건너뛰기(TitleSkipHint)와 같은 키캡·글자(알약 배경 없음). 영상 위라 글자에 검은 외곽선.
    private void BuildSkip(Transform parent)
    {
        _skipRoot = new GameObject("SkipHint", typeof(RectTransform));
        _skipRoot.transform.SetParent(parent, false);
        var root = (RectTransform)_skipRoot.transform;
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(1f, 0f);
        root.anchoredPosition = new Vector2(-56f, 56f);
        root.sizeDelta = new Vector2(420f, CinematicOverlay.SkipPillHeight * TitleSkipScale);

        _skipLabel = CinematicOverlay.Label(root, "Label", TitleLabelFontSize * TitleSkipScale, TextAlignmentOptions.Right);
        CinematicOverlay.Stretch((RectTransform)_skipLabel.transform);
        _skipLabel.color = CinematicOverlay.SkipTextColor;
        _skipLabel.outlineWidth = 0.25f;
        _skipLabel.outlineColor = new Color32(0, 0, 0, 220);
        _skipLabel.text = UiStrings.BossIntroSkip.Text;

        // 키캡은 글자 바로 왼쪽 — 글자 폭이 n/m 에 따라 바뀌어 글자가 바뀔 때마다 PlaceKeycap 이 옮긴다.
        float kw = TitleKeycapWidth * TitleSkipScale;
        float kh = TitleKeycapHeight * TitleSkipScale;
        Image keycap = CinematicOverlay.NewImage(root, "Keycap",
            CinematicOverlay.KeycapSprite(Mathf.RoundToInt(kw), Mathf.RoundToInt(kh)), Color.white);
        RectTransform k = keycap.rectTransform;
        k.anchorMin = k.anchorMax = k.pivot = new Vector2(1f, 0.5f);
        k.sizeDelta = new Vector2(kw, kh);
        _keycap = k;
        PlaceKeycap();
        TMP_Text key = CinematicOverlay.Label(keycap.transform, "Key", TitleKeyFontSize * TitleSkipScale, TextAlignmentOptions.Center);
        CinematicOverlay.Stretch((RectTransform)key.transform);
        ((RectTransform)key.transform).offsetMin = new Vector2(0f, 3f * TitleSkipScale);   // 키캡 아랫단(그림자) 위 가운데
        key.text = "Esc";
        key.color = CinematicOverlay.KeycapTextColor;
        key.fontStyle = FontStyles.Bold;

        _skipRoot.SetActive(false);
    }
}
