using UnityEngine;

/// <summary>
/// 간파 연출 튜닝 패널(DebugInterruptLab 씬에만 둔다, 할 일 132 §17) — 화면 OnGUI 하나로
/// 연출 효과마다 켜기/끄기 토글 + 디버그 배속(<see cref="DebugTimeScale"/> 재사용)을 만진다.
///
/// <list type="bullet">
/// <item>토글은 이 피어의 디렉터(<see cref="InterruptProductionDirector.Instance"/>) 런타임 값만 바꾼다(<see cref="InterruptProductionDirector.SetEffect"/>·
///       <see cref="InterruptProductionDirector.Set"/>). 프리팹·씬에 저장하지 않는다 — Play 를 끝내면 원래 값.</item>
/// <item>배속은 같은 오브젝트의 <see cref="DebugTimeScale"/> 슬라이더 값을 쓴다 — 0.05~2, 성공 후 1배속 복귀 규칙도 그 컴포넌트 그대로.</item>
/// <item>꺼진 효과는 그리기만 안 한다 — 상태 기계·시간표·섬광 순간(암전 걷힘 등)은 그대로 돈다.</item>
/// <item>허수아비 간파를 제압 간파로 재현하는 토글(179 후속 3, <see cref="DebugSuppressInterruptRepro"/>). A6·A7 섬광 시퀀스 칸은 11-04(할 일 211) 런타임에서 빠져 버튼도 뺐다.</item>
/// <item>간파 연출 4단계(할 일 192) — 지금 단계 표시, 실패/근접/대쉬/제압 재현 버튼(<see cref="DebugInterruptTierPreview"/>).</item>
/// <item>카메라 대상 중심 토글(할 일 196).</item>
/// <item>11-06(할 일 238) 정리 — 버전 V0~V2 버튼·눌러도 안 보이는 효과 칸(오너 암전·②~⑥·월드 섬광·적중 지점 라이트·A2·A4)·'전부 켬'·'암전만' 삭제. 남은 버튼은 모두 V3 런타임에 연결.</item>
/// <item>간파 스킬 캐릭터 VFX 개별 켜고 끄기(할 일 238) — <see cref="InterruptVfxToggles"/>(정적·저장 안 함), 디렉터 없어도 보인다. 목록 Docs/tech/interrupt-vfx-list.md.</item>
/// <item>배치(11-05 정리) — 성격별 접는 섹션(<see cref="Section"/>: 간파 재생 / 단계·버전 / 화이트아웃·제압 / 화면 효과 / 카메라 / 허수아비·서버 / 배속),
///       같은 폭 버튼 줄, 넘치면 스크롤. 접힘 상태만 PlayerPrefs(<see cref="FoldPrefsPrefix"/>)에 기억 — 칸 값은 저장하지 않는다.</item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
public sealed class DebugInterruptTuningPanel : MonoBehaviour
{
    /// <summary>패널 한 줄 — 효과 칸 또는 기존 스위치 칸(팀원 암순응 = A2).</summary>
    public readonly struct Row
    {
        public readonly string Label;
        public readonly bool IsCue;
        public readonly InterruptPresentationEffect Effect;
        public readonly InterruptProductionCue Cue;

        public Row(string label, InterruptPresentationEffect effect)
        {
            Label = label;
            IsCue = false;
            Effect = effect;
            Cue = default;
        }

        public Row(string label, InterruptProductionCue cue)
        {
            Label = label;
            IsCue = true;
            Effect = default;
            Cue = cue;
        }

        public bool Get(InterruptProductionDirector director) =>
            director != null && (IsCue ? director.Get(Cue) : director.IsEffectOn(Effect));

        public void Set(InterruptProductionDirector director, bool on)
        {
            if (director == null)
                return;

            if (IsCue)
                director.Set(Cue, on);
            else
                director.SetEffect(Effect, on);
        }
    }

