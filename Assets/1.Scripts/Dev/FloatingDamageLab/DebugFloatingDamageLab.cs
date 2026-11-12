using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 할 일 242 — 피해 숫자 확인 씬(<c>Assets/0.Scenes/Debug/DebugFloatingDamage.unity</c>) 구동기.
/// 네트워크 없이 ▶ 하면 바로 쓴다: 씬에 놓인 실제 프리팹(23호 레이드·웰즈 포함 / 팔라딘 / 일반 몬스터)의 스크립트를 꺼서(AI·공격 없음, 애니메이터만)
/// 세워 두고, 화면 버튼·단축키로 선택 대상에 숫자를 띄운다(<see cref="FloatingDamageSpawner.Submit"/> — 게임과 같은 경로).
/// 숫자 크기 = <see cref="FloatingDamageSettings"/> 에셋 칸, 위치 = 대상 프리팹 루트 <see cref="FloatingDamageAnchor"/>.
/// SO 는 Play 중 고쳐도 남고 다음 숫자부터 반영된다. 앵커는 Play 중 고치면 끝날 때 날아가므로 '프리팹에 저장' 버튼으로 원본 프리팹에 쓴다.
/// 카메라 = 실제 플레이 카메라와 같은 각도·거리(<c>PlayerFollowCamera</c> FollowOffset (7,17,-7)·FOV 34).
/// 🔴 빌드 목록(EditorBuildSettings)에 넣지 않는다 — 씬을 열고 그냥 ▶. 사용법 = Docs/tech/floating-damage-design.md §12.
/// </summary>
[DefaultExecutionOrder(-32000)]
[DisallowMultipleComponent]
public sealed class DebugFloatingDamageLab : MonoBehaviour
{
    [Serializable]
    public sealed class TargetGroup
    {
        public string label;
        [Tooltip("씬에 놓은 대상 프리팹 인스턴스 루트. 아래 Unit 전부(23호 + 웰즈 등)가 선택 대상이 된다")]
        public Transform root;
        [Tooltip("구간(낮음·중간·높음) 판정 기준 최대 체력 — 네트워크 없이는 Unit 최대 체력이 0 이라 여기 값을 쓴다(실제 데이터 값으로)")]
        [Min(1)] public int baseMaxHp = 100;
    }

    public enum KindChoice { Hp, Shield, HpAndShield, Heal, Status, Text }

    /// <summary>공격자(힐이면 힐한 사람) 자리 — 맞은 반대쪽 연출(268) 확인용. 없음 = 몬스터·장판·도트·자기 회복처럼 위로만.</summary>
    public enum SourceChoice { None, Left, Right, Front }

    [SerializeField] TargetGroup[] groups = Array.Empty<TargetGroup>();

    [Header("카메라 — 실제 플레이 카메라(PlayerFollowCamera)와 같은 값")]
    [SerializeField] Camera viewCamera;
    [SerializeField] Vector3 cameraOffset = new Vector3(7f, 17f, -7f);
    [SerializeField] Vector3 cameraEuler = new Vector3(59.787f, -45f, 0f);
    [SerializeField] float fieldOfView = 34f;
    [Tooltip("켜면 선택 대상이 화면 가운데(플레이어 시점처럼), 끄면 Overview Focus 기준 전체 보기")]
    [SerializeField] bool followSelected;
    [SerializeField] Vector3 overviewFocus = Vector3.zero;

    [Header("띄우기")]
    [SerializeField, Min(0.05f)] float autoInterval = 0.6f;
    [SerializeField, Range(2, 20)] int multiHits = 6;
    [SerializeField, Min(0.02f)] float multiHitInterval = 0.1f;
    [Tooltip("공격자 자리 — 대상 생성점에서 화면 좌우(카메라 오른쪽 방향, 바닥 평면)로 이 거리(m). 정면 = 카메라 쪽.")]
    [SerializeField, Min(0.1f)] float sourceDistance = 3f;

    sealed class Entry
    {
        public Unit unit;
        public string label;
        public int baseMaxHp;
        public Transform groupRoot;
    }

    readonly List<Entry> _entries = new List<Entry>();
    int _selected;
    KindChoice _kind = KindChoice.Hp;
    AttackHitPattern _pattern = AttackHitPattern.Single;
    int _tier = 1; // 0 낮음 · 1 중간 · 2 높음 · 3 직접
    string _customAmount = "123";
    bool _fromLocalPlayer = true;
    SourceChoice _source = SourceChoice.Left;
    Vector3 _lastSource;
    bool _hasLastSource;
    bool _auto;
    float _nextAutoAt;
    bool _panelVisible = true;
    string _status = string.Empty;
    string _lastShot = string.Empty;
    FloatingDamageTier? _lastTier;
    Vector2 _scroll;

