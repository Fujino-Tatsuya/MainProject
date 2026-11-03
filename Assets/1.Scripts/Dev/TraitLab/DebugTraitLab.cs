#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 특성 디버그 랩 OnGUI 패널(DebugTraitLab 씬에만 둔다, 할 일 135) — 특성 넣기/빼기·캐릭터 변경·배속·쿨 0·허수아비 HP 리셋·플레이어 체력 조절.
///
/// 특성은 디버그 우회 없이 정식 서버 경로다 — 고친 보유 목록을 <see cref="TraitDirector.ReplaceAcquiredServer"/> 로 보내면
/// 디렉터가 획득 행·이벤트 효과 장부를 맞추고, 수치형은 각 피어의 <see cref="PlayerTraitModifiers"/> 가 획득 행으로 다시 합성한다(실게임과 같은 결과).
/// 규칙(차수당 하나·규칙 무시·전부 빼기·차수별 랜덤·캐릭터 변경 시 초기화)은 <see cref="TraitLabModel"/>.
/// <list type="bullet">
/// <item>캐릭터 변경 = <see cref="DebugCharacterCycle"/>(F1 순환과 같은 길). 교체 직전(<see cref="DebugCharacterCycle.ServerSwapping"/>) 보유 특성을 비운다 — F1 도 같다.</item>
/// <item>배속 = <see cref="DebugTimeScale"/>, 쿨 0 = <see cref="DebugZeroCooldown"/> 켜고 끄기, 허수아비 HP 리셋 = 서버 체력 설정.</item>
/// <item>플레이어 체력(2026-11-02 민경 — 나누는 불굴 확인용) = 씬의 플레이어마다 10%·30%·50%·만피·−10% (서버 체력 설정, 피해·회복 경로 아님).
/// 받을 아군 = 씬의 <see cref="DebugAllyDummy"/>(나누는 불굴 후보에 개발 전용으로 낀다). Dev Boot 는 MPPM 클론도 호스트로 띄워 클라가 붙지 않는다.</item>
/// <item>플레이어 피격(2026-11-02 민경) = 가장 가까운 <see cref="TrainingDummy"/> 를 공격자로 내 플레이어를 정식 피격 경로(<see cref="Player.ReceiveAttack"/>)로 때린다.
/// 1회·자동(0.5/1/2초), 피해 = 최대 체력 5/10/30%. E 방어·수호의 잔광·불굴의 가호 보호막 소모·깨짐·패시브 피격 쿨감 확인용.</item>
/// <item>호스트 1인 기준. 클라 창은 보유 목록만 보인다(버튼 비활성, 새 RPC 없음).</item>
/// <item>공격 범위 기즈모(할 일 152·153 6단계, 에디터 전용) = <see cref="TraitLabAttackGizmos"/> — 판정이 쓰는 (도형, 포즈) 를 그대로 받아 <see cref="AttackShapeDrawing"/> 으로만 그린다.</item>
/// </list>
/// 게이트 = <c>UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS</c>(Docs/tech/debug-hotkeys.md).
/// </summary>
[DisallowMultipleComponent]
public sealed class DebugTraitLab : MonoBehaviour
{
    private const string Tag = "[DebugTraitLab]";

    static readonly TutorialCharacter[] Characters = { TutorialCharacter.Paladin, TutorialCharacter.Gunner, TutorialCharacter.Assassin };
    static readonly string[] CharacterLabels = { "팔라딘", "거너", "어쌔신" };

    [SerializeField] DebugCharacterCycle characterCycle;
    [SerializeField] DebugTimeScale timeScale;
    [SerializeField] DebugZeroCooldown zeroCooldown;
    [Tooltip("패널 위치·폭(화면 픽셀). 높이는 화면에 맞춘다.")]
    [SerializeField] Vector2 panelPosition = new Vector2(10f, 10f);
    [SerializeField, Min(200f)] float panelWidth = 400f;

    readonly TraitLabModel model = new TraitLabModel();
    readonly List<int> hashScratch = new List<int>();
    readonly List<string> idScratch = new List<string>();
    readonly List<(string chosen, string disabled)> conflictScratch = new List<(string, string)>();
    readonly StringBuilder text = new StringBuilder();
    readonly System.Random random = new System.Random();
    readonly List<Unit> healthTargets = new List<Unit>();

    bool _folded;
    Vector2 _scroll;
    string _status = string.Empty;

#if UNITY_EDITOR
    // 공격 범위 기즈모(할 일 152) — 기본 꺼짐. 에디터 전용(Gizmos·Handles).
    readonly TraitLabAttackGizmos attackGizmos = new TraitLabAttackGizmos();