    /// <summary>
    /// 패널 줄 — 지금 런타임(일반·대쉬·제압 = V3, 단계 ownerDim 전부 끔)에서 실제로 보이는 칸만(11-06 할 일 238 정리).
    /// 뺀 칸: 오너 암전(단계 ownerDim 끔이라 안 켜짐)·②~⑥·월드 섬광·적중 지점 라이트·팀원 암순응 A2(V0 섬광 순간 경로에서만 쓰임 — V3 는 안 지남)·
    /// 성공 이펙트 A4(세 Variant 칸이 비어 아무것도 안 나옴). 디렉터 칸 자체는 그대로(인스펙터).
    /// </summary>
    public static readonly Row[] Rows =
    {
        new Row("빛 모임 (팀원 강 시도 — 남의 화면)", InterruptPresentationEffect.LightGather),
    };

    /// <summary>카메라 대상 중심 토글 글자(할 일 196).</summary>
    public const string CameraCenterLabel = "카메라 대상 중심 (196)";

    /// <summary>배속 버튼 — 끝값 + 인스펙터 프리셋.</summary>
    public static readonly float[] SpeedButtons =
        { DebugTimeScale.MinScale, 0.1f, 0.25f, 0.5f, 1f, DebugTimeScale.MaxScale };

    [Tooltip("패널을 펼친 채로 시작")]
    [SerializeField] bool expanded = true;

    [Tooltip("패널 너비(px)")]
    [SerializeField, Min(160f)] float width = 240f;

    DebugTimeScale _timeScale;
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
    DebugCharacterCycle _characterCycle; // 같은 오브젝트 — F1 순환·캐릭터 변경 버튼 공용(할 일 238)
#endif
    readonly DebugSuppressInterruptRepro _suppressRepro = new DebugSuppressInterruptRepro();
    readonly DebugInterruptTierPreview _tierPreview = new DebugInterruptTierPreview();

    void Awake()
    {
        TryGetComponent(out _timeScale);
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
        TryGetComponent(out _characterCycle);
#endif
    }

    void Update()
    {
        float now = Time.unscaledTime;
        _suppressRepro.Tick(now);
        _tierPreview.Tick(InterruptProductionDirector.Instance, now);
    }

    void OnDestroy() => _suppressRepro.Dispose();

    // ---- 화면 배치(11-05 정리) — 성격별 접는 섹션 · 일정한 버튼 폭 · 넘치면 스크롤 ----

    /// <summary>섹션(접기 단위) — 순서 = 화면 순서. 접힘 상태는 PlayerPrefs(<see cref="FoldPrefsPrefix"/> + 이름)에 기억한다.</summary>
    public enum Section
    {
        Replay,        // 간파 재생 — 지금 단계 표시·단계 재현
        Character,     // 캐릭터 변경 — 팔라딘/거너/어쌔신(할 일 238, F1 순환과 같은 길)
        Versions,      // 단계별 적중 라이트 — 일반·대쉬·제압 암순응 가/나·위치·재생(버전 버튼은 238 에서 삭제, V3)
        Whiteout,      // 화이트아웃·제압 — 3단계 화이트아웃 스위치·단계 에셋
        Effects,       // 화면 효과 — 효과 칸 켜기/끄기
        Camera,        // 카메라 — 대상 중심
        Server,        // 허수아비·서버 — 허수아비 간파 = 제압 간파
        CharacterVfx,  // 간파 스킬 캐릭터 VFX 개별 켜고 끄기(할 일 238)
        Speed,         // 배속(슬로우)
    }

    /// <summary>섹션 제목.</summary>
    public static string SectionTitle(Section section) => section switch
    {
        Section.Replay => "간파 재생",
        Section.Character => "캐릭터 변경",
        Section.Versions => "단계별 적중 라이트 (V3)",
        Section.Whiteout => "화이트아웃·제압 (219)",
        Section.Effects => "화면 효과",
        Section.Camera => "카메라",
        Section.Server => "허수아비·서버",
        Section.CharacterVfx => "캐릭터 VFX 개별 켜고 끄기 (238)",
        _ => "배속",
    };

    /// <summary>접힘 상태 PlayerPrefs 키 앞부분 — 값 1 = 펼침.</summary>
    public const string FoldPrefsPrefix = "DebugInterruptLab.Fold.";

    // 처음(키 없음)엔 자주 쓰는 재생·버전·배속만 펼친다.
    static bool DefaultOpen(Section section) => section == Section.Replay || section == Section.Character || section == Section.Versions || section == Section.Speed;

