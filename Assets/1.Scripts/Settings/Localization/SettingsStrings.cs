using System.Collections.Generic;

/// <summary>
/// 설정창 문구 표(PLAN-settings S15) — 키 → English / 한국어.
/// 🔴 표는 <b>키·영·한 3열만</b> 둔다. 나중에 버튼 하나로 SO/xlsx 로 옮길 것이 확정이라(팀장 10-13),
///    그때 <see cref="All"/> 을 그대로 내보내면 되게 하려는 것이다. 문맥·주석 열을 늘리지 말 것.
/// - <c>{0}</c> 자리표시자는 <see cref="Format"/> 으로 채운다.
/// - 프리팹 고정 라벨은 저작 메뉴가 <b>영어 원문</b>으로 키를 찾는다(<see cref="FindKeyByEnglish"/>) —
///   영어 문구를 바꾸면 프리팹 쪽 라벨 문구도 같이 바뀌어야 다시 찾힌다(저작 메뉴를 다시 돌리면 맞춰진다).
/// </summary>
public static class SettingsStrings
{
    public readonly struct Entry
    {
        public readonly string Key;
        public readonly string English;
        public readonly string Korean;

        public Entry(string key, string english, string korean)
        {
            Key = key;
            English = english;
            Korean = korean;
        }

        public string For(GameLanguage language) => language == GameLanguage.Korean ? Korean : English;
    }

