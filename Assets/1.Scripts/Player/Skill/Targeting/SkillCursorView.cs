using UnityEngine;

/// <summary>
/// 마우스 상태별 커서 아이콘 교체 훅. PlayerSkillTargeting이 상태 변화 시 ApplyState를 호출한다.
/// 기본 커서는 캐릭터 Variant(Player_Paladin·Player_Gunner)가 defaultIcon을 오버라이드한다(50.Art/UI/Cursor).
/// 텍스처가 빈 상태는 시스템 커서로 둔다.
/// Windows 빌드는 <see cref="WindowsNativeCursor"/>(하드웨어 커서, 배율 적용·지연 없음, 할 일 250)로 그리고,
/// 에디터·MPPM·그 밖의 플랫폼(또는 네이티브 실패 시)은 <c>Cursor.SetCursor</c> + 아래 모드 칸을 쓴다.
/// </summary>
public class SkillCursorView : MonoBehaviour
{
    [System.Serializable]
    private struct CursorIcon
    {
        public Texture2D texture;
        public Vector2 hotspot;
    }

    [Tooltip("각 상태의 커서 텍스처. 비워두면 해당 상태에서 커서를 바꾸지 않는다(현재 전부 비어 있어 no-op).")]
    [SerializeField] private CursorIcon defaultIcon;
    [SerializeField] private CursorIcon targetingIcon;
    [SerializeField] private CursorIcon validTargetIcon;
    [SerializeField] private CursorIcon invalidTargetIcon;
    [SerializeField] private CursorIcon outOfRangeIcon;

    [Tooltip("커서 표시 배율(10-07 은희 — 1080 기준 1.2배). 원본 텍스처를 이 배율로 다시 그려 쓴다. 1 = 원본 그대로.\n" +
             "Windows 빌드 = 모든 상태에 네이티브 하드웨어 커서로 적용(지연 없음). 에디터·그 밖 = 아래 모드가 ForceSoftware 일 때만 적용.")]
    [SerializeField, Min(0.25f)] private float cursorScale = 1.2f;

    [Tooltip("화면 높이에 비례해 키운다 — 기준 높이(1080)에서 cursorScale, 1440 이면 ×1.33. 0 = 해상도 무관.")]
    [SerializeField, Min(0f)] private float referenceScreenHeight = 1080f;

    // 10-07 은희: 지금은 둘 다 하드웨어(Auto) — 반응 즉시, 크기는 Windows 시스템 커서 크기(배율 무시).
    // 소프트웨어(ForceSoftware)로 바꾸면 배율·해상도 비례가 먹지만 렌더 프레임만큼 늦게 따라온다(에디터에서 특히 느림).
    // 11-09(250): Windows 빌드는 이 두 칸을 안 쓴다 — 네이티브 하드웨어 커서로 배율까지 지연 없이 그린다.
    [Tooltip("기본(idle) 커서 모드 — 에디터·MPPM·비 Windows 전용(Windows 빌드는 네이티브 커서). Auto = 하드웨어(지연 없음, 배율 무시).")]
    [SerializeField] private CursorMode defaultCursorMode = CursorMode.Auto;
    [Tooltip("조준 커서 모드 — 에디터·MPPM·비 Windows 전용(Windows 빌드는 네이티브 커서). ForceSoftware = 배율 적용(렌더 지연 있음). Auto 는 Windows 에서 시스템 크기로 줄여 그린다.")]
    [SerializeField] private CursorMode targetingCursorMode = CursorMode.Auto;

    private Player player;
    private bool isLocal;

    // Windows 빌드 네이티브 커서 캐시(원본 텍스처 → HCURSOR). 배율(해상도)이 바뀌면 지우고 다시 만든다.
    private readonly System.Collections.Generic.Dictionary<Texture2D, System.IntPtr> nativeCursors =
        new System.Collections.Generic.Dictionary<Texture2D, System.IntPtr>();
    private float nativeScale = -1f;

    // 배율 적용본 캐시(원본 텍스처 → 확대본). 배율(해상도)이 바뀌면 비운다.
    private readonly System.Collections.Generic.Dictionary<Texture2D, Texture2D> scaled =
        new System.Collections.Generic.Dictionary<Texture2D, Texture2D>();
    private float cachedScale = -1f;

    private float EffectiveScale =>
        cursorScale * (referenceScreenHeight > 0f ? Screen.height / referenceScreenHeight : 1f);

    // 기본 커서는 캐릭터 Variant마다 다르다 — 조준을 한 번도 안 해도 로컬 플레이어가 되는 순간 적용하고,
    // 로컬에서 빠지면(디스폰·캐릭터 교체 전 해제) 시스템 커서로 되돌린다.
    private void Awake()
    {
        player = GetComponentInParent<Player>();
        Player.LocalPlayerChanged += HandleLocalPlayerChanged;
    }

    private void OnDestroy()
    {
        Player.LocalPlayerChanged -= HandleLocalPlayerChanged;
        if (isLocal)
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);