    void OnDrawGizmos() => attackGizmos.Draw();
#endif

    void OnEnable()
    {
        if (characterCycle != null)
            characterCycle.ServerSwapping += HandleSwappingServer;
    }

    void OnDisable()
    {
        if (characterCycle != null)
            characterCycle.ServerSwapping -= HandleSwappingServer;
    }

    // 교체 직전 — 옛 PlayerObject 가 살아 있을 때 이벤트 효과를 되돌린다(새 캐릭터는 빈 목록으로 시작).
    void HandleSwappingServer(ulong clientId)
    {
        TraitDirector director = TraitDirector.Instance;
        if (director != null && director.ReplaceAcquiredServer(clientId, System.Array.Empty<int>()))
            _status = "캐릭터 변경 — 보유 특성 초기화";
    }

    // 상태는 프레임마다 한 번 — OnGUI 는 프레임에 여러 번 불린다.
    void Update()
    {
        model.SetCharacter(Player.LocalPlayer != null ? TutorialLocalPlayerInfo.ResolveCharacter(Player.LocalPlayer) : TutorialCharacter.None);

        TraitDirector director = TraitDirector.Instance;
        NetworkManager networkManager = NetworkManager.Singleton;
        idScratch.Clear();
        if (director != null && networkManager != null && networkManager.IsListening)
        {
            director.GetAcquired(networkManager.LocalClientId, hashScratch);
            for (int i = 0; i < hashScratch.Count; i++)
            {
                TraitDefinition trait = director.GetTrait(hashScratch[i]);
                if (trait != null)
                    idScratch.Add(trait.Id);
            }
        }

        model.Sync(idScratch);

        healthTargets.Clear();
        healthTargets.AddRange(FindObjectsByType<Player>(FindObjectsSortMode.None));
        healthTargets.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));
        healthTargets.AddRange(FindObjectsByType<DebugAllyDummy>(FindObjectsSortMode.None));
        healthTargets.RemoveAll(u => u == null || !u.IsSpawned);

        TickAutoAttack();
    }

    void OnGUI()
    {
        float height = Mathf.Max(120f, Screen.height - panelPosition.y * 2f);
        GUILayout.BeginArea(new Rect(panelPosition.x, panelPosition.y, panelWidth, _folded ? 40f : height), GUI.skin.box);

        GUILayout.BeginHorizontal();
        GUILayout.Label("<b>특성 랩</b> (DebugTraitLab)", RichLabel());
        if (GUILayout.Button(_folded ? "펼치기" : "접기", GUILayout.Width(60f)))
            _folded = !_folded;
        GUILayout.EndHorizontal();

        if (!_folded)
        {
            _scroll = GUILayout.BeginScrollView(_scroll);
            DrawBody();
            GUILayout.EndScrollView();
        }

        GUILayout.EndArea();
    }

    void DrawBody()
    {
        TraitDirector director = TraitDirector.Instance;
        NetworkManager networkManager = NetworkManager.Singleton;
        bool host = networkManager != null && networkManager.IsListening && networkManager.IsServer;
        bool ready = director != null && director.IsSpawned && Player.LocalPlayer != null;

        if (director == null)
            GUILayout.Label("TraitDirector 없음 — 씬 구성 확인");
        else if (!host)
            GUILayout.Label("클라 창 — 보기 전용(조작은 호스트 창에서)");

        // ── 캐릭터 ──
        GUILayout.Label($"캐릭터: {CharacterName(model.Character)}  (F1 순환)");
        GUI.enabled = host && characterCycle != null;
        GUILayout.BeginHorizontal();
        for (int i = 0; i < Characters.Length; i++)
        {
            bool current = model.Character == Characters[i];
            if (GUILayout.Toggle(current, CharacterLabels[i], GUI.skin.button) && !current)
                SwapTo(Characters[i]);
        }
        GUILayout.EndHorizontal();

        // ── 특성 ──
        GUI.enabled = host && ready;
        model.IgnoreRules = GUILayout.Toggle(model.IgnoreRules, "규칙 무시(같은 차수 동시 보유)");
        IReadOnlyList<TraitCatalog.Slot> slots = model.Slots;
        for (int tier = 1; tier <= TraitCatalog.TierCount; tier++)
        {
            GUILayout.Label($"{tier}차");
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].Tier != tier)
                    continue;

                string id = slots[i].Id;
                bool owned = model.IsOwned(id);
                if (GUILayout.Toggle(owned, $"{(owned ? "■" : "□")} {TraitName(director, id)}  <size=10>{id}</size>", ToggleButton()) != owned)
                {
                    model.Toggle(id);
                    Apply(director, owned ? $"빼기 {id}" : $"넣기 {id}");
                }
            }
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("전부 빼기"))
        {
            model.ClearAll();
            Apply(director, "전부 빼기");
        }

        if (GUILayout.Button("차수별 랜덤"))
        {
            model.RandomPerTier(random.Next);
            Apply(director, "차수별 랜덤");
        }
        GUILayout.EndHorizontal();

        GUI.enabled = true;
        GUILayout.Label(OwnedText(director), RichLabel());
        if (!string.IsNullOrEmpty(_status))
            GUILayout.Label(_status);

        // ── 배속·쿨·허수아비 ──
        GUILayout.Space(6f);
        if (timeScale != null)
        {
            GUILayout.Label($"배속 ×{timeScale.Scale:0.00}{(timeScale.IsHoldingNormalSpeed ? " (간파 성공 후 1배속 중)" : "")}");
            timeScale.Scale = GUILayout.HorizontalSlider(timeScale.Scale, DebugTimeScale.MinScale, DebugTimeScale.MaxScale);
            GUILayout.BeginHorizontal();
            foreach (float preset in DebugTimeScale.Presets)
            {
                if (GUILayout.Button($"×{preset:0.##}"))
                    timeScale.Scale = preset;
            }
            GUILayout.EndHorizontal();
        }

        GUI.enabled = host;
        if (zeroCooldown != null)
            zeroCooldown.enabled = GUILayout.Toggle(zeroCooldown.enabled, "쿨 0(Q·E·R·간파·대시)");

        if (GUILayout.Button("허수아비 HP 리셋"))
            ResetDummies();
        GUI.enabled = true;

        DrawPlayerHealth(host);
        DrawIncomingAttack(host);

