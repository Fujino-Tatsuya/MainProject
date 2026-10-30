using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VeyTrace.UI;

/// <summary>
/// 화면 상단 보스 체력바. BossHudTarget 등록 목록을 매 프레임 폴링해
/// 먼저 스폰된 생존 보스의 HP를 표시하고, 보스가 없으면 바 전체를 숨긴다.
/// (복수 보스 동시 표시는 필요 시 확장)
/// </summary>
public class BossHealthHUD : MonoBehaviour
{
    [SerializeField] private GameObject barRoot;
    [SerializeField] private Image hpFill;
    [Header("제압 칸(전투 시스템 명세 10-14 §6 — 예전 간파 게이지 Detection_Fill 자리)")]
    [Tooltip("HP 바 밑 **제압 칸** 한 칸의 원본(SuppressSegmentTemplate) = 칸 채움(fill) 한 장(frame 없음). 자신은 꺼 두고 칸 수만큼 복제한다.\n" +
             "칸 줄 자리·폭·높이 = 아래 슬롯 이미지 아래칸 영역(할 일 151 후속, 10-30). 칸 폭 = (줄 폭 − 간격×(N−1)) / N, 정수 픽셀.\n" +
             "이 원본의 크기·자리는 런타임에 덮어쓴다(스프라이트·색·모서리 배율만 쓴다). 슬롯 이미지(suppressRowSlot)의 **직속 자식**·앵커 가운데로 둔다.\n" +
             "남은 칸 = 복제본 켬, 빈 칸 = 복제본 끔(슬롯 아래칸 어두운 바탕이 보인다). 왼쪽부터 찬 칸, 줄어들면 HP 와 같은 방향(오른쪽부터) 빈 칸이 된다.\n" +
             "늘어나도 안 찌그러지게 9-슬라이스(Image Sliced) 스프라이트를 쓴다. 제압 칸이 없는 보스면 칸 줄 전체를 숨긴다.")]
    [SerializeField] private Graphic suppressSegmentTemplate;
    [Tooltip("칸 사이 간격(px).")]
    [SerializeField, Min(0f)] private float suppressSegmentGap = 8f;
    [Tooltip("칸 줄이 겹칠 보스 정보 슬롯 이미지(BossInfoSlot, slot_boss_info). Type Simple·Preserve Aspect 끔(이미지를 사각형에 꽉 늘려 그림)이어야 영역이 맞는다.")]
    [SerializeField] private Image suppressRowSlot;
    [Tooltip("슬롯 이미지 **원본 픽셀** 기준 칸 줄 영역(왼쪽 위 원점, X·Y·W·H). 지금 = 아래칸 바깥 테두리 57,45 / 812×16(926×61 이미지).\n" +
             "슬롯 사각형 크기 ÷ 이미지 픽셀로 늘려 HUD 좌표로 바꾼다 — 슬롯 크기를 바꿔도 칸 줄이 아래칸에 붙어 따라간다.")]
    [SerializeField] private RectInt suppressRowSlotPixels = new RectInt(57, 45, 812, 16);
    [SerializeField] private DelayedHealthBar delayed = new DelayedHealthBar();

    [Header("23호 레이드 ④ 자폭 실드(팀장 10-19)")]
    [Tooltip("HP 바 위를 덮는 실드 바(프리팹의 SelfDestructShieldFill — HP_Fill 바로 다음 형제). 비우면 실드를 표시하지 않는다.\n" +
             "자폭 시작 때 바 전체를 덮고, 실드가 깎일수록 오른쪽부터 줄어든다. 자폭이 아니면 숨긴다.")]
    [SerializeField] private Image shieldFill;
    [Tooltip("실드 바 색. Awake 에서 shieldFill 에 적용한다(색 원본은 이 필드).")]
    [SerializeField] private Color shieldColor = new Color(0.55f, 0.85f, 1f, 0.65f);

    [Header("HP 색(경석 Play 6차 — 빨강→주황 그라데이션)")]
    [Tooltip("켜면 HP_Fill 을 바 모양 마스크로만 쓰고 그 안에 아래 그라데이션을 런타임 생성해 그린다(스프라이트 원래 색 대신).\n" +
             "스프라이트가 이미 색이 칠해진 그라데이션이라 Image.color(곱하기)로는 주황을 못 만든다. 끄면 스프라이트 색 그대로.")]
    [SerializeField] private bool hpGradientEnabled = true;
    [Tooltip("왼쪽(0) → 오른쪽(1) HP 색. 바 전체 길이 기준이라 HP 가 줄면 앞쪽 주황부터 사라진다.")]
    [SerializeField] private Gradient hpGradient = DefaultHpGradient();