        ReleaseNative();
        ReleaseScaled();
    }

    // 네이티브 커서는 Unity 가 모르는 상태다 — 숨김(설정창 등 Cursor.visible=false)이면 WndProc 가 Unity 처리에 넘기도록 매 프레임 알린다.
    private void LateUpdate()
    {
        if (isLocal && WindowsNativeCursor.IsSupported)
            WindowsNativeCursor.SyncVisible(Cursor.visible);
    }

    private void ReleaseNative()
    {
        foreach (System.IntPtr handle in nativeCursors.Values)
            WindowsNativeCursor.Destroy(handle); // 지금 쓰는 커서면 원래 WndProc 복원까지
        nativeCursors.Clear();
    }

    private void ReleaseScaled()
    {
        foreach (Texture2D texture in scaled.Values)
        {
            if (texture != null)
                Destroy(texture);
        }
        scaled.Clear();
    }

    private void HandleLocalPlayerChanged(Player localPlayer)
    {
        if (player != null && localPlayer == player)
        {
            isLocal = true;
            ApplyState(SkillCursorState.Default);
        }
        else if (isLocal)
        {
            isLocal = false;
            ReleaseNative();
            if (localPlayer == null)
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }
    }

    public void ApplyState(SkillCursorState state)
    {
        CursorIcon icon = Resolve(state);

        // 텍스처 미배선이면 기본 시스템 커서 유지 (훅만 존재하는 현 단계의 의도된 동작)
        if (icon.texture == null)
        {
            WindowsNativeCursor.Show(System.IntPtr.Zero);
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            return;
        }

        // Windows 빌드: 하드웨어 커서에 배율까지 — 실패(창·서브클래싱·생성)하면 아래 기존 경로로.
        if (WindowsNativeCursor.IsSupported && TryShowNative(icon))
            return;

        CursorMode mode = state == SkillCursorState.Default ? defaultCursorMode : targetingCursorMode;
        // 하드웨어 커서는 배율이 무시되므로 원본 그대로 넘긴다(확대본을 만들 이유가 없다).
        float scale = mode == CursorMode.ForceSoftware ? EffectiveScale : 1f;
        Cursor.SetCursor(Scaled(icon.texture, scale), icon.hotspot * scale, mode);
    }

    private bool TryShowNative(CursorIcon icon)
    {
        float scale = EffectiveScale;
        if (!Mathf.Approximately(scale, nativeScale))
        {
            ReleaseNative();
            nativeScale = scale;
        }

        if (!nativeCursors.TryGetValue(icon.texture, out System.IntPtr handle))
        {
            // 픽셀을 읽어야 하므로 배율 1 이어도 읽기 가능한 사본을 만든다.
            Texture2D pixels = Scaled(icon.texture, scale, forceCopy: true);
            handle = WindowsNativeCursor.Create(
                NativeCursorPixels.ToTopDownBgra(pixels.GetPixels32(), pixels.width, pixels.height),
                pixels.width, pixels.height,
                NativeCursorPixels.ScaledHotspot(icon.hotspot, scale, pixels.width, pixels.height));
            if (handle == System.IntPtr.Zero)
                return false;
            nativeCursors[icon.texture] = handle;
        }

        // Unity 쪽 커서를 먼저 비워 둔다 — 대체 경로에서 남은 텍스처를 Unity 가 다시 세우지 않게.
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        return WindowsNativeCursor.Show(handle);
    }

    // 커서는 텍스처 픽셀 크기 그대로 그려진다 — 키우려면 큰 텍스처가 필요하다.
    // 원본이 읽기 불가(Cursor 임포트)여도 되도록 GPU 에서 RenderTexture 로 늘린 뒤 읽어 온다.
    // forceCopy = 배율 1 이어도 읽기 가능한 사본(네이티브 커서용 픽셀).
    private Texture2D Scaled(Texture2D source, float scale, bool forceCopy = false)
    {
        // 하드웨어(배율 1)와 조준(배율 s)을 오가도 확대본 캐시는 유지 — 해상도가 바뀌어 s 가 달라질 때만 비운다.
        if (!forceCopy && Mathf.Approximately(scale, 1f))
            return source;
        if (!Mathf.Approximately(scale, cachedScale))
        {
            ReleaseScaled();
            cachedScale = scale;
        }
        if (scaled.TryGetValue(source, out Texture2D cached) && cached != null)
            return cached;

        int width = Mathf.Max(1, Mathf.RoundToInt(source.width * scale));
        int height = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));
        RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture previous = RenderTexture.active;
        Graphics.Blit(source, rt);
        RenderTexture.active = rt;

        var result = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = source.name + "_x" + scale.ToString("0.##") };
#if UNITY_EDITOR
        result.alphaIsTransparency = true; // 에디터 전용 API — 플레이어 빌드에선 컴파일되지 않는다
#endif
        result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        result.Apply(false, false);

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);
        scaled[source] = result;
        return result;
    }

    // 상태별 아이콘이 비어 있으면 targeting → default 순으로 폴백한다.
    // 커서를 2개(기본/조준)만 배선해도 조준 중 상태가 바뀔 때 시스템 커서로 깜빡이지 않는다.
    private CursorIcon Resolve(SkillCursorState state)
    {
        return state switch
        {
            SkillCursorState.Targeting => FirstAssigned(targetingIcon, defaultIcon),
            SkillCursorState.ValidTarget => FirstAssigned(validTargetIcon, targetingIcon, defaultIcon),
            SkillCursorState.InvalidTarget => FirstAssigned(invalidTargetIcon, targetingIcon, defaultIcon),
            SkillCursorState.OutOfRange => FirstAssigned(outOfRangeIcon, targetingIcon, defaultIcon),
            _ => defaultIcon
        };
    }

    private static CursorIcon FirstAssigned(params CursorIcon[] icons)
    {
        for (int i = 0; i < icons.Length; i++)
        {
            if (icons[i].texture != null)
                return icons[i];
        }

        return default;
    }
}