#if UNITY_EDITOR
        DrawAttackGizmoOptions();
#endif
    }

#if UNITY_EDITOR
    // ── 공격 범위 기즈모(할 일 152) ──
    void DrawAttackGizmoOptions()
    {
        GUILayout.Space(6f);
        attackGizmos.Enabled = GUILayout.Toggle(attackGizmos.Enabled, "공격 범위 기즈모");
        if (!attackGizmos.Enabled)
            return;

        GUILayout.BeginHorizontal();
        attackGizmos.ShowBasic = GUILayout.Toggle(attackGizmos.ShowBasic, GizmoLabel("기본공격", TraitLabGizmoCategory.Basic), RichToggle());
        attackGizmos.ShowSkill = GUILayout.Toggle(attackGizmos.ShowSkill, GizmoLabel("스킬", TraitLabGizmoCategory.Skill), RichToggle());
        attackGizmos.ShowTrait = GUILayout.Toggle(attackGizmos.ShowTrait, GizmoLabel("특성 추가 공격", TraitLabGizmoCategory.Trait), RichToggle());
        GUILayout.EndHorizontal();
        GUILayout.Label("<size=10>Gizmos 라 Game 뷰에서는 상단 Gizmos 버튼이 켜져 있어야 보입니다. 특성 추가 공격·거너 R 은 호스트 창에서만 나옵니다. " +
                        "'(도형 아님)' = 판정 도형이 아닌 콜라이더 판정(거너 평타·R·간파 상자).</size>", RichLabel());
    }

    static string GizmoLabel(string text, TraitLabGizmoCategory category) =>
        $"<color=#{ColorUtility.ToHtmlStringRGB(TraitLabAttackGizmos.ColorOf(category))}>■</color> {text}";