    [Header("등장 차오름(경석 10-23 — Shape of Dreams 식: 흰 발광 덩어리가 앞서 달리고 빨강이 뒤따른다)")]
    [Tooltip("바가 처음 보일 때 이 비율에서 실제 HP 까지 채운다. 끄면 처음부터 실제 값.")]
    [SerializeField] private bool introFillEnabled = true;
    [Tooltip("빨강이 차오르기 시작하는 비율(0~1). 멈춤 없이 바로 달린다(경석 Play 6차 — 반에서 끊겼다 차오르는 게 거슬렸다).")]
    [SerializeField, Range(0f, 1f)] private float introFillStart = 0.45f;
    [Tooltip("흰 구간이 빨강보다 앞서 출발하는 비율 — 바가 보이는 첫 프레임부터 이미 흰 덩어리가 앞서 있게.")]
    [SerializeField, Range(0f, 0.5f)] private float introLeadStartGap = 0.08f;
    [Tooltip("흰 구간이 끝까지 달려가는 시간(초) — 빨강보다 짧게.")]
    [SerializeField, Min(0.05f)] private float introLeadSeconds = 0.55f;
    [Tooltip("빨간 바가 끝까지 차오르는 시간(초).")]
    [SerializeField, Min(0.05f)] private float introFillSeconds = 1.15f;
    [Tooltip("빨강이 다 찬 뒤 흰 구간·발광이 사그라드는 시간(초).")]
    [SerializeField, Min(0f)] private float introFadeSeconds = 0.4f;
    [Tooltip("흰 구간 색 — 바 안쪽 흰 띠 전체가 이 색으로 고르게 빛난다.")]
    [SerializeField] private Color introLeadColor = Color.white;
    [Tooltip("흰 구간에서 번지는 빛 색 — 바 모양 안에서만 보인다(빨강 끝·빈 칸 쪽으로 퍼진다).")]
    [SerializeField] private Color introGlowColor = new Color(1f, 0.96f, 0.9f, 0.7f);
    [Tooltip("흰 구간 밖으로 빛이 번지는 폭(px). 바 높이 28 기준.")]
    [SerializeField, Min(1f)] private float introGlowSpread = 14f;

    [Header("무적 잠김(할 일 147 — 보스가 피해를 안 받는 동안 바를 밝은 회색 + 자물쇠)")]
    [Tooltip("바 가운데 자물쇠 아이콘(프리팹의 HP_LockIcon). 비우면 색만 바뀐다.")]
    [SerializeField] private Image hpLockIcon;
    [Tooltip("무적 동안 HP 채움 색. HP_Fill 모양 안에 이 색 덮개를 런타임 생성해 덮는다(스프라이트가 색이 칠해져 곱하기로는 회색이 안 나온다).")]
    [SerializeField] private Color hpLockColor = new Color(0.902f, 0.902f, 0.902f, 1f);   // #E6E6E6
    [Tooltip("무적이 풀릴 때 회색·자물쇠가 걷히는 시간(초). 0 = 즉시. 최대 0.2초.")]
    [SerializeField, Range(0f, BossHpLockDisplay.MaxReleaseSeconds)] private float hpLockReleaseSeconds = 0.15f;
    [Tooltip("해제 끝에서 자물쇠가 커지는 정도(0.3 = 1.3배까지 커지며 사라진다).")]
    [SerializeField, Min(0f)] private float hpLockReleasePop = 0.3f;

    private readonly BossHpLockDisplay hpLock = new BossHpLockDisplay();
    private Image hpLockTint;

    private readonly BossHealthIntroFill introFill = new BossHealthIntroFill();
    private Unit introPlayedFor;              // 이 보스로 차오름을 한 번 했다(재도전 = 새 보스 인스턴스라 다시 한다)
    private bool wasShown;

    private Unit boundBoss;
    private TwentyThreeBoss boundGaugeBoss;   // 제압 칸을 가진 보스(23호)일 때만

    private readonly List<GameObject> suppressSegments = new List<GameObject>();