    void Awake()
    {
        foreach (TargetGroup group in groups)
        {
            if (group == null || group.root == null)
                continue;

            Freeze(group.root);
            Unit[] units = group.root.GetComponentsInChildren<Unit>(true);
            for (int i = 0; i < units.Length; i++)
            {
                _entries.Add(new Entry
                {
                    unit = units[i],
                    label = i == 0 ? group.label : $"{group.label} · {units[i].name}",
                    baseMaxHp = group.baseMaxHp,
                    groupRoot = group.root,
                });
            }
        }

        if (_entries.Count == 0)
            _status = "대상이 없다 — 구동기 Groups 칸에 씬의 프리팹 인스턴스를 넣는다.";
    }

    // AI·공격·네트워크 스크립트를 끈다(애니메이터·조명·앵커만 남김). 물리는 멈춘다. 네트워크 없이 서 있기만 하면 된다.
    static void Freeze(Transform root)
    {
        foreach (Behaviour behaviour in root.GetComponentsInChildren<Behaviour>(true))
        {
            if (behaviour is Animator || behaviour is Light || behaviour is FloatingDamageAnchor)
                continue;
            behaviour.enabled = false;
        }

        foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>(true))
        {
            body.isKinematic = true;
            body.useGravity = false;
        }
    }

    Entry Selected => _entries.Count > 0 ? _entries[Mathf.Clamp(_selected, 0, _entries.Count - 1)] : null;

    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
            HandleKeys(keyboard);

        if (_auto && Time.unscaledTime >= _nextAutoAt)
        {
            _nextAutoAt = Time.unscaledTime + autoInterval;
            Fire();
        }
    }

    void HandleKeys(Keyboard keyboard)
    {
        for (int i = 0; i < 9 && i < _entries.Count; i++)
        {
            if (keyboard[Key.Digit1 + i].wasPressedThisFrame)
                _selected = i;
        }

        if (keyboard.spaceKey.wasPressedThisFrame) Fire();
        if (keyboard.rKey.wasPressedThisFrame) ToggleAuto();
        if (keyboard.mKey.wasPressedThisFrame)
            _pattern = _pattern == AttackHitPattern.Single ? AttackHitPattern.Multi : AttackHitPattern.Single;
        if (keyboard.zKey.wasPressedThisFrame) _tier = 0;
        if (keyboard.xKey.wasPressedThisFrame) _tier = 1;
        if (keyboard.cKey.wasPressedThisFrame) _tier = 2;
        if (keyboard.hKey.wasPressedThisFrame) _kind = KindChoice.Hp;
        if (keyboard.sKey.wasPressedThisFrame) _kind = KindChoice.Shield;
        if (keyboard.bKey.wasPressedThisFrame) _kind = KindChoice.HpAndShield;
        if (keyboard.tKey.wasPressedThisFrame) _fromLocalPlayer = !_fromLocalPlayer;
        if (keyboard.aKey.wasPressedThisFrame) _source = (SourceChoice)(((int)_source + 1) % 4);
        if (keyboard.fKey.wasPressedThisFrame) followSelected = !followSelected;
        if (keyboard.f1Key.wasPressedThisFrame) _panelVisible = !_panelVisible;
    }

    void ToggleAuto()
    {
        _auto = !_auto;
        _nextAutoAt = Time.unscaledTime;
    }

    void LateUpdate()
    {
        if (viewCamera == null)
            return;

        Entry selected = Selected;
        Vector3 focus = followSelected && selected != null && selected.groupRoot != null ? selected.groupRoot.position : overviewFocus;
        viewCamera.transform.SetPositionAndRotation(focus + cameraOffset, Quaternion.Euler(cameraEuler));
        viewCamera.fieldOfView = fieldOfView;
    }

    // ---- 띄우기 ----

    void Fire()
    {
        FloatingDamageSpawner spawner = FloatingDamageSpawner.Instance;
        Entry entry = Selected;
        if (spawner == null || spawner.Settings == null)
        {
            _status = "씬에 FloatingDamageSpawner(설정 칸 포함)가 없다.";
            return;
        }
        if (entry == null || entry.unit == null)
            return;

        int total = ResolveAmount(spawner.Settings, entry);
        if (_pattern == AttackHitPattern.Multi)
            StartCoroutine(FireMulti(entry, total));
        else
            Submit(entry, total, AttackHitPattern.Single);
    }

    int ResolveAmount(FloatingDamageSettings settings, Entry entry)
    {
        if (_tier == 3)
            return int.TryParse(_customAmount, out int custom) ? Mathf.Max(1, custom) : 1;

        settings.TryGetTierThresholds(entry.unit.Rank, out float mid, out float high);
        return DebugFloatingDamageLabRules.AmountFor((FloatingDamageTier)_tier, entry.baseMaxHp, mid, high);
    }

    // 누적 = 같은 키로 여러 번 — 합계가 고른 구간이 되게 나눈다(구간은 누적 합계로 판정).
    IEnumerator FireMulti(Entry entry, int total)
    {
        int perHit = DebugFloatingDamageLabRules.PerHitAmount(total, multiHits);
        for (int i = 0; i < multiHits; i++)
        {
            Submit(entry, perHit, AttackHitPattern.Multi);
            yield return new WaitForSeconds(multiHitInterval);
        }
    }

    void Submit(Entry entry, int amount, AttackHitPattern pattern)
    {
        FloatingDamageSpawner spawner = FloatingDamageSpawner.Instance;
        if (spawner == null || entry.unit == null)
            return;

        // 게임처럼 한 타격이 보호막과 체력을 함께 깎으면 보호막 숫자 → HP 숫자 순.
        switch (_kind)
        {
            case KindChoice.Hp: SubmitKind(spawner, entry, PopupKind.Damage, amount, pattern); break;
            case KindChoice.Shield: SubmitKind(spawner, entry, PopupKind.ShieldDamage, amount, pattern); break;
            case KindChoice.HpAndShield:
                SubmitKind(spawner, entry, PopupKind.ShieldDamage, Mathf.Max(1, amount / 2), pattern);
                SubmitKind(spawner, entry, PopupKind.Damage, amount, pattern);
                break;
            case KindChoice.Heal: SubmitKind(spawner, entry, PopupKind.Heal, amount, pattern); break;
            case KindChoice.Status: SubmitKind(spawner, entry, PopupKind.Status, amount, pattern); break;
            case KindChoice.Text: SubmitKind(spawner, entry, PopupKind.Text, amount, pattern); break;
        }
    }

    void SubmitKind(FloatingDamageSpawner spawner, Entry entry, PopupKind kind, int amount, AttackHitPattern pattern)
    {
        if (!spawner.Settings.TryGetStyle(kind, out _))
        {
            _status = $"{kind} 스타일이 설정에 없다 — 안 뜬다.";
            return;
        }

        // 공격자 0 = 내 공격, 1 = 팀원(표시 필터 '내 데미지 강조'면 작고 흐리게).
        var request = new FloatingPopupRequest(entry.unit, kind, amount, _fromLocalPlayer,
            _fromLocalPlayer ? 0UL : 1UL, AttackType.Default, pattern)
        {
            baseMaxHpOverride = entry.baseMaxHp,
        };
        // 공격자 자리(268) — 네트워크가 없어 clientId 로 못 찾으니 기준점을 직접 단다(게임은 Presenter 가 단다).
        Vector3 cameraRight = viewCamera != null ? viewCamera.transform.right : Vector3.right;
        Vector3 cameraBack = viewCamera != null ? -viewCamera.transform.forward : Vector3.back;
        request.hasSource = DebugFloatingDamageLabRules.TryGetSource(_source, FloatingDamageSpawner.ResolveAnchor(entry.unit),
            cameraRight, cameraBack, sourceDistance, out request.sourceWorld);
        _hasLastSource = request.hasSource;
        _lastSource = request.sourceWorld;
        spawner.Submit(request);
        _lastShot = DescribeShot(spawner.Settings, request, kind);
    }

    // 방금 띄운 숫자가 설정 SO 의 어느 칸을 읽었는지 — 스포너·팝업과 같은 계산(구간 판정·높이·유지 크기·팀원 배율·해상도 하한).
    string DescribeShot(FloatingDamageSettings settings, FloatingPopupRequest request, PopupKind kind)
    {
        settings.TryGetStyle(kind, out FloatingPopupStyle style);
        FloatingDamageTier tier = settings.ClassifyTier(request.amount, request.BaseMaxHp, request.target.Rank);
        _lastTier = tier;
        FloatingDamageTierLook look = settings.GetLook(tier);
        float display = FloatingDamageDisplayPolicy.Evaluate(settings.DisplayFilter, request.fromLocalPlayer,
            settings.TeammateScaleMultiplier, settings.TeammateAlpha).scaleMultiplier;
        float canvasScale = Screen.height / Mathf.Max(1f, settings.ReferenceResolution.y);
        // 판정 크기 배율(268) — 스포너와 같은 측정·같은 식.
        FloatingDamageSpawner spawner = FloatingDamageSpawner.Instance;
        float hurtboxSize = spawner != null ? spawner.ResolveHurtboxSize(request.target) : 0f;
        float hurtbox = settings.HurtboxScale(hurtboxSize);
        float steady = style.height * look.holdScale * display * hurtbox;
        float floor = FloatingDamageMinPixelPolicy.Multiplier(settings.MinScreenPixelHeight, steady, canvasScale);
        string floorNote = floor > 1f ? $" — [!] 해상도 하한 {settings.MinScreenPixelHeight:0}px 에 걸려 높이를 바꿔도 안 변함" : string.Empty;
        string side = !request.hasSource ? "위로만(공격자 없음)" : _source == SourceChoice.Front ? "정면 → 위로만" : $"공격자 {_source} → 숫자 반대쪽";
        return $"방금: {kind} {request.amount} → 구간 {tier} · {side}\n" +
               $"  판정 크기 {hurtboxSize:0.##}m ÷ 기준 {settings.HurtboxReferenceSize:0.##} → 배율 {hurtbox:0.##} (최소 {settings.HurtboxScaleMin:0.##}·최대 {settings.HurtboxScaleMax:0.##})\n" +
               $"  높이 {style.height:0.#} × 유지 {look.holdScale:0.##} × 팀원 {display:0.##} × 판정 {hurtbox:0.##} = {steady:0.#} (화면 {steady * canvasScale * floor:0}px){floorNote}";
    }

    // ---- 화면 ----

    void OnGUI()
    {
        if (!_panelVisible)
        {
            GUI.Label(new Rect(10, 10, 300, 22), "F1 = 피해 숫자 패널");
            return;
        }

        GUILayout.BeginArea(new Rect(10, 10, 380, Screen.height - 20), GUI.skin.box);
        _scroll = GUILayout.BeginScrollView(_scroll);
        GUILayout.Label("<b>피해 숫자 확인 (242)</b>  F1 숨김", Rich());

        GUILayout.Label("대상 (1~9)");
        for (int i = 0; i < _entries.Count; i++)
        {
            string mark = i == _selected ? "▶ " : "   ";
            if (GUILayout.Button($"{mark}{i + 1}. {_entries[i].label}  ({_entries[i].unit.Rank}, 기준 HP {_entries[i].baseMaxHp})"))
                _selected = i;
        }

        GUILayout.Space(4);
        GUILayout.Label("종류 (H HP · S 보호막 · B 둘 다)");
        _kind = (KindChoice)GUILayout.SelectionGrid((int)_kind, new[] { "HP", "보호막", "HP+보호막", "회복", "상태", "텍스트" }, 3);
        GUILayout.Label("흐름 (M 전환)");
        _pattern = (AttackHitPattern)GUILayout.Toolbar((int)_pattern, new[] { "단발", $"누적 ×{multiHits}" });
        GUILayout.Label("구간 (Z 낮음 · X 중간 · C 높음)");
        _tier = GUILayout.Toolbar(_tier, new[] { "낮음", "중간", "높음", "직접" });
        if (_tier == 3)
            _customAmount = GUILayout.TextField(_customAmount);
        _fromLocalPlayer = GUILayout.Toggle(_fromLocalPlayer, " 내 공격 (끄면 팀원 공격 — T)");
        GUILayout.Label("공격자 자리 (A 전환 — 숫자는 반대쪽에, 없음·정면 = 위로만)");
        _source = (SourceChoice)GUILayout.Toolbar((int)_source, new[] { "없음", "왼쪽", "오른쪽", "정면" });

        GUILayout.Space(4);
        if (GUILayout.Button("띄우기 (Space)", GUILayout.Height(30)))
            Fire();
        if (GUILayout.Button(_auto ? $"자동 반복 끄기 (R) — {autoInterval:0.##}초마다" : "자동 반복 켜기 (R)"))
            ToggleAuto();
        followSelected = GUILayout.Toggle(followSelected, " 카메라 = 선택 대상 중심 (F)");

        DrawSettings();
        DrawAnchor();

        if (!string.IsNullOrEmpty(_status))
            GUILayout.Label(_status);

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    void DrawSettings()
    {
        FloatingDamageSpawner spawner = FloatingDamageSpawner.Instance;
        FloatingDamageSettings settings = spawner != null ? spawner.Settings : null;
        GUILayout.Space(6);
        GUILayout.Label("<b>숫자 크기 — 설정 SO (Play 중 고쳐도 남음, 다음 숫자부터)</b>", Rich());
        if (settings == null)
        {
            GUILayout.Label("설정 없음");
            return;
        }

        StyleRow(settings, PopupKind.Damage, "HP 숫자 높이");
        StyleRow(settings, PopupKind.ShieldDamage, "보호막 숫자 높이");
        GUILayout.Label("  tierLooks (▶ = 방금 숫자가 쓴 줄 — 다른 줄을 고치면 안 보인다)");
        for (int t = 0; t < 3; t++)
        {
            FloatingDamageTierLook look = settings.GetLook((FloatingDamageTier)t);
            string mark = _lastTier == (FloatingDamageTier)t ? "▶" : "  ";
            // 등장 확대·흔들림은 spawnSettleDuration > 0 이고 등장 > 유지일 때만 돈다(FloatingDamageTierLook.HasSpawnPop).
            string pop = look.HasSpawnPop ? $"등장 ×{look.spawnScale:0.##}" : "[!] 등장 확대·흔들림 꺼짐(settle 0 또는 등장≤유지)";
            GUILayout.Label($"  {mark} [{t}] {(FloatingDamageTier)t}: 유지 ×{look.holdScale:0.##} · {pop}");
        }
        if (!string.IsNullOrEmpty(_lastShot))
            GUILayout.Label(_lastShot);
        GUILayout.Label($"  몸 중심 위 spawnOffset {settings.SpawnOffset:0.#}px");
        GUILayout.Label($"  외곽선 알파 outlineAlpha {settings.OutlineAlpha:0.##} · 반대쪽 밀기 {settings.SidePush:0.#}px + 흐름 {settings.SideDrift:0.#}px / {settings.SideDriftDuration:0.##}초 · 정면 문턱 {settings.SideFrontThreshold:0.#}px");
#if UNITY_EDITOR
        if (GUILayout.Button("설정 SO 인스펙터에서 열기"))
        {
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }
#endif
    }

    void StyleRow(FloatingDamageSettings settings, PopupKind kind, string label)
    {
        settings.TryGetStyle(kind, out FloatingPopupStyle style);
        GUILayout.BeginHorizontal();
        GUILayout.Label($"  {label} {style.height:0.#}px", GUILayout.Width(190));
#if UNITY_EDITOR
        if (GUILayout.Button("-4")) _status = DebugFloatingDamageLabEditing.NudgeStyleHeight(settings, kind, -4f);
        if (GUILayout.Button("-1")) _status = DebugFloatingDamageLabEditing.NudgeStyleHeight(settings, kind, -1f);
        if (GUILayout.Button("+1")) _status = DebugFloatingDamageLabEditing.NudgeStyleHeight(settings, kind, 1f);
        if (GUILayout.Button("+4")) _status = DebugFloatingDamageLabEditing.NudgeStyleHeight(settings, kind, 4f);
#endif
        GUILayout.EndHorizontal();
    }

    void DrawAnchor()
    {
        Entry entry = Selected;
        GUILayout.Space(6);
        GUILayout.Label("<b>생성 위치 — 대상 프리팹 루트 FloatingDamageAnchor</b>", Rich());
        if (entry == null || entry.unit == null)
            return;

        if (!entry.unit.TryGetComponent(out FloatingDamageAnchor anchor))
        {
            GUILayout.Label("  앵커 없음 → 몸 콜라이더 중심(초록 구). 추가는 프리팹 루트에.");
            return;
        }

        string basis = anchor.AnchorTransform != null ? $"본 {anchor.AnchorTransform.name}" : $"로컬 {anchor.LocalOffset}";
        GUILayout.Label($"  기준 {basis}\n  worldOffset {anchor.WorldOffset}");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Y -0.5")) Nudge(anchor, new Vector3(0f, -0.5f, 0f));
        if (GUILayout.Button("Y -0.1")) Nudge(anchor, new Vector3(0f, -0.1f, 0f));
        if (GUILayout.Button("Y +0.1")) Nudge(anchor, new Vector3(0f, 0.1f, 0f));
        if (GUILayout.Button("Y +0.5")) Nudge(anchor, new Vector3(0f, 0.5f, 0f));
        GUILayout.EndHorizontal();
        GUILayout.Label("  (인스펙터에서 직접 고쳐도 된다 — Play 끝나면 되돌아감)");
#if UNITY_EDITOR
        if (GUILayout.Button("현재 앵커 값을 프리팹에 저장", GUILayout.Height(26)))
            _status = DebugFloatingDamageLabEditing.SaveAnchorToPrefab(anchor);
        if (GUILayout.Button("앵커 선택(인스펙터)"))
            Selection.activeObject = anchor;
#endif
    }

    static void Nudge(FloatingDamageAnchor anchor, Vector3 delta) => anchor.WorldOffset += delta;

    static GUIStyle _rich;
    static GUIStyle Rich() => _rich ??= new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };

    // 앵커(숫자 생성점) 표시 — 노랑 = 앵커, 초록 = 앵커 없음(콜라이더 중심). 에디트 모드에서도 보인다(씬 뷰, Game 뷰는 Gizmos 켬).
    void OnDrawGizmos()
    {
        if (groups == null)
            return;

        // 마지막 숫자의 공격자 자리(268) — 빨간 구 + 대상 생성점까지 선.
        if (Application.isPlaying && _hasLastSource && Selected != null && Selected.unit != null)
        {
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 1f);
            Gizmos.DrawSphere(_lastSource, 0.2f);
            Gizmos.DrawLine(_lastSource, FloatingDamageSpawner.ResolveAnchor(Selected.unit));
        }

        foreach (TargetGroup group in groups)
        {
            if (group == null || group.root == null)
                continue;
            foreach (Unit unit in group.root.GetComponentsInChildren<Unit>(true))
            {
                bool hasAnchor = unit.TryGetComponent(out FloatingDamageAnchor _);
                Gizmos.color = hasAnchor ? new Color(1f, 0.85f, 0.2f, 1f) : new Color(0.3f, 1f, 0.4f, 1f);
                Vector3 point = FloatingDamageSpawner.ResolveAnchor(unit);
                Gizmos.DrawSphere(point, 0.12f);
                Gizmos.DrawWireSphere(point, 0.3f);
                Gizmos.DrawLine(unit.transform.position, point);
            }
        }
    }
}

