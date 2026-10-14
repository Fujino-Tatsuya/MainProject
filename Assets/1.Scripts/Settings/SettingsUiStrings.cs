using System.Collections.Generic;

/// <summary>
/// 설정창 문구 키(PLAN-settings S15 → 로컬라이징 일원화, 할 일 38). 키 = <c>UI.Settings.*</c>, ko = <see cref="UiString.Ko"/>(폴백),
/// 원본 = GameData.xlsx Strings 시트 — <see cref="UiStrings.All"/> 에 들어가 Export 가 행을 붙인다. 읽기는 <see cref="UiString.Text"/>·<see cref="UiString.Format"/>.
/// <para>
/// <see cref="AuthoredEnglish"/> 는 <b>프리팹 저작 영어 원문</b>이다 — 항목 배치 메뉴(<c>SettingsPanelAuthoring</c>)가 라벨을 영어로 만들고,
/// <c>SettingsLocalizationLinker</c> 가 그 글자로 키를 찾는다. 화면의 en 은 시트 en 칸이 원본이다(여기를 고쳐도 화면 en 은 안 바뀐다).
/// </para>
/// 언어 이름(한국어 / English)은 번역하지 않는다 — <see cref="LocText.NativeName"/>.
/// </summary>
public static class SettingsUiStrings
{
    public const string KeyPrefix = "UI.Settings.";
    public const string ControlsActionPrefix = KeyPrefix + "Controls.";

    // 🔴 필드보다 먼저 선언 — 정적 초기화는 글자 순서라 S() 가 이 사전을 쓴다.
    private static readonly List<UiString> all = new List<UiString>();
    private static readonly Dictionary<string, string> english = new Dictionary<string, string>();

    private const string Body = "SettingsBody.prefab › ";

    // 공용
    public static readonly UiString CommonOff = S("Common.Off", "끄기", "Off", "설정창 — 드롭다운 '끄기'(데미지 숫자·성능 표시·그림자·AA)(코드)");

    // 탭
    public static readonly UiString TabGameplay = S("Tab.Gameplay", "게임플레이", "Gameplay", Body + "Gameplay 탭");
    public static readonly UiString TabGraphics = S("Tab.Graphics", "그래픽", "Graphics", Body + "Graphics 탭");
    public static readonly UiString TabControls = S("Tab.Controls", "조작", "Controls", Body + "Controls 탭");
    public static readonly UiString TabAudio = S("Tab.Audio", "오디오", "Audio", Body + "Audio 탭");

    // 게임플레이
    public static readonly UiString GameplayDamageNumbers = S("Gameplay.DamageNumbers", "데미지 숫자", "Damage Numbers", Body + "DamageNumbers 라벨");
    public static readonly UiString GameplayDamageNumbersAll = S("Gameplay.DamageNumbers.All", "전체", "All", "설정창 — 데미지 숫자 드롭다운(코드)");
    public static readonly UiString GameplayDamageNumbersMine = S("Gameplay.DamageNumbers.Mine", "내 것만", "Mine Only", "설정창 — 데미지 숫자 드롭다운(코드)");
    public static readonly UiString GameplayDamageNumbersAllEmphasized = S("Gameplay.DamageNumbers.AllEmphasized", "전체 (내 것 강조)", "All (Mine Emphasized)", "설정창 — 데미지 숫자 드롭다운(코드)");
    public static readonly UiString GameplayScreenShake = S("Gameplay.ScreenShake", "화면 흔들림", "Screen Shake", Body + "ScreenShake 라벨");
    public static readonly UiString GameplayLanguage = S("Gameplay.Language", "언어 (Language)", "Language", Body + "Language 라벨(버튼 글자 = 언어 이름, 번역 안 함)");
    public static readonly UiString GameplayPerformance = S("Gameplay.Performance", "성능 표시", "Performance Info", Body + "Performance 라벨");
    public static readonly UiString GameplayPerformanceFps = S("Gameplay.Performance.Fps", "FPS", "FPS", "설정창 — 성능 표시 드롭다운(코드)");
    public static readonly UiString GameplayPerformanceFpsPing = S("Gameplay.Performance.FpsPing", "FPS + 핑", "FPS + Ping", "설정창 — 성능 표시 드롭다운(코드)");

