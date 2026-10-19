# handoff — 보스 자폭 실드 바를 프리팹 오브젝트로 (2026-10-19, 은희 → 경석)

사용자 지시로 보스 실드 표시를 런타임 복제에서 프리팹 오브젝트로 전환, 로직 무변경.
대상은 23호 레이드 ④ 자폭 실드(778182d0) 의 HUD 표시다.

## 바뀐 것
- `Assets/2.Prefabs/UI/BossHealthHUD.prefab` — `BossInfoSlot/SelfDestructShieldFill` 추가.
  `HP_Fill` 복제(RectTransform·스프라이트·Filled/Horizontal/Origin 0 동일), 색 = `shieldColor`(0.55, 0.85, 1, 0.65),
  `raycastTarget` 끔, 기본 비활성. 형제 순서 = `HP_DelayedFill`, `HP_Fill`, **`SelfDestructShieldFill`**, `Detection_Fill`.
  루트 `BossHealthHUD` 컴포넌트의 `shieldFill` 이 이 Image 를 가리킨다.
- `Assets/1.Scripts/UI/Combat/BossHealthHUD.cs` — `EnsureShieldFill`(hpFill 런타임 `Instantiate`) 삭제. 직렬화된 `shieldFill` 만 쓴다.
  `Awake` 에서 `shieldFill.color = shieldColor`(색 원본은 여전히 `shieldColor` 필드), 비어 있으면 경고 1회 후 실드 표시를 건너뛴다.
  실드 비율(`SelfDestructShield01`)·`fillAmount`·표시 조건·활성 토글은 그대로.
- 후속: 사용자 지시로 실드 스프라이트를 흰색 단색 `Assets/2.Prefabs/UI/Textures/BossShieldGaugeSolid.png`(HP 바와 같은 916×28 실루엣, RGB 흰색·알파만 `gauge_boss_HP` 복사)로 교체 — HP 붉은 그라데이션에 곱해져 주황으로 보이던 것을 `shieldColor` 가 그대로 보이게, 로직 무변경.

## 상속
- `CombatHUD.prefab` 은 `BossHealthHUD.prefab` 을 중첩 인스턴스로 쓰고, `shieldFill` 오버라이드·제거 오브젝트가 없어 그대로 상속된다(CombatHUD 무수정).
  `CombatHUD.prefab` 안의 다른 `shieldFill`(PlayerHealthHUD) 은 무관.
- `BossHealthHUD` 컴포넌트를 직접 가진 프리팹·씬은 `BossHealthHUD.prefab` 하나뿐이다.