    private void Awake()
    {
        if (suppressSegmentTemplate != null)
        {
            suppressSegmentTemplate.gameObject.SetActive(false);   // 원본은 그리지 않는다 — 복제본만 보인다
            if (suppressRowSlot == null || suppressRowSlot.sprite == null)
                Debug.LogWarning($"[BossHealthHUD] suppressRowSlot(슬롯 이미지)이 비어 있어 제압 칸을 그리지 않는다: {name}", this);
            else if (suppressSegmentTemplate.transform.parent != suppressRowSlot.transform)
                Debug.LogWarning($"[BossHealthHUD] 제압 칸 원본이 슬롯 이미지의 직속 자식이 아니다 — 칸 줄이 아래칸에서 어긋난다: {name}", this);
        }

        if (shieldFill != null)
            shieldFill.color = shieldColor;
        else
            Debug.LogWarning($"[BossHealthHUD] shieldFill 이 비어 있어 자폭 실드를 표시하지 않는다: {name}", this);

        if (hpGradientEnabled)
            BuildHpGradient();

        BuildHpLockTint();
        ApplyHpLock(default);
    }

    private void OnDestroy()
    {
        for (int i = 0; i < generatedSprites.Count; i++)
        {
            if (generatedSprites[i] == null) continue;
            Destroy(generatedSprites[i].texture);
            Destroy(generatedSprites[i]);
        }
        generatedSprites.Clear();
    }

    private void Update()
    {
        Unit boss = FindBoss();
        if (boss != boundBoss)
            BindBoss(boss);

        // 등장 시네마틱 워크인·강하·착지 동안은 숨긴다 — 웰즈 지시 구간부터 보인다(PLAN-boss-encounter-cutscene 정정 8).
        BossEncounterDirector director = BossEncounterDirector.Instance;
        bool introHides = director != null && director.IsSpawned && director.HidesBossHud;
        bool shouldShow = boss != null && boss.CurrentHealth > 0 && !introHides;
        if (barRoot != null && barRoot.activeSelf != shouldShow)
            barRoot.SetActive(shouldShow);

        if (!shouldShow)
        {
            wasShown = false;
            introFill.Cancel();
            SetIntroLead(false, 0f, 0f, 0f);
            hpLock.Reset();
            ApplyHpLock(default);
            return;
        }

        // 처음 보이는 순간(등장 연출이 끝나 바가 뜰 때) 한 번 — 멈춤 없이 흰 발광이 앞서 달리고 빨강이 뒤따라 최대까지.
        if (!wasShown && introFillEnabled && introPlayedFor != boss)
        {
            introPlayedFor = boss;
            introFill.Begin();
            Edit.Log($"[BossHealthHUD] 등장 차오름 시작 — 빨강 {introFillStart:0.##}·흰 구간 +{introLeadStartGap:0.##} 에서 바로 출발, 흰 구간 {introLeadSeconds:0.##}초·빨강 {introFillSeconds:0.##}초.", this);
        }
        wasShown = true;

        int maxHp = boss.FinalMaxHp;
        float actual01 = maxHp > 0 ? Mathf.Clamp01((float)boss.CurrentHealth / maxHp) : 0f;
        BossHealthIntroFill.Frame intro = introFill.Evaluate(actual01, Time.unscaledDeltaTime,
            introFillStart, introLeadSeconds, introFillSeconds, introFadeSeconds, introLeadStartGap);
        if (hpFill != null)
            hpFill.fillAmount = intro.Red;

        delayed.Tick(Time.deltaTime, boss.CurrentHealth, maxHp);
        // 차오르는 동안: 흰 구간(오버레이) = 빨강 뒤에서 앞서 달린다 · 잔상 바는 빨강 뒤에 숨긴다(회색이 흰색 너머로 비치지 않게).
        if (intro.Active)
            delayed.ForceFill(intro.Red);
        SetIntroLead(intro.Active, intro.Red, intro.White, intro.WhiteAlpha);

        // 복제값 — 늦은 합류도 현재 칸.
        if (boundGaugeBoss != null)
            SetSuppressSegments(boundGaugeBoss.SuppressSegmentTotal, boundGaugeBoss.SuppressSegmentsRemaining);
        else
            SetSuppressSegments(0, 0);

        // ④ 자폭 실드 — 복제 비율(SelfDestructShield01)을 HP 바 위 덮개로.
        float shield01 = boundGaugeBoss != null ? boundGaugeBoss.SelfDestructShield01 : 0f;
        if (shieldFill != null)
        {
            bool show = shield01 > 0f;
            if (shieldFill.gameObject.activeSelf != show) shieldFill.gameObject.SetActive(show);
            if (show) shieldFill.fillAmount = Mathf.Clamp01(shield01);
        }

        // 무적 잠김 — 복제값(HpLocked). 웰즈는 23호 등에 탄 장식이라 자기 HP·무적이 없다 → 23호 값 하나로 충분.
        bool locked = boundGaugeBoss != null && boundGaugeBoss.HpLocked;
        ApplyHpLock(hpLock.Evaluate(locked, Time.unscaledDeltaTime, hpLockReleaseSeconds, hpLockReleasePop));
    }