    // 그래픽 — 화면
    public static readonly UiString GraphicsResolution = S("Graphics.Resolution", "해상도", "Resolution", Body + "Resolution 라벨");
    public static readonly UiString GraphicsResolution1440 = S("Graphics.Resolution.Recommended1440", "2560 x 1440 (권장)", "2560 x 1440 (Recommended)", "설정창 — 해상도 드롭다운(코드)");
    public static readonly UiString GraphicsResolution1080 = S("Graphics.Resolution.Recommended1080", "1920 x 1080 (권장)", "1920 x 1080 (Recommended)", "설정창 — 해상도 드롭다운(코드)");
    public static readonly UiString GraphicsResolutionNative = S("Graphics.Resolution.MonitorNative", "모니터 기본 ({width} x {height})", "Monitor Default ({width} x {height})", "설정창 — 해상도 드롭다운({width}·{height} = 모니터 픽셀)(코드)");
    public static readonly UiString GraphicsWindowMode = S("Graphics.WindowMode", "창 모드", "Window Mode", Body + "WindowMode 라벨");
    public static readonly UiString GraphicsWindowModeFullscreen = S("Graphics.WindowMode.Fullscreen", "전체 화면", "Fullscreen", "설정창 — 창 모드 드롭다운(코드)");
    public static readonly UiString GraphicsWindowModeBorderless = S("Graphics.WindowMode.Borderless", "테두리 없는 창", "Borderless Window", "설정창 — 창 모드 드롭다운(코드)");
    public static readonly UiString GraphicsWindowModeWindowed = S("Graphics.WindowMode.Windowed", "창", "Windowed", "설정창 — 창 모드 드롭다운(코드)");
    public static readonly UiString GraphicsApply = S("Graphics.Apply", "적용", "Apply", Body + "ApplyDisplay 버튼");
    public static readonly UiString GraphicsFrameLimit = S("Graphics.FrameLimit", "프레임 제한", "Frame Limit", Body + "FrameLimit 라벨");
    public static readonly UiString GraphicsFrameLimitUnlimited = S("Graphics.FrameLimit.Unlimited", "무제한", "Unlimited", "설정창 — 프레임 제한 드롭다운(코드)");
    public static readonly UiString GraphicsVSync = S("Graphics.VSync", "수직 동기화 (VSync)", "VSync", Body + "VSync 라벨");
    public static readonly UiString GraphicsConfirm = S("Graphics.Confirm", "이 화면 설정을 유지할까요?\n{time} 뒤 되돌립니다", "Keep these display settings?\nReverting in {time}", "설정창 — 화면 적용 확인 창({time} = UI.Unit.Seconds 로 만든 남은 초)(코드)");
    public static readonly UiString GraphicsKeep = S("Graphics.Keep", "유지", "Keep", Body + "ConfirmDialog › Keep 버튼");
    public static readonly UiString GraphicsRevert = S("Graphics.Revert", "되돌리기", "Revert", Body + "ConfirmDialog › Revert 버튼");

    // 그래픽 — 품질
    public static readonly UiString GraphicsPreset = S("Graphics.Preset", "품질 프리셋", "Quality Preset", Body + "QualityPreset 라벨");
    public static readonly UiString GraphicsPresetLow = S("Graphics.Preset.Low", "낮음", "Low", "설정창 — 품질 프리셋·그림자 드롭다운(코드)");
    public static readonly UiString GraphicsPresetMedium = S("Graphics.Preset.Medium", "중간", "Medium", "설정창 — 품질 프리셋·그림자 드롭다운(코드)");
    public static readonly UiString GraphicsPresetHigh = S("Graphics.Preset.High", "높음", "High", "설정창 — 품질 프리셋·그림자 드롭다운(코드)");
    public static readonly UiString GraphicsPresetCustom = S("Graphics.Preset.Custom", "사용자 지정", "Custom", "설정창 — 품질 프리셋 드롭다운(코드)");
    public static readonly UiString GraphicsRenderScale = S("Graphics.RenderScale", "렌더 스케일", "Render Scale", Body + "RenderScale 라벨");
    public static readonly UiString GraphicsShadows = S("Graphics.Shadows", "그림자", "Shadows", Body + "Shadows 라벨");
    public static readonly UiString GraphicsAntiAliasing = S("Graphics.AntiAliasing", "안티앨리어싱", "Anti-Aliasing", Body + "AntiAliasing 라벨");
    // 10-13 팀장: "후처리"는 플레이어가 모른다 → 실제로 끄는 두 효과의 게임 표준 이름으로(블룸 = 빛 번짐, 색수차 = 가장자리 색 갈라짐).
    public static readonly UiString GraphicsPostEffects = S("Graphics.PostEffects", "블룸·색수차", "Bloom & Chromatic Aberration", Body + "PostEffects 라벨");
    public static readonly UiString GraphicsVignetteSize = S("Graphics.VignetteSize", "비네트 크기", "Vignette Size", Body + "VignetteSize 라벨");
    public static readonly UiString GraphicsVignetteEdge = S("Graphics.VignetteEdge", "비네트 경계", "Vignette Edge", Body + "VignetteEdge 라벨");

