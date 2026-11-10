#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 할 일 243 — 대시 관통 랩(<see cref="DebugDashPassLab"/>)의 어쌔신 Q(관통 돌진 → 그림자 회귀) 확인 패널·사거리 표시.
/// 게임 코드는 바꾸지 않는다 — 읽기 + 정식 경로만 쓴다.
/// <list type="bullet">
/// <item>어쌔신으로 = <see cref="DebugCharacterCycle.TrySwapToServer"/>(F1 순환과 같은 길).</item>
/// <item>그림자 회귀 = 특성 ASN_2_RETURN 을 <see cref="TraitDirector.ReplaceAcquiredServer"/> 로 넣고 뺀다(특성 랩과 같은 정식 경로).
///   랩 씬에는 디렉터가 없어 처음 켤 때 NetworkManager 프리팹 목록의 <c>TraitDirector</c> 를 서버가 스폰한다. 어쌔신이 아니면 비운다.</item>
/// <item>Q·E·R 쿨 0 = 랩 오브젝트의 <see cref="DebugZeroCooldown.Skills"/>.</item>
/// <item>사거리 표시(게임 화면, 라인) — 하늘색 원 = 플레이어 중심 반경 Q 돌진 거리(<see cref="AssassinDashStrikeSkillData.DashDistance"/>, 출발 Q 만).
///   회귀 창이 열려 있으면 출발점 표식 + 지금 위치 → 출발점 선(초록). 회귀는 사거리 제한이 없다(할 일 243 — 거리 상관없이 출발점까지, 벽만 예외).</item>
/// <item>마지막 Q/회귀 = 오너가 본 스킬 시작 → 끝 이동 거리·시간(실제 시간). 끝 = 클립 End 이벤트(또는 안전망).</item>
/// </list>
/// 호스트 1인 기준, 새 RPC 없음.
/// </summary>
internal sealed class DashPassLabAssassinQ
{
    private const string Tag = "[DashPassLab/AssassinQ]";
    private const int CircleSegments = 64;
    private const float LineY = 0.05f;

    private readonly MonoBehaviour host;
    private readonly DebugCharacterCycle characterCycle;
    private readonly DebugZeroCooldown zeroCooldown;
    private readonly List<int> hashScratch = new List<int>();

    private bool wantShadowReturn = true;
    private bool showRange = true;
    private string status = string.Empty;

    // 마지막 Q/회귀 측정(오너 화면).
    private PlayerSkillBase trackedSkill;
    private Vector3 trackedStart;
    private float trackedStartTime;
    private string lastMeasure = string.Empty;

    // 사거리 표시 — 런타임 라인(게임 화면에서 보이게).
    private GameObject drawRoot;
    private LineRenderer rangeCircle;
    private LineRenderer originLine;
    private LineRenderer originMark;

    public DashPassLabAssassinQ(MonoBehaviour host)
    {
        this.host = host;
        characterCycle = host.GetComponent<DebugCharacterCycle>();
        zeroCooldown = host.GetComponent<DebugZeroCooldown>();
    }

    public void Dispose()
    {
        if (drawRoot != null)
            Object.Destroy(drawRoot);
        drawRoot = null;
    }

    // ── 매 프레임 ──

    public void Tick()
    {
        Player player = Player.LocalPlayer;
        PlayerSkillController controller = player != null ? player.GetComponent<PlayerSkillController>() : null;
        AssassinDashStrikeSkill q = ResolveQ(controller);

        ReconcileShadowReturnServer(player, q != null);
        TrackMeasure(player, controller);
        UpdateDrawing(player, controller, q);
    }

    // 원래 Q(쿨 소유 스킬) — 후속(회귀)이 열려 있어도 원래 Q. 어쌔신이 아니면 null.
    private static AssassinDashStrikeSkill ResolveQ(PlayerSkillController controller)
    {
        if (controller == null)
            return null;

        return controller.GetCooldownOwnerSkill(PlayerSkillSlot.Main) as AssassinDashStrikeSkill;
    }