    static readonly Entry[] Entries =
    {
        // 공용
        new Entry("settings.common.off", "Off", "끄기"),

        // 탭
        new Entry("settings.tab.gameplay", "Gameplay", "게임플레이"),
        new Entry("settings.tab.graphics", "Graphics", "그래픽"),
        new Entry("settings.tab.controls", "Controls", "조작"),
        new Entry("settings.tab.audio", "Audio", "오디오"),

        // 게임플레이
        new Entry("settings.gameplay.damageNumbers", "Damage Numbers", "데미지 숫자"),
        new Entry("settings.gameplay.damageNumbers.all", "All", "전체"),
        new Entry("settings.gameplay.damageNumbers.mine", "Mine Only", "내 것만"),
        new Entry("settings.gameplay.damageNumbers.allEmphasized", "All (Mine Emphasized)", "전체 (내 것 강조)"),
        new Entry("settings.gameplay.screenShake", "Screen Shake", "화면 흔들림"),
        new Entry("settings.gameplay.language", "Language", "언어 (Language)"),
        new Entry("settings.gameplay.performance", "Performance Info", "성능 표시"),
        new Entry("settings.gameplay.performance.fps", "FPS", "FPS"),
        new Entry("settings.gameplay.performance.fpsPing", "FPS + Ping", "FPS + 핑"),

        // 그래픽 — 화면
        new Entry("settings.graphics.resolution", "Resolution", "해상도"),
        new Entry("settings.graphics.resolution.1440", "2560 x 1440 (Recommended)", "2560 x 1440 (권장)"),
        new Entry("settings.graphics.resolution.1080", "1920 x 1080 (Recommended)", "1920 x 1080 (권장)"),
        new Entry("settings.graphics.resolution.native", "Monitor Default ({0} x {1})", "모니터 기본 ({0} x {1})"),
        new Entry("settings.graphics.windowMode", "Window Mode", "창 모드"),
        new Entry("settings.graphics.windowMode.fullscreen", "Fullscreen", "전체 화면"),
        new Entry("settings.graphics.windowMode.borderless", "Borderless Window", "테두리 없는 창"),
        new Entry("settings.graphics.windowMode.windowed", "Windowed", "창"),
        new Entry("settings.graphics.apply", "Apply", "적용"),
        new Entry("settings.graphics.frameLimit", "Frame Limit", "프레임 제한"),
        new Entry("settings.graphics.frameLimit.unlimited", "Unlimited", "무제한"),
        new Entry("settings.graphics.vsync", "VSync", "수직 동기화 (VSync)"),
        new Entry("settings.graphics.confirm", "Keep these display settings?\nReverting in {0}s", "이 화면 설정을 유지할까요?\n{0}초 뒤 되돌립니다"),
        new Entry("settings.graphics.keep", "Keep", "유지"),
        new Entry("settings.graphics.revert", "Revert", "되돌리기"),

        // 그래픽 — 품질
        new Entry("settings.graphics.preset", "Quality Preset", "품질 프리셋"),
        new Entry("settings.graphics.preset.low", "Low", "낮음"),
        new Entry("settings.graphics.preset.medium", "Medium", "중간"),
        new Entry("settings.graphics.preset.high", "High", "높음"),
        new Entry("settings.graphics.preset.custom", "Custom", "사용자 지정"),
        new Entry("settings.graphics.renderScale", "Render Scale", "렌더 스케일"),
        new Entry("settings.graphics.shadows", "Shadows", "그림자"),
        new Entry("settings.graphics.antiAliasing", "Anti-Aliasing", "안티앨리어싱"),
        // 10-13 팀장: "후처리"는 플레이어가 모른다 → 실제로 끄는 두 효과의 게임 표준 이름으로(블룸 = 빛 번짐, 색수차 = 가장자리 색 갈라짐).
        new Entry("settings.graphics.postEffects", "Bloom & Chromatic Aberration", "블룸·색수차"),
        new Entry("settings.graphics.vignetteSize", "Vignette Size", "비네트 크기"),
        new Entry("settings.graphics.vignetteEdge", "Vignette Edge", "비네트 경계"),

        // 조작 — 키 이름 뒤는 입력 액션 이름(KeyRebindRow 가 맞바꿈 알림에 쓴다)
        new Entry("settings.controls.holdSkillToggle", "Hold Skill: Toggle", "홀드 스킬: 토글 방식"),
        new Entry("settings.controls.dashMouse", "Dash: Mouse Direction", "대시: 마우스 방향"),
        new Entry("settings.controls.Attack", "Attack", "기본 공격"),
        new Entry("settings.controls.Interrupt", "Interrupt", "간파"),
        new Entry("settings.controls.SkillMain", "Skill 1 (Main)", "스킬 1 (메인)"),
        new Entry("settings.controls.SkillSub", "Skill 2 (Sub)", "스킬 2 (서브)"),
        new Entry("settings.controls.SkillUltimate", "Ultimate", "궁극기"),
        new Entry("settings.controls.Dash", "Dash", "대시"),
        new Entry("settings.controls.Interact", "Interact", "상호작용"),
        new Entry("settings.controls.resetKeys", "Reset Keys to Default", "키 기본값으로 초기화"),
        new Entry("settings.controls.pressKey", "Press a key... (Esc: cancel)", "키를 누르세요... (Esc: 취소)"),
        new Entry("settings.controls.swapped", "Swapped with {0}", "{0}와(과) 키를 맞바꿨습니다"),
        new Entry("settings.controls.resetDone", "Keys reset to default", "키를 기본값으로 되돌렸습니다"),

        // 오디오
        new Entry("settings.audio.master", "Master", "전체"),
        new Entry("settings.audio.bgm", "BGM", "배경음"),
        new Entry("settings.audio.sfx", "SFX", "효과음"),
        new Entry("settings.audio.muteUnfocused", "Mute When Unfocused", "창 비활성 시 음소거"),
    };

    static Dictionary<string, Entry> _byKey;

    /// <summary>표 전체(키·영·한). SO/xlsx 로 옮기는 버튼이 그대로 내보낼 원본.</summary>
    public static IReadOnlyList<Entry> All => Entries;

    public static string Get(string key) => Get(key, LanguageConfig.Current);

    /// <summary>키가 없으면 키 자체를 돌려준다 — 화면에 키가 보이면 표에 빠진 것이다.</summary>
    public static string Get(string key, GameLanguage language)
    {
        if (_byKey == null)
        {
            _byKey = new Dictionary<string, Entry>(Entries.Length);
            foreach (Entry e in Entries) _byKey[e.Key] = e;
        }
        return _byKey.TryGetValue(key, out Entry entry) ? entry.For(language) : key;
    }

    public static string Format(string key, params object[] args) => string.Format(Get(key), args);

    /// <summary>영어 원문 → 키(없으면 null). 저작 메뉴가 프리팹 라벨에 <see cref="LocalizedText"/> 를 붙일 때 쓴다.</summary>
    public static string FindKeyByEnglish(string english)
    {
        if (string.IsNullOrEmpty(english)) return null;
        foreach (Entry e in Entries)
            if (e.English == english) return e.Key;
        return null;
    }
}