    // 조작 — Controls.<입력 액션 이름> 은 KeyRebindRow 가 맞바꿈 알림에도 쓴다(ActionLabel)
    public static readonly UiString ControlsHoldSkillToggle = S("Controls.HoldSkillToggle", "홀드 스킬: 토글 방식", "Hold Skill: Toggle", Body + "HoldSkillToggle 라벨");
    public static readonly UiString ControlsDashMouse = S("Controls.DashMouse", "대시: 마우스 방향", "Dash: Mouse Direction", Body + "DashDirectionToggle 라벨");
    public static readonly UiString ControlsAttack = S("Controls.Attack", "기본 공격", "Attack", Body + "Rebind_Attack 라벨 · 맞바꿈 알림");
    public static readonly UiString ControlsInterrupt = S("Controls.Interrupt", "간파", "Interrupt", Body + "Rebind_Interrupt 라벨 · 맞바꿈 알림");
    public static readonly UiString ControlsSkillMain = S("Controls.SkillMain", "스킬 1 (메인)", "Skill 1 (Main)", Body + "Rebind_SkillMain 라벨 · 맞바꿈 알림");
    public static readonly UiString ControlsSkillSub = S("Controls.SkillSub", "스킬 2 (서브)", "Skill 2 (Sub)", Body + "Rebind_SkillSub 라벨 · 맞바꿈 알림");
    public static readonly UiString ControlsSkillUltimate = S("Controls.SkillUltimate", "궁극기", "Ultimate", Body + "Rebind_SkillUltimate 라벨 · 맞바꿈 알림");
    public static readonly UiString ControlsDash = S("Controls.Dash", "대시", "Dash", Body + "Rebind_Dash 라벨 · 맞바꿈 알림");
    public static readonly UiString ControlsInteract = S("Controls.Interact", "상호작용", "Interact", Body + "Rebind_Interact 라벨 · 맞바꿈 알림");
    public static readonly UiString ControlsResetKeys = S("Controls.ResetKeys", "키 기본값으로 초기화", "Reset Keys to Default", Body + "ResetKeys 버튼");
    public static readonly UiString ControlsPressKey = S("Controls.PressKey", "키를 누르세요... (Esc: 취소)", "Press a key... (Esc: cancel)", "설정창 — 키 변경 대기(코드)");
    public static readonly UiString ControlsSwapped = S("Controls.Swapped", "{action}와(과) 키를 맞바꿨습니다", "Swapped with {action}", "설정창 — 키 맞바꿈 알림({action} = 상대 액션 이름, UI.Settings.Controls.<액션>)(코드)");
    public static readonly UiString ControlsResetDone = S("Controls.ResetDone", "키를 기본값으로 되돌렸습니다", "Keys reset to default", "설정창 — 키 초기화 알림(코드)");

    // 오디오
    public static readonly UiString AudioMaster = S("Audio.Master", "전체", "Master", Body + "MasterVolume 라벨");
    public static readonly UiString AudioBgm = S("Audio.Bgm", "배경음", "BGM", Body + "BGM 라벨");
    public static readonly UiString AudioSfx = S("Audio.Sfx", "효과음", "SFX", Body + "SFX 라벨");
    public static readonly UiString AudioMuteUnfocused = S("Audio.MuteUnfocused", "창 비활성 시 음소거", "Mute When Unfocused", Body + "MuteInBackground 라벨");

    /// <summary>설정창 키 전부(선언 순서). <see cref="UiStrings.All"/> 이 그대로 모은다.</summary>
    public static IReadOnlyList<UiString> All => all;

    /// <summary>키 → 프리팹 저작 영어 원문(Gyeongseok English). 시트 en 칸을 처음 채울 때·라벨 역조회에 쓴다.</summary>
    public static IReadOnlyDictionary<string, string> AuthoredEnglish => english;

    /// <summary>입력 액션 이름(<c>SkillMain</c>)의 표시 이름 키. 없으면 false.</summary>
    public static bool TryGetAction(string actionName, out UiString text)
    {
        string key = ControlsActionPrefix + actionName;
        foreach (UiString s in all)
        {
            if (s.Key == key)
            {
                text = s;
                return true;
            }
        }

        text = default;
        return false;
    }

    /// <summary>
    /// 라벨 글자 → 키(없으면 null). 영어 원문이 먼저, 그다음 ko — ko 는 한 키에만 있는 글자만(<c>전체</c> 처럼 겹치면 못 찾는다).
    /// 저작 메뉴가 프리팹 라벨에 LocalizedText 를 붙일 때 쓴다.
    /// </summary>
    public static string FindKeyByLabel(string label)
    {
        if (string.IsNullOrEmpty(label))
            return null;

        foreach (KeyValuePair<string, string> pair in english)
        {
            if (pair.Value == label)
                return pair.Key;
        }

        string found = null;
        foreach (UiString s in all)
        {
            if (s.Ko != label)
                continue;
            if (found != null)
                return null;
            found = s.Key;
        }

        return found;
    }

    private static UiString S(string name, string ko, string en, string where)
    {
        var text = new UiString(KeyPrefix + name, ko, where);
        all.Add(text);
        english[text.Key] = en;
        return text;
    }
}