    const float Gap = 4f;          // 버튼 사이 간격
    const float RowHeight = 22f;   // 버튼·토글 높이
    const float PanelPadding = 12f; // 상자 안쪽 여백 + 스크롤 막대 몫

    Vector2 _scroll;
    GUIStyle _sectionStyle;

    void OnGUI()
    {
        _sectionStyle ??= new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };

        GUILayout.BeginArea(new Rect(Screen.width - width - 10f, 10f, width, Screen.height - 20f));
        GUILayout.BeginVertical(GUI.skin.box);

        if (GUILayout.Button(expanded ? "▼ 간파 연출 튜닝" : "▶ 간파 연출 튜닝", GUILayout.Height(RowHeight)))
            expanded = !expanded;

        if (expanded)
        {
            _scroll = GUILayout.BeginScrollView(_scroll);
            InterruptProductionDirector director = InterruptProductionDirector.Instance;
            if (director == null)
                GUILayout.Label("디렉터 없음(NetworkManager 대기)");
            else
            {
                DrawSection(Section.Replay, () => DrawReplay(director));
                DrawSection(Section.Character, DrawCharacter);
                DrawSection(Section.Versions, () => DrawTierVersions(director));
                DrawSection(Section.Whiteout, () => DrawWhiteout(director));
                DrawSection(Section.Effects, () => DrawEffects(director));
                DrawSection(Section.Camera, () => DrawCameraCenter(director));
                DrawSection(Section.Server, DrawSuppressRepro);
            }

            DrawSection(Section.CharacterVfx, DrawCharacterVfx); // 디렉터와 무관(정적 토글)
            DrawSection(Section.Speed, DrawSpeed);
            GUILayout.EndScrollView();
        }

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    void DrawSection(Section section, System.Action body)
    {
        string key = FoldPrefsPrefix + section;
        bool open = ReadFold(key, DefaultOpen(section));
        GUILayout.Space(Gap);
        if (GUILayout.Button((open ? "▼ " : "▶ ") + SectionTitle(section), _sectionStyle, GUILayout.Height(RowHeight)))
        {
            open = !open;
            WriteFold(key, open);
        }

        if (open)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            body();
            GUILayout.EndVertical();
        }
    }

    static bool ReadFold(string key, bool fallback)
    {
        try { return PlayerPrefs.GetInt(key, fallback ? 1 : 0) != 0; }
        catch { return fallback; }
    }

    static void WriteFold(string key, bool open)
    {
        try { PlayerPrefs.SetInt(key, open ? 1 : 0); }
        catch { /* 기억 못 해도 동작은 그대로 */ }
    }

    // 섹션 상자 안 한 줄 폭 — 버튼 n 개를 같은 폭으로 나눈다.
    float ButtonWidth(int count) => Mathf.Max(24f, (width - PanelPadding * 2f - Gap * (count - 1)) / Mathf.Max(1, count));

    /// <summary>같은 폭 버튼 한 줄 — 누른 버튼 번호(없으면 -1).</summary>
    int ButtonRow(params string[] labels)
    {
        int clicked = -1;
        float w = ButtonWidth(labels.Length);
        GUILayout.BeginHorizontal();
        for (int i = 0; i < labels.Length; i++)
        {
            if (i > 0)
                GUILayout.Space(Gap);
            if (GUILayout.Button(labels[i], GUILayout.Width(w), GUILayout.Height(RowHeight)))
                clicked = i;
        }

        GUILayout.EndHorizontal();
        return clicked;
    }

    /// <summary>같은 폭 버튼 격자 — 한 줄 <paramref name="perRow"/> 개씩. 누른 버튼 번호(없으면 -1).</summary>
    int ButtonGrid(string[] labels, int perRow)
    {
        int clicked = -1;
        for (int start = 0; start < labels.Length; start += perRow)
        {
            int count = Mathf.Min(perRow, labels.Length - start);
            var row = new string[count];
            System.Array.Copy(labels, start, row, 0, count);
            int hit = ButtonRow(row);
            if (hit >= 0)
                clicked = start + hit;
        }

        return clicked;
    }

    static bool Toggle(bool value, string label) => GUILayout.Toggle(value, label, GUILayout.Height(RowHeight));

    // 간파 연출 4단계(할 일 192) — 지금 단계 표시 + 단계 재현 버튼 4개(이 화면만, 슬로우·줌 없음).
    void DrawReplay(InterruptProductionDirector director)
    {
        GUILayout.Label($"내 마지막: {DebugInterruptTierPreview.Label(director.LastOwnerTier)}" +
                        $"  ·  받은 성공: {DebugInterruptTierPreview.Label(director.LastSuccessTier)}");

        var labels = new string[DebugInterruptTierPreview.All.Length];
        for (int i = 0; i < labels.Length; i++)
            labels[i] = DebugInterruptTierPreview.Label(DebugInterruptTierPreview.All[i]);
        int clicked = ButtonGrid(labels, 2); // 240px 에 4개는 글자가 잘려 2×2
        if (clicked >= 0)
            _tierPreview.Begin(director, DebugInterruptTierPreview.All[clicked], Time.unscaledTime);

        if (!string.IsNullOrEmpty(_tierPreview.Status))
            GUILayout.Label(_tierPreview.Status);
        GUILayout.Label("※ 재현은 이 화면만·슬로우 없음(줌·슬로우는 실제 간파)");
    }

    // 단계별 적중 라이트(할 일 194 → 11-06 238 정리: 버전 V0~V2 버튼 삭제, 런타임 = V3) — 일반·대쉬·제압 단계마다 암순응 가/나·월드 이펙트 위치
    // (이 피어 런타임 값만, 다음 성공부터) + 그 단계 재생. '실제' = 데이터·프리팹이 없어 V0 로 돌아갔는지까지 반영한 값.
    void DrawTierVersions(InterruptProductionDirector director)
    {
        bool first = true;
        foreach (InterruptPresentationTier tier in DebugInterruptTierPreview.VersionTiers)
        {
            InterruptTierProfile profile = director.TierProfile(tier);
            if (profile == null)
                continue;

            if (!first)
                GUILayout.Space(Gap * 2f);
            first = false;

            GUILayout.BeginHorizontal();
            GUILayout.Label($"{DebugInterruptTierPreview.Label(tier)} — 실제 {DebugInterruptTierPreview.LookLabel(director.SuccessLook(tier))}");
            if (GUILayout.Button("재생", GUILayout.Width(ButtonWidth(4)), GUILayout.Height(RowHeight)))
                _tierPreview.Begin(director, tier, Time.unscaledTime);
            GUILayout.EndHorizontal();

            InterruptDarkAdaptMode[] modes = DebugInterruptTierPreview.DarkAdaptModes;
            var modeLabels = new string[modes.Length];
            for (int i = 0; i < modes.Length; i++)
                modeLabels[i] = DebugInterruptTierPreview.Selected(DebugInterruptTierPreview.DarkAdaptLabel(modes[i]), profile.darkAdapt == modes[i]);
            int m = ButtonRow(modeLabels);
            if (m >= 0)
                profile.darkAdapt = modes[m];

            InterruptHitLightOrigin[] origins = DebugInterruptTierPreview.Origins;
            var originLabels = new string[origins.Length];
            for (int i = 0; i < origins.Length; i++)
                originLabels[i] = DebugInterruptTierPreview.Selected(DebugInterruptTierPreview.OriginLabel(origins[i]), profile.hitLightOrigin == origins[i]);
            int o = ButtonRow(originLabels);
            if (o >= 0)
                profile.hitLightOrigin = origins[o];
        }
    }

    /// <summary>제압 화이트아웃 토글 글자(할 일 219).</summary>
    public const string SuppressWhiteoutLabel = "제압 화이트아웃 (219)";

    // 3단계 제압 화이트아웃 스위치(할 일 219) — 이 피어 디렉터 런타임 값만, 다음 성공부터. 제압 SO whiteoutFrames 0 이면 켜도 안 나온다.
    void DrawWhiteout(InterruptProductionDirector director)
    {
        bool whiteout = director.SuppressWhiteout;
        bool next = Toggle(whiteout, SuppressWhiteoutLabel);
        if (next != whiteout)
            director.SuppressWhiteout = next;
        GUILayout.Label($"V3 에셋 — 대쉬 {AssetTierLabel(director, InterruptPresentationTier.Dash)} · 제압 {AssetTierLabel(director, InterruptPresentationTier.Suppress)}");
    }

    // 그 단계가 실제로 쓰는 V3 에셋 묶음 — 단계 칸이 비면 '1단계 대체'.
    static string AssetTierLabel(InterruptProductionDirector director, InterruptPresentationTier tier) =>
        director.ReferenceAssetTier(tier) == tier ? "전용" : "1단계 대체";

    void DrawEffects(InterruptProductionDirector director)
    {
        foreach (Row row in Rows)
        {
            bool on = row.Get(director);
            bool next = Toggle(on, row.Label);
            if (next != on)
                row.Set(director, next);
        }
    }

    // 카메라 대상 중심(할 일 196) — 이 피어 디렉터 런타임 값만, 다음 간파부터(진행 중인 이동은 그대로 끝난다). 저장 안 함.
    static void DrawCameraCenter(InterruptProductionDirector director)
    {
        bool on = director.CameraCenterEnabled;
        bool next = Toggle(on, CameraCenterLabel);
        if (next != on)
            director.CameraCenterEnabled = next;
    }

    // 허수아비 간파 = 제압 간파 재현(179 후속 3, 호스트에서만 효과) — 제압 단계를 실제 서버 성공 경로로 본다(219 — 오너 판정 순간 제압 예측도 덮어씀).
    void DrawSuppressRepro()
    {
        _suppressRepro.Enabled = Toggle(_suppressRepro.Enabled, "허수아비 간파 = 제압 간파(제압 직전)");
        if (_suppressRepro.Enabled && !DebugSuppressInterruptRepro.IsServer)
            GUILayout.Label("※ 호스트 화면에서만 효과(표식은 서버)");
        else if (_suppressRepro.Enabled)
            GUILayout.Label($"허수아비 {_suppressRepro.DummyCount} · 제압 간파 {_suppressRepro.MarkedCount}회");
    }

    /// <summary>캐릭터 변경 버튼 순서(할 일 238).</summary>
    public static readonly TutorialCharacter[] CharacterButtons =
        { TutorialCharacter.Paladin, TutorialCharacter.Gunner, TutorialCharacter.Assassin };

    /// <summary>캐릭터 변경 버튼 글자.</summary>
    public static string CharacterButtonLabel(TutorialCharacter character) => character switch
    {
        TutorialCharacter.Gunner => "거너",
        TutorialCharacter.Assassin => "어쌔신",
        _ => "팔라딘",
    };