    // ── 무적 잠김 ──────────────────────────────────────────────────────────
    // HP_Fill 은 그라데이션 Mask 다(BuildHpGradient) — 그 안 맨 위에 단색 덮개를 깔아 채움 모양 그대로 회색으로 덮는다.
    // 그라데이션을 끈 경우에도 덮개가 바 모양 밖으로 안 나가게 Mask 를 붙이되 원래 스프라이트는 그대로 그린다.
    private void BuildHpLockTint()
    {
        if (hpFill == null) return;

        Mask mask = hpFill.GetComponent<Mask>();
        if (mask == null)
        {
            mask = hpFill.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = true;
        }

        hpLockTint = NewImage("HP_LockTint", hpFill.rectTransform, null);   // 마지막 자식 = HP_Gradient 위
        hpLockTint.gameObject.SetActive(false);
    }

    private void ApplyHpLock(BossHpLockDisplay.Frame frame)
    {
        bool tint = frame.Tint01 > 0f;
        if (hpLockTint != null)
        {
            if (hpLockTint.gameObject.activeSelf != tint) hpLockTint.gameObject.SetActive(tint);
            if (tint) hpLockTint.color = WithAlpha(hpLockColor, hpLockColor.a * frame.Tint01);
        }

        if (hpLockIcon != null)
        {
            if (hpLockIcon.gameObject.activeSelf != frame.ShowLock) hpLockIcon.gameObject.SetActive(frame.ShowLock);
            if (frame.ShowLock)
            {
                hpLockIcon.color = WithAlpha(hpLockIcon.color, frame.LockAlpha);
                hpLockIcon.rectTransform.localScale = Vector3.one * frame.LockScale;
            }
        }
    }

    private void OnDisable()
    {
        BindBoss(null);
    }

    // ── 제압 칸 ──────────────────────────────────────────────────────────
    // 원본(fill 한 장)을 total 개 복제해 슬롯 이미지 아래칸 영역에 가로 한 줄로 깐다. 영역 폭을 N등분하고 높이·가운데는 영역 그대로
    // (9-슬라이스라 모서리 유지). 칸 수가 바뀔 때만 다시 만든다. 남은 칸 = 켬, 빈 칸 = 끔(슬롯 아래칸 바탕이 보인다).
    private void SetSuppressSegments(int total, int remaining)
    {
        if (suppressSegmentTemplate == null) return;
        if (suppressSegments.Count != total)
            BuildSuppressSegments(total);

        for (int i = 0; i < suppressSegments.Count; i++)
        {
            GameObject seg = suppressSegments[i];
            bool filled = BossSuppressCellLayout.IsFilled(i, remaining);
            if (seg != null && seg.activeSelf != filled) seg.SetActive(filled);
        }
    }