/// <summary>할 일 242 디버그 씬 순수 규칙 — 구간별 피해량. EditMode 테스트 대상.</summary>
public static class DebugFloatingDamageLabRules
{
    /// <summary>
    /// 그 구간 한가운데쯤 들어가는 피해량 — 낮음 = 중간 경계의 절반, 중간 = 두 경계 사이 가운데, 높음 = 높음 경계 ×1.5. 최소 1.
    /// </summary>
    public static int AmountFor(FloatingDamageTier tier, int baseMaxHp, float midPercent, float highPercent)
    {
        float percent = tier switch
        {
            FloatingDamageTier.Low => midPercent * 0.5f,
            FloatingDamageTier.Mid => (midPercent + highPercent) * 0.5f,
            _ => highPercent * 1.5f,
        };
        return Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(0, baseMaxHp) * percent / 100f));
    }

    /// <summary>
    /// 공격자 자리(268) — 대상 생성점에서 카메라 오른쪽(바닥 평면) ± 거리, 정면 = 카메라 쪽(뒤쪽, 바닥 평면). 없음 = false(위로만).
    /// </summary>
    public static bool TryGetSource(DebugFloatingDamageLab.SourceChoice choice, Vector3 targetPoint,
        Vector3 cameraRight, Vector3 cameraBack, float distance, out Vector3 source)
    {
        Vector3 right = Flat(cameraRight);
        Vector3 back = Flat(cameraBack);
        switch (choice)
        {
            case DebugFloatingDamageLab.SourceChoice.Left: source = targetPoint - right * distance; return true;
            case DebugFloatingDamageLab.SourceChoice.Right: source = targetPoint + right * distance; return true;
            case DebugFloatingDamageLab.SourceChoice.Front: source = targetPoint + back * distance; return true;
            default: source = default; return false;
        }
    }

    static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.zero;
    }

    /// <summary>누적 한 번 값 — 합계(한 번 값 × 횟수)가 넘치지 않게 내림, 최소 1.</summary>
    public static int PerHitAmount(int total, int hits) => Mathf.Max(1, total / Mathf.Max(1, hits));
}