#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
    string _characterStatus;
#endif

    // 캐릭터 변경(할 일 238) — 같은 오브젝트의 DebugCharacterCycle.TrySwapToServer(F1 순환·특성 랩 버튼과 같은 길: 디스폰 → 같은 자리 새 Variant).
    // 호스트 화면만 된다(새 RPC 없음). 바꾸면 HP·쿨·상태가 새로 시작된다.
    void DrawCharacter()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
        if (_characterCycle == null)
        {
            GUILayout.Label("DebugCharacterCycle 없음");
            return;
        }

        var labels = new string[CharacterButtons.Length];
        for (int i = 0; i < labels.Length; i++)
            labels[i] = CharacterButtonLabel(CharacterButtons[i]);
        int clicked = ButtonRow(labels);
        if (clicked >= 0)
        {
            TutorialCharacter character = CharacterButtons[clicked];
            int id = DebugCharacterCycle.FindCharacterId(_characterCycle.Roster, character);
            if (id < 0)
                _characterStatus = $"로스터에 {CharacterButtonLabel(character)} 없음";
            else
                _characterStatus = _characterCycle.TrySwapToServer(id)
                    ? $"{CharacterButtonLabel(character)}(으)로 변경"
                    : "변경 안 됨(이미 그 캐릭터·연타 간격·클라 창 — 콘솔)";
        }

        Unity.Netcode.NetworkManager networkManager = Unity.Netcode.NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening && !networkManager.IsServer)
            GUILayout.Label("※ 호스트 화면에서만 바뀐다");
        if (!string.IsNullOrEmpty(_characterStatus))
            GUILayout.Label(_characterStatus);