    private void BuildSuppressSegments(int total)
    {
        for (int i = 0; i < suppressSegments.Count; i++)
            if (suppressSegments[i] != null) Destroy(suppressSegments[i]);
        suppressSegments.Clear();
        if (total <= 0 || suppressRowSlot == null || suppressRowSlot.sprite == null) return;

        RectTransform src = suppressSegmentTemplate.rectTransform;
        BossSuppressCellLayout.RowArea row = SuppressRowArea();
        int sibling = src.GetSiblingIndex();

        for (int i = 0; i < total; i++)
        {
            Graphic seg = Instantiate(suppressSegmentTemplate, src.parent, false);
            seg.name = $"SuppressSegment_{i}";
            var rect = seg.rectTransform;
            BossSuppressCellLayout.CellSpan span = BossSuppressCellLayout.PixelCell(i, total, row.Width, suppressSegmentGap);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);   // 슬롯 사각형 가운데 기준
            rect.sizeDelta = new Vector2(span.Width, row.PixelHeight);
            rect.anchoredPosition = new Vector2(row.CenterX + span.CenterX, row.CenterY);
            rect.SetSiblingIndex(sibling + 1 + i);   // 원본 자리(그리는 순서) 그대로
            seg.gameObject.SetActive(true);
            suppressSegments.Add(seg.gameObject);
        }
    }

    // 슬롯 이미지 픽셀 영역 → 슬롯 사각형 가운데 기준 HUD 좌표. 배율 = 슬롯 사각형 크기 ÷ 스프라이트 픽셀(가로·세로 따로).
    private BossSuppressCellLayout.RowArea SuppressRowArea()
    {
        Rect image = suppressRowSlot.sprite.rect;
        Rect slot = suppressRowSlot.rectTransform.rect;
        RectInt px = suppressRowSlotPixels;
        return BossSuppressCellLayout.SlotPixelsToRow(image.width, image.height, slot.width, slot.height,
            px.x, px.y, px.width, px.height);
    }

    // ── HP 그라데이션 ──────────────────────────────────────────────────────
    // HP_Fill 스프라이트는 색이 칠해진 그라데이션이라 Image.color(곱하기)로 주황을 못 만든다 → HP_Fill 자체를 Mask 로 쓰고(채움 모양 그대로,
    // 그리지는 않음) 그 안에 바 전체 크기의 그라데이션 Image 를 깐다. fillAmount 는 지금처럼 HP_Fill 에 넣으면 마스크가 같이 줄어든다.
    // Mask 는 스프라이트 알파로 자른다(uGUI StencilMaterial 이 UNITY_UI_ALPHACLIP 을 켠다) — 둥근 끝 모양이 유지된다. 런타임 생성(프리팹 무수정).
    private readonly List<Sprite> generatedSprites = new List<Sprite>();

    private void BuildHpGradient()
    {
        if (hpFill == null || hpGradient == null) return;

        Mask mask = hpFill.GetComponent<Mask>();
        if (mask == null) mask = hpFill.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        var go = new GameObject("HP_Gradient", typeof(RectTransform), typeof(Image));
        var rect = (RectTransform)go.transform;
        rect.SetParent(hpFill.rectTransform, false);
        Stretch(rect);
        var image = go.GetComponent<Image>();
        image.raycastTarget = false;
        image.type = Image.Type.Simple;
        image.sprite = MakeSprite(256, 32, (u, v) =>
        {
            Color c = hpGradient.Evaluate(u);
            // 위가 밝고 아래가 어두운 입체감 + 위쪽 얇은 하이라이트 줄.
            float shade = Mathf.Lerp(0.68f, 1f, Mathf.SmoothStep(0f, 1f, v / 0.65f));
            float d = (v - 0.78f) / 0.09f;
            float highlight = Mathf.Exp(-d * d) * 0.22f;
            Color shaded = new Color(c.r * shade, c.g * shade, c.b * shade, 1f);
            return Color.Lerp(shaded, Color.white, highlight);
        });
    }

    private static Gradient DefaultHpGradient()
    {
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.55f, 0.04f, 0.05f), 0f),
                new GradientColorKey(new Color(0.86f, 0.12f, 0.07f), 0.5f),
                new GradientColorKey(new Color(1f, 0.52f, 0.14f), 1f),
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }

    // ── 등장 차오름 흰 발광 ────────────────────────────────────────────────
    // 🔴 잔상 바 색을 흰색으로 바꾸는 방식은 안 된다 — HP_Fill·HP_DelayedFill 이 같은 빨간 그라데이션 스프라이트라 Image.color(곱하기)를
    //    흰색으로 둬도 스프라이트 원래 색(빨강)이 나온다(경석 Play 10-23: 흰색이 안 보였다). → 런타임 생성(프리팹 무수정) 두 겹:
    //    ① 바 안 흰 띠(IntroLeadMask — 바 모양 Mask, HP_Fill 바로 뒤): 빨강 끝 ~ 흰 구간 끝, 전체가 고른 흰색.
    //    ② 흰 띠 발광(IntroGlow — HP_Fill 바로 앞, 바 모양 Mask): 흰 띠에서 번지는 빛(9-슬라이스 소프트 사각형), 바 모양 밖은 잘린다.
    //    🔴 경석 Play 6차 정정: "흰 구간 **자체**가 빛나야 한다" — 오른쪽 끝 반짝임·머리 글로우는 뺐다(끝에만 빛이 몰리면 안 된다).
    private RectTransform introLeadMaskRect;
    private RectTransform introLeadRect;
    private Image introLeadImage;
    private RectTransform introGlowRect;
    private Image introGlowImage;
    private bool introLeadBuilt;

    private void SetIntroLead(bool active, float red01, float white01, float alpha)
    {
        if (!active)
        {
            if (introLeadMaskRect != null && introLeadMaskRect.gameObject.activeSelf)
                introLeadMaskRect.gameObject.SetActive(false);
            if (introGlowRect != null && introGlowRect.gameObject.activeSelf)
                introGlowRect.gameObject.SetActive(false);
            return;
        }

        if (!introLeadBuilt) BuildIntroLead();
        if (introLeadImage == null) return;

        if (!introLeadMaskRect.gameObject.activeSelf) introLeadMaskRect.gameObject.SetActive(true);
        if (!introGlowRect.gameObject.activeSelf) introGlowRect.gameObject.SetActive(true);

        float red = Mathf.Clamp01(red01);
        float white = Mathf.Max(red, Mathf.Clamp01(white01));
        float a = Mathf.Clamp01(alpha);

        // ① 바 안 흰 띠 — 빨강 끝부터 흰 구간 끝까지(빨강 뒤에 깔리지만 빨강이 덮는 부분은 어차피 안 보인다).
        introLeadRect.anchorMin = new Vector2(red, 0f);
        introLeadRect.anchorMax = new Vector2(white, 1f);
        introLeadImage.color = WithAlpha(introLeadColor, introLeadColor.a * a);

        // ② 발광 = 흰 띠와 같은 구간을 번짐 폭만큼 사방으로 넓힌 사각형. 가운데는 고르고 가장자리에서 부드럽게 꺼진다.
        float spread = Mathf.Max(1f, introGlowSpread);
        introGlowRect.anchorMin = new Vector2(red, 0f);
        introGlowRect.anchorMax = new Vector2(white, 1f);
        introGlowRect.offsetMin = new Vector2(-spread, -spread);
        introGlowRect.offsetMax = new Vector2(spread, spread);
        introGlowImage.pixelsPerUnitMultiplier = GlowBorderTexels / spread;   // 슬라이스 테두리(번짐) = spread px
        introGlowImage.color = WithAlpha(introGlowColor, introGlowColor.a * a);
    }

    const int GlowBorderTexels = 32;

    private void BuildIntroLead()
    {
        introLeadBuilt = true;
        Image shape = delayed.FillImage != null ? delayed.FillImage : hpFill;
        if (shape == null || hpFill == null)
        {
            Debug.LogWarning($"[BossHealthHUD] 등장 차오름 흰 구간을 만들 바 이미지가 없다 — 흰 구간 없이 빨강만 차오른다: {name}", this);
            return;
        }

        // ① 마스크 = 바 모양(같은 스프라이트, 가득 찬 모양). 자리·크기는 잔상 바와 같게, 순서는 HP_Fill 바로 뒤.
        var maskGo = new GameObject("IntroLeadMask", typeof(RectTransform), typeof(Image), typeof(Mask));
        introLeadMaskRect = (RectTransform)maskGo.transform;
        introLeadMaskRect.SetParent(hpFill.rectTransform.parent, false);
        CopyRect(shape.rectTransform, introLeadMaskRect);
        introLeadMaskRect.SetSiblingIndex(hpFill.rectTransform.GetSiblingIndex());   // HP_Fill 을 한 칸 뒤로 밀고 그 앞(아래)에 선다
        var maskImage = maskGo.GetComponent<Image>();
        maskImage.sprite = shape.sprite;
        maskImage.type = Image.Type.Simple;
        maskImage.raycastTarget = false;
        maskGo.GetComponent<Mask>().showMaskGraphic = false;

        introLeadImage = NewImage("IntroLead", introLeadMaskRect, null);   // 스프라이트 없음 = 단색 흰 띠
        introLeadRect = introLeadImage.rectTransform;

        // ② 발광 루트 = 바와 같은 자리(앵커 비율 = 바 위 위치), 순서는 HP_Fill 바로 앞(빨강 끝 위로도 살짝 번진다).
        //    🔴 이것도 바 모양 Mask 다(경석 Play 10-23: 마스크 없이 두니 번짐 사각형이 바 밖으로 삐져나왔다) —
        //       빛은 바 모양 안에서만 보이고, 위아래는 바 테두리에서 잘려 앞뒤(빨강 끝·빈 칸 쪽)로만 부드럽게 번진다.
        var rootGo = new GameObject("IntroGlow", typeof(RectTransform), typeof(Image), typeof(Mask));
        var root = (RectTransform)rootGo.transform;
        root.SetParent(hpFill.rectTransform.parent, false);
        CopyRect(shape.rectTransform, root);
        root.SetSiblingIndex(hpFill.rectTransform.GetSiblingIndex() + 1);
        var rootImage = rootGo.GetComponent<Image>();
        rootImage.sprite = shape.sprite;
        rootImage.type = Image.Type.Simple;
        rootImage.raycastTarget = false;
        rootGo.GetComponent<Mask>().showMaskGraphic = false;

        // 9-슬라이스 소프트 사각형: 테두리(번짐) 32텍셀 · 가운데 2텍셀(고르게 늘어난다). 가장자리 쪽으로 (1-d)² 로 꺼지고 모서리는 둥글다.
        const int size = GlowBorderTexels * 2 + 2;
        Sprite glow = MakeSprite(size, size, (u, v) =>
        {
            float x = Mathf.Max(0f, Mathf.Abs(u * (size - 1) - (size - 1) * 0.5f) - 0.5f) / GlowBorderTexels;
            float y = Mathf.Max(0f, Mathf.Abs(v * (size - 1) - (size - 1) * 0.5f) - 0.5f) / GlowBorderTexels;
            float d = Mathf.Clamp01(Mathf.Sqrt(x * x + y * y));
            float k = 1f - d;
            return new Color(1f, 1f, 1f, k * k);
        }, new Vector4(GlowBorderTexels, GlowBorderTexels, GlowBorderTexels, GlowBorderTexels));
        introGlowImage = NewImage("IntroGlowBand", root, glow);
        introGlowImage.type = Image.Type.Sliced;
        introGlowImage.fillCenter = true;
        introGlowRect = introGlowImage.rectTransform;

        introLeadMaskRect.gameObject.SetActive(false);
        introGlowRect.gameObject.SetActive(false);
    }

    private static Image NewImage(string goName, RectTransform parent, Sprite sprite)
    {
        var go = new GameObject(goName, typeof(RectTransform), typeof(Image));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        Stretch(rect);
        var image = go.GetComponent<Image>();
        image.raycastTarget = false;
        image.type = Image.Type.Simple;
        image.sprite = sprite;
        return image;
    }

    /// <summary>(u, v) ∈ [0,1]² → 색 함수로 텍스처를 구워 스프라이트로. OnDestroy 에서 정리한다.</summary>
    private Sprite MakeSprite(int width, int height, System.Func<float, float, Color> pixel, Vector4 border = default)
    {
        var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = $"{name}_Generated",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.DontSave,
        };
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        {
            float v = height > 1 ? (float)y / (height - 1) : 0.5f;
            for (int x = 0; x < width; x++)
            {
                float u = width > 1 ? (float)x / (width - 1) : 0.5f;
                pixels[y * width + x] = pixel(u, v);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        sprite.hideFlags = HideFlags.DontSave;
        generatedSprites.Add(sprite);
        return sprite;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static Color WithAlpha(Color c, float a)
    {
        c.a = Mathf.Clamp01(a);
        return c;
    }

    private static void CopyRect(RectTransform from, RectTransform to)
    {
        to.anchorMin = from.anchorMin;
        to.anchorMax = from.anchorMax;
        to.pivot = from.pivot;
        to.anchoredPosition = from.anchoredPosition;
        to.sizeDelta = from.sizeDelta;
        to.localRotation = from.localRotation;
        to.localScale = from.localScale;
    }

    private void BindBoss(Unit boss)
    {
        if (boundBoss != null)
            boundBoss.ClientHpChanged -= delayed.OnHpChanged;

        boundBoss = boss;
        boundGaugeBoss = boss as TwentyThreeBoss;

        if (boundBoss != null)
            boundBoss.ClientHpChanged += delayed.OnHpChanged;

        delayed.Bind(boundBoss != null ? boundBoss.CurrentHealth : 0);
    }

    private static Unit FindBoss()
    {
        var targets = BossHudTarget.Active;
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] != null && targets[i].Unit != null && targets[i].Unit.CurrentHealth > 0)
                return targets[i].Unit;
        }

        return null;
    }
}