    // [호스트] 원하는 상태(체크 + 어쌔신)와 획득 목록이 다르면 맞춘다. 같으면 아무것도 안 한다.
    private void ReconcileShadowReturnServer(Player player, bool isAssassin)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening || !networkManager.IsServer || player == null || !player.IsSpawned)
            return;

        bool want = wantShadowReturn && isAssassin;
        TraitDirector director = TraitDirector.Instance;
        if (director == null)
        {
            if (want)
                SpawnTraitDirectorServer(networkManager);
            return;
        }

        if (!director.IsSpawned)
            return;

        int hash = TraitIdHash.Of(TraitModeIds.AssassinShadowReturn);
        director.GetAcquired(networkManager.LocalClientId, hashScratch);
        bool has = hashScratch.Contains(hash);
        if (has == want)
            return;

        hashScratch.Clear();
        if (want)
            hashScratch.Add(hash);

        bool ok = director.ReplaceAcquiredServer(networkManager.LocalClientId, hashScratch);
        status = ok ? (want ? "그림자 회귀 특성 넣음" : "그림자 회귀 특성 뺌") : "특성 교체 실패 — 콘솔 확인";
        Debug.Log($"{Tag} {status}", host);
    }

    private bool directorSpawnTried;

    private void SpawnTraitDirectorServer(NetworkManager networkManager)
    {
        if (directorSpawnTried)
            return;

        directorSpawnTried = true;
        foreach (NetworkPrefab entry in networkManager.NetworkConfig.Prefabs.Prefabs)
        {
            GameObject prefab = entry?.Prefab;
            if (prefab == null || prefab.GetComponent<TraitDirector>() == null)
                continue;

            GameObject go = Object.Instantiate(prefab);
            go.name = prefab.name + " (DashPassLab)";
            go.GetComponent<NetworkObject>().Spawn(true);
            status = "TraitDirector 스폰(그림자 회귀용)";
            Debug.Log($"{Tag} {status}", host);
            return;
        }

        status = "네트워크 프리팹 목록에 TraitDirector 없음 — 그림자 회귀 못 켬";
        Debug.LogError($"{Tag} {status}", host);
    }

    private void TrackMeasure(Player player, PlayerSkillController controller)
    {
        PlayerSkillBase active = controller != null ? controller.ActiveSkill : null;
        if (active is not AssassinDashStrikeSkill)
            active = null;

        if (active == trackedSkill)
            return;

        if (trackedSkill != null && player != null)
        {
            Vector3 delta = player.transform.position - trackedStart;
            delta.y = 0f;
            string what = trackedSkill is AssassinShadowReturnSkill ? "회귀" : "Q";
            lastMeasure = $"마지막 {what}: 이동 {delta.magnitude:F2}m · 시작→끝 {Time.unscaledTime - trackedStartTime:F2}s";
            Debug.Log($"{Tag} {lastMeasure}", host);
        }

        trackedSkill = active;
        if (active != null && player != null)
        {
            trackedStart = player.transform.position;
            trackedStartTime = Time.unscaledTime;
        }
    }

    // ── 패널 ──

    public void DrawGui(bool server)
    {
        GUILayout.Space(4f);
        GUILayout.Label("── 243 어쌔신 Q (관통 돌진 → 그림자 회귀) ──");

        Player player = Player.LocalPlayer;
        PlayerSkillController controller = player != null ? player.GetComponent<PlayerSkillController>() : null;
        AssassinDashStrikeSkill q = ResolveQ(controller);

        GUI.enabled = server && characterCycle != null;
        if (q == null && GUILayout.Button("어쌔신으로 바꾸기 (F1 순환과 같음)"))
            SwapToAssassin();
        GUI.enabled = true;

        bool want = GUILayout.Toggle(wantShadowReturn, " 그림자 회귀 특성(ASN_2_RETURN) 넣기 — Q 다시 누르면 출발점으로");
        if (want != wantShadowReturn)
            wantShadowReturn = want;

        if (zeroCooldown != null)
        {
            GUI.enabled = server;
            bool zero = GUILayout.Toggle(zeroCooldown.Skills, " Q·E·R 쿨 0");
            if (zero != zeroCooldown.Skills)
                zeroCooldown.Skills = zero;
            GUI.enabled = true;
        }

        showRange = GUILayout.Toggle(showRange, " 사거리 표시(하늘색 원 = Q 거리 · 초록 선 = 회귀 경로)");

        if (q != null)
        {
            AssassinDashStrikeSkillData data = q.DashData;
            bool transformed = data is AssassinTransformedDashStrikeSkillData;
            if (data != null)
                GUILayout.Label($"{(transformed ? "변신" : "일반")} Q: 거리 {data.DashDistance:0.##}m · 속도 {data.DashSpeed:0.##}m/s · 쿨 {data.CooldownTime:0.##}s(데이터)");

            float remaining = controller.GetCooldownRemaining(q);
            bool open = controller.IsFollowUpOpen(PlayerSkillSlot.Main);
            string returnText = open ? "회귀 창 열림" : "회귀 창 닫힘";
            if (open && data != null && q.LocalReturnOrigin.TryPeek(out Vector3 origin))
            {
                float toOrigin = Flat(origin - player.transform.position).magnitude;
                returnText += $" · 출발점까지 {toOrigin:F2}m(회귀 = 전부, 사거리 제한 없음)";
            }

            GUILayout.Label($"쿨 남음 {remaining:F1}s · {returnText}");
        }

        if (!string.IsNullOrEmpty(lastMeasure))
            GUILayout.Label(lastMeasure);
        if (!string.IsNullOrEmpty(status))
            GUILayout.Label(status);
    }

    private void SwapToAssassin()
    {
        int id = DebugCharacterCycle.FindCharacterId(characterCycle.Roster, TutorialCharacter.Assassin);
        if (id < 0)
        {
            status = "로스터에 어쌔신 없음";
            return;
        }

        if (!characterCycle.TrySwapToServer(id))
            status = "캐릭터 변경 안 됨(연타 간격·프리팹 — 콘솔)";
    }

    // ── 사거리 라인 ──

    private void UpdateDrawing(Player player, PlayerSkillController controller, AssassinDashStrikeSkill q)
    {
        AssassinDashStrikeSkillData data = q != null ? q.DashData : null;
        bool visible = showRange && player != null && data != null;
        if (!visible)
        {
            if (drawRoot != null)
                drawRoot.SetActive(false);
            return;
        }

        EnsureDrawing();
        drawRoot.SetActive(true);

        Vector3 center = player.transform.position;
        SetCircle(rangeCircle, center, data.DashDistance);

        Vector3 origin = default;
        bool hasOrigin = controller.IsFollowUpOpen(PlayerSkillSlot.Main) && q.LocalReturnOrigin.TryPeek(out origin);
        originLine.enabled = hasOrigin;
        originMark.enabled = hasOrigin;
        if (!hasOrigin)
            return;

        Color color = new Color(0.3f, 1f, 0.4f); // 회귀는 거리 제한 없음(243) — 항상 출발점까지(벽만 예외)
        originLine.startColor = originLine.endColor = color;
        originMark.startColor = originMark.endColor = color;
        originLine.SetPosition(0, Ground(center));
        originLine.SetPosition(1, Ground(origin));
        SetCircle(originMark, origin, 0.4f);
    }

    private void EnsureDrawing()
    {
        if (drawRoot != null)
            return;

        drawRoot = new GameObject("AssassinQ Range (DashPassLab)");
        Material material = CreateLineMaterial();
        rangeCircle = CreateLine("Q range", material, new Color(0.3f, 0.85f, 1f), CircleSegments, true);
        originLine = CreateLine("Return line", material, Color.green, 2, false);
        originMark = CreateLine("Return origin", material, Color.green, 24, true);
    }

    private static Material CreateLineMaterial()
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        return new Material(shader);
    }

    private LineRenderer CreateLine(string name, Material material, Color color, int points, bool loop)
    {
        var go = new GameObject(name);
        go.transform.SetParent(drawRoot.transform, false);
        LineRenderer line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.useWorldSpace = true;
        line.loop = loop;
        line.positionCount = points;
        line.widthMultiplier = 0.06f;
        line.startColor = line.endColor = color;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }

    private static void SetCircle(LineRenderer line, Vector3 center, float radius)
    {
        int count = line.positionCount;
        for (int i = 0; i < count; i++)
        {
            float angle = i * Mathf.PI * 2f / count;
            line.SetPosition(i, Ground(center) + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius);
        }
    }

    private static Vector3 Ground(Vector3 p) => new Vector3(p.x, p.y + LineY, p.z);

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v;
    }
}
#endif