#else
        GUILayout.Label("이 빌드에선 캐릭터 변경 없음");
#endif
    }

    // 간파 스킬 캐릭터 VFX 개별 켜고 끄기(할 일 238) — InterruptVfxToggles(정적·이 피어만·저장 안 함, Play 시작마다 전부 켬).
    // 끄면 그 VFX 재생만 건너뛴다(판정·슬로우·공용 간파 연출 그대로). 목록 = Docs/tech/interrupt-vfx-list.md.
    void DrawCharacterVfx()
    {
        // 표시·조작 = '실제로 나오는가'(이 토글 ∧ 디렉터 B1·B2·B3·C1·C2 칸). 그 칸들은 11-06(할 일 238)부터 NetworkManager.prefab 기본 켬 —
        // 런타임에 꺼져 있으면 켤 때 칸도 런타임으로 켠다(저장 안 함 — Play 끝나면 프리팹 값). 끌 때는 이 토글만.
        InterruptProductionDirector director = InterruptProductionDirector.Instance;
        switch (ButtonRow("전체 켬", "전체 끔"))
        {
            case 0: SetVfx(director, _ => true, true); break;
            case 1: SetVfx(director, _ => true, false); break;
        }

        InterruptVfxCharacter? group = null;
        foreach (InterruptVfx vfx in InterruptVfxToggles.All)
        {
            InterruptVfxCharacter character = InterruptVfxToggles.CharacterOf(vfx);
            if (group != character)
            {
                group = character;
                GUILayout.Space(Gap);
                GUILayout.BeginHorizontal();
                GUILayout.Label(InterruptVfxToggles.CharacterLabel(character), GUILayout.Width(ButtonWidth(3)));
                if (GUILayout.Button("켬", GUILayout.Width(ButtonWidth(3)), GUILayout.Height(RowHeight)))
                    SetVfx(director, v => InterruptVfxToggles.CharacterOf(v) == character, true);
                GUILayout.Space(Gap);
                if (GUILayout.Button("끔", GUILayout.Width(ButtonWidth(3)), GUILayout.Height(RowHeight)))
                    SetVfx(director, v => InterruptVfxToggles.CharacterOf(v) == character, false);
                GUILayout.EndHorizontal();
            }

            bool on = InterruptVfxToggles.IsEffective(vfx, director);
            bool next = Toggle(on, InterruptVfxToggles.Label(vfx));
            if (next != on)
                InterruptVfxToggles.SetEffective(vfx, next, director);
        }

        if (director == null)
            GUILayout.Label("※ 디렉터 없음 — 팔라딘·어쌔신 VFX 는 디렉터 칸이 있어야 나온다");
        if (!InterruptVfxToggles.GateActive)
            GUILayout.Label("※ 릴리스 빌드 — 토글이 무시된다(늘 켬)");
        GUILayout.Label("※ 이 화면만·저장 안 함. 다음 재생부터(루프 중이면 다음 시전)");
    }

    static void SetVfx(InterruptProductionDirector director, System.Func<InterruptVfx, bool> match, bool on)
    {
        foreach (InterruptVfx vfx in InterruptVfxToggles.All)
        {
            if (match(vfx))
                InterruptVfxToggles.SetEffective(vfx, on, director);
        }
    }

    void DrawSpeed()
    {
        if (_timeScale == null)
        {
            GUILayout.Label("DebugTimeScale 없음");
            return;
        }

        GUILayout.Label($"배속 ×{_timeScale.Scale:0.00}" +
            (_timeScale.IsHoldingNormalSpeed ? $"  (성공 후 1배속 {_timeScale.NormalSpeedRemaining:0.0}s)" : ""));
        _timeScale.Scale = GUILayout.HorizontalSlider(_timeScale.Scale, DebugTimeScale.MinScale, DebugTimeScale.MaxScale);

        var labels = new string[SpeedButtons.Length];
        for (int i = 0; i < SpeedButtons.Length; i++)
            labels[i] = SpeedButtons[i].ToString("0.##");
        int clicked = ButtonRow(labels);
        if (clicked >= 0)
            _timeScale.Scale = SpeedButtons[clicked];
    }

}