#endif

    void SwapTo(TutorialCharacter character)
    {
        int id = DebugCharacterCycle.FindCharacterId(characterCycle.Roster, character);
        if (id < 0)
        {
            _status = $"로스터에 {CharacterName(character)} 없음";
            return;
        }

        // 특성 초기화는 교체 직전 ServerSwapping 에서(F1 과 같은 길).
        if (!characterCycle.TrySwapToServer(id))
            _status = "캐릭터 변경 안 됨(연타 간격·프리팹 확인 — 콘솔)";
    }

    void Apply(TraitDirector director, string what)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (director == null || networkManager == null)
            return;

        hashScratch.Clear();
        IReadOnlyList<string> owned = model.Owned;
        for (int i = 0; i < owned.Count; i++)
            hashScratch.Add(TraitIdHash.Of(owned[i]));

        bool ok = director.ReplaceAcquiredServer(networkManager.LocalClientId, hashScratch);
        _status = ok ? what : $"{what} 실패 — 콘솔 확인";
        Debug.Log($"{Tag} {_status} → 보유 {owned.Count}개 [{string.Join(", ", owned)}]", this);
    }

    // ── 플레이어 체력(나누는 불굴 확인용) ──
    static readonly float[] HealthPresets = { 0.1f, 0.3f, 0.5f, 1f };

    void DrawPlayerHealth(bool host)
    {
        GUILayout.Space(6f);
        GUILayout.Label("플레이어 체력 <size=10>(아군 허수아비 = 나누는 불굴 받는 쪽)</size>", RichLabel());
        if (healthTargets.Count == 0)
        {
            GUILayout.Label("대상 없음");
            return;
        }

        GUI.enabled = host;
        foreach (Unit unit in healthTargets)
        {
            GUILayout.Label($"{HealthTargetName(unit)}  {unit.CurrentHealth}/{unit.MaxHp}");
            GUILayout.BeginHorizontal();
            foreach (float preset in HealthPresets)
            {
                if (GUILayout.Button(preset >= 1f ? "만피" : $"{preset * 100f:0}%"))
                    SetHealth(unit, Mathf.CeilToInt(unit.MaxHp * preset));
            }
            if (GUILayout.Button("−10%"))
                SetHealth(unit, unit.CurrentHealth - Mathf.CeilToInt(unit.MaxHp * 0.1f));
            GUILayout.EndHorizontal();
        }
        GUI.enabled = true;
    }

    static string HealthTargetName(Unit unit)
    {
        if (unit is Player player)
            return $"{(player == Player.LocalPlayer ? "나" : $"클라 {player.OwnerClientId}")} · {CharacterName(TutorialLocalPlayerInfo.ResolveCharacter(player))}";
        return $"아군 허수아비 · {unit.name}";
    }

    void SetHealth(Unit unit, int health)
    {
        // 0 은 사망 래치만 걸고 쓰러짐 흐름을 타지 않는다 — 최소 1.
        int target = Mathf.Clamp(health, 1, unit.MaxHp);
        unit.SetCurrentHealthOnServer(target);
        _status = $"'{unit.name}' 체력 → {target}/{unit.MaxHp}";
    }

    // ── 플레이어 피격(2026-11-02 민경 — 피격으로 발동하는 연출 확인용) ──
    // 랩엔 플레이어를 때리는 몬스터가 없다. 가장 가까운 허수아비를 공격자로 세워 정식 피격 경로(Player.ReceiveAttack, 서버)로 때린다 —
    // E 방어(공격자 위치로 각도 판정)·보호막 소모·깨짐·피격 파문·패시브 피격 쿨감이 실게임과 같은 길로 돈다.
    static readonly float[] AttackDamagePresets = { 0.05f, 0.1f, 0.3f };
    static readonly float[] AttackIntervalPresets = { 0.5f, 1f, 2f };

    float _attackDamageRatio = 0.1f;
    float _attackInterval = 1f;
    bool _autoAttack;
    float _nextAutoAttackAt;

    void DrawIncomingAttack(bool host)
    {
        GUILayout.Space(6f);
        GUILayout.Label("플레이어 피격 <size=10>(가장 가까운 허수아비가 나를 때림 — E 는 허수아비 쪽을 보고)</size>", RichLabel());

        Player target = Player.LocalPlayer;
        TrainingDummy attacker = target != null ? NearestDummy(target.transform.position) : null;
        if (target == null || attacker == null)
        {
            GUILayout.Label(target == null ? "내 플레이어 없음" : "허수아비 없음");
            return;
        }

        GUILayout.Label($"공격자: {attacker.name} ({Vector3.Distance(attacker.transform.position, target.transform.position):0.0}m)");

        GUI.enabled = host;
        GUILayout.BeginHorizontal();
        GUILayout.Label("피해", GUILayout.Width(36f));
        foreach (float preset in AttackDamagePresets)
        {
            if (GUILayout.Toggle(Mathf.Approximately(_attackDamageRatio, preset), $"최대 체력 {preset * 100f:0}%", GUI.skin.button))
                _attackDamageRatio = preset;
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("1회 공격"))
            HitPlayer(target, attacker);
        _autoAttack = GUILayout.Toggle(_autoAttack, "자동", GUI.skin.button, GUILayout.Width(50f));
        foreach (float preset in AttackIntervalPresets)
        {
            if (GUILayout.Toggle(Mathf.Approximately(_attackInterval, preset), $"{preset:0.#}초", GUI.skin.button))
                _attackInterval = preset;
        }
        GUILayout.EndHorizontal();
        GUI.enabled = true;
    }

    // 자동 공격은 Update 에서 — 패널을 접어도 계속 돈다. 배속을 따른다(Time.time).
    void TickAutoAttack()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        bool host = networkManager != null && networkManager.IsListening && networkManager.IsServer;
        if (!_autoAttack || !host || Time.time < _nextAutoAttackAt)
            return;

        _nextAutoAttackAt = Time.time + _attackInterval;
        Player target = Player.LocalPlayer;
        TrainingDummy attacker = target != null ? NearestDummy(target.transform.position) : null;
        if (attacker != null)
            HitPlayer(target, attacker);
    }

    TrainingDummy NearestDummy(Vector3 position)
    {
        TrainingDummy nearest = null;
        float best = float.MaxValue;
        foreach (TrainingDummy dummy in FindObjectsByType<TrainingDummy>(FindObjectsSortMode.None))
        {
            if (dummy == null || !dummy.IsSpawned || dummy.CurrentHealth <= 0)
                continue;

            float distance = (dummy.transform.position - position).sqrMagnitude;
            if (distance < best)
            {
                best = distance;
                nearest = dummy;
            }
        }
        return nearest;
    }

    void HitPlayer(Player target, TrainingDummy attacker)
    {
        int damage = Mathf.Max(1, Mathf.CeilToInt(target.MaxHp * _attackDamageRatio));
        var info = new AttackInfo(damage);
        var context = new AttackHitContext(attacker.transform.position, attacker.transform, null, attacker);
        int healthBefore = target.CurrentHealth;
        int shieldBefore = target.CurrentShield;
        target.ReceiveAttack(info, context);
        _status = $"피격 {damage} — 체력 {healthBefore}→{target.CurrentHealth}, 보호막 {shieldBefore}→{target.CurrentShield}";
    }

    void ResetDummies()
    {
        TrainingDummy[] dummies = FindObjectsByType<TrainingDummy>(FindObjectsSortMode.None);
        int count = 0;
        for (int i = 0; i < dummies.Length; i++)
        {
            if (dummies[i] != null && dummies[i].IsSpawned)
            {
                dummies[i].SetCurrentHealthOnServer(dummies[i].MaxHp);
                count++;
            }
        }

        _status = $"허수아비 {count}개 HP 리셋";
    }

    string OwnedText(TraitDirector director)
    {
        text.Clear();
        IReadOnlyList<string> owned = model.Owned;
        text.Append($"보유 {owned.Count}개");
        for (int i = 0; i < owned.Count; i++)
        {
            int tier = TraitCatalog.TryGetSlot(owned[i], out TraitCatalog.Slot slot) ? slot.Tier : 0;
            text.Append($"\n  {tier}차 {TraitName(director, owned[i])}");
        }

        model.GetConflicts(TraitContent.Registry, conflictScratch);
        for (int i = 0; i < conflictScratch.Count; i++)
        {
            text.Append($"\n<color=#ffb040>⚠ {TraitName(director, conflictScratch[i].chosen)} → " +
                        $"{TraitName(director, conflictScratch[i].disabled)} 효과 없음(2차 상충)</color>");
        }

        return text.ToString();
    }

    static string TraitName(TraitDirector director, string id)
    {
        TraitDefinition trait = director != null ? director.GetTrait(TraitIdHash.Of(id)) : null;
        return trait != null && !string.IsNullOrEmpty(trait.DisplayName) ? trait.DisplayName : id;
    }

    static string CharacterName(TutorialCharacter character)
    {
        int index = System.Array.IndexOf(Characters, character);
        return index >= 0 ? CharacterLabels[index] : "모름";
    }

    static GUIStyle _richLabel;
    static GUIStyle _richToggle;
    static GUIStyle _toggleButton;

    static GUIStyle RichToggle() => _richToggle ??= new GUIStyle(GUI.skin.toggle) { richText = true };

    static GUIStyle RichLabel() => _richLabel ??= new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };

    static GUIStyle ToggleButton() =>
        _toggleButton ??= new GUIStyle(GUI.skin.button) { richText = true, alignment = TextAnchor.MiddleLeft };
}
#endif
