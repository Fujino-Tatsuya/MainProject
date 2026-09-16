# Cyanilux CRT 테스트 — fix/PP

2026-09-15 갱신. Unity 6000.3.16f1 / URP 17.3 / RenderGraph.

## 사용

- 기존 MainFlow로 게임을 시작하면 `4.MapScene`에서 CRT가 켜진다. 기존 픽셀레이트와 스캔라인은 기본 OFF다.
- 맵의 `RetroCRTController` 인스펙터에서 아래 세 효과를 각각 조절한다.

| 구역 | 항목 | 기본값 / 의미 |
|---|---|---|
| Cyanilux CRT | Effect Enabled | ON. Cyanilux CRT만 전환 |
| Cyanilux CRT | Bezel Size | 1. 0=검은 베젤 없음, 1=기존 크기, 클수록 넓어짐(최대 3) |
| 기존 픽셀레이트 | Pixelate Enabled / Pixel Block Size | OFF / 4px. 1px이면 픽셀화 없음 |
| 기존 스캔라인 | Scanline Enabled | OFF. CRT와 별도로 전환 |
| 기존 스캔라인 | Scanline Thickness Px / Scanline Spacing Px | 2px / 4px |
| 기존 스캔라인 | Scanline Color / Scanline Opacity | 기존 녹색 계열 / 0.2. 불투명도 0이면 효과 없음 |

줄 색의 기본 RGB는 `(0.032484863, 0.09433961, 0.06783043)`이며 알파 대신 불투명도로 강도를 정한다.
픽셀·줄 크기는 CRT 처리 전 렌더 픽셀 기준이다. CRT도 켜면 블록과 줄에 CRT 곡률이 적용된다.

Play 중 Game View에 포커스를 두고 **F7**을 누르면 **Cyanilux CRT만** 전환한다.
이 단축키는 에디터와 Development Build에서만 동작한다. 픽셀레이트·스캔라인 설정은 유지된다.
원본 화면을 보려면 세 효과를 모두 끈다.

CRT 모양은 `Assets/99.Settings/CyaniluxRetroCRT.mat`에서 조절한다.
검은 가장자리 범위는 컨트롤러의 `Bezel Size`로 Play 중에도 조절한다. 화면 곡률을 바꾸지 않고 베젤 마스크의 표시 영역만 조절하며, 값 1 부근에서 조금씩 움직이면 얇은 테두리를 맞추기 쉽다.
컨트롤러의 베젤 값은 해당 렌더 패스에만 전달하며 머티리얼 에셋에 저장된 값을 덮어쓰지 않는다.

## 기본 프리셋

| 항목 | 설정 |
|---|---|
| CRT Warp | ON, strength (0.035, 0.035) |
| RGB Stripes | ON, resolution 640, strength 0.15 |
| Scanline Strength / Height | 0.08 / 2 |
| Static / Rolling Static / Image Distortion | OFF |
| Image Brightness | 1 |

Cyanilux의 `Scanline Strength`와 컨트롤러의 기존 스캔라인은 서로 다른 효과다.
함께 켜면 줄무늬가 중첩되므로 기존 스캔라인을 조절할 때는 머티리얼의 `Scanline Strength`를 0으로 낮출 수 있다.
기존 픽셀레이트는 블록 중심을 샘플링하며, 저해상도 RenderTexture를 만들지는 않는다.
`RGB Stripe Strength`는 0이면 RGB 무늬 없음, 1이면 원본 Cyanilux 강도다.
실제 맵에서 원본 강도의 색 줄무늬가 너무 강하게 나타나 조절 항목을 추가했다.

키워드 체크박스는 편집 모드에서 설정하고 머티리얼에 저장한다. F7은 셰이더 키워드를 변경하지 않는다.
향후 빌드에서 미사용 키워드 조합을 런타임으로 활성화하려면 셰이더 변형 보존을 별도로 다뤄야 한다.

## 연결과 범위

- `PC_Renderer`의 `RetroCRTFeature`가 CRT 머티리얼과 기존 계산을 복원한 PixelScanline 셰이더를 참조한다.
- `4.MapScene`의 `RetroCRTController`가 씬 단위로 세 효과를 각각 제어한다. 별도 PixelScanlineSettings 애셋은 사용하지 않는다.
- 기존 MaskBlur와 URP 후처리 이후, `AfterRenderingPostProcessing`에서 실행한다.
- 순서는 **기존 픽셀레이트·스캔라인 → Cyanilux CRT**다. CRT의 RGB 무늬를 다시 픽셀화하지 않도록 CRT를 마지막에 적용한다.
- 기존 두 효과는 함께 한 패스를 사용한다. CRT만 켜면 1개, 기존 효과만 켜도 1개, 양쪽을 켜면 2개다. 모두 OFF면 효과 패스를 추가하지 않는다.
- RenderGraph에서 각 결과를 다음 카메라 컬러로 넘기며, 원본으로 다시 복사하는 패스는 추가하지 않는다.
- Game/Base 출력 카메라에만 적용한다. Scene View, Preview, Reflection, Overlay 카메라와 RenderTexture 미니맵은 제외한다.
- Screen Space Overlay HUD는 CRT 이후에 그려진다. World Space UI는 월드 화면과 함께 처리된다.
- 네트워크 권한·RPC·게임플레이 판정은 변경하지 않는다.

## 원본과 변경

[Cyanilux/URP_RetroCRTShader](https://github.com/Cyanilux/URP_RetroCRTShader),
revision `ccd515c616f409e7aabda4fd02a46fa97317de10`.
외부 텍스처 입력을 `URP Sample Buffer / Blit Source`로 변경하고 RGB 마스크 강도 조절을 추가했다.
RGB 강도 1에서는 원본 RGB 마스크 계산이 유지된다. 베젤 크기도 1에서 원본 가장자리 마스크 계산을 유지하며, 크기를 줄일 때 화면 밖 버퍼를 읽지 않도록 샘플 UV를 유효한 픽셀 중심 범위로 제한한다.
샘플 씬·카메라·URP 설정은 가져오지 않았다.
MIT 라이선스와 변경 고지는 `Assets/1.Scripts/Rendering/RetroCRT/`에 포함한다.

## 검증

### 2026-09-15 — 베젤 크기 추가

- `Bezel Size`를 0 / 1 / 1.1 / 3으로 바꿔 실제 맵 1920×1080 화면의 베젤 제거·확대를 확인했다. 기본값 1은 기존 마스크 수식을 유지한다.
- 비교한 중앙 배경·불투명 HP 바·미니맵 영역의 픽셀 차이 0. 베젤 조절은 영상 샘플링 곡률과 분리되어 있다.
- C# 컴파일 오류 0, 렌더 후 셰이더 메시지 0. 각 값이 컨트롤러에 반영되는 동안 머티리얼 에셋 값은 1로 유지됐고 파일 해시도 변하지 않았다.
- 테스트 화면: `Generated/crt-bezel-20260915/`. 기본값 검증은 수식 검토와 실제 렌더 확인이며, 이전 그래프와의 전체 화면 GPU 비교는 수행하지 않았다.

### 2026-09-15 — 독립 제어 추가

- Unity 실제 C# 컴파일 완료: 오류 0, 기존 경고 4. 두 셰이더 모두 지원됨, 실제 맵 렌더 후 셰이더 메시지 0.
- 로컬 호스트로 `4.MapScene` 실행 후 씬에 저장된 각 인스펙터 필드와 기본값을 확인했다.
- 실제 Game View 1920×1080에서 모두 OFF / 픽셀만 / 스캔라인만 / 모두 ON을 비교했다. CRT OFF 상태에서도 기존 두 효과가 독립적으로 적용됐다.
- 8px 픽셀 블록 1,400개에서 블록 내부 색상 차이 0 확인. 두께 2px·간격 4px 스캔라인 주기와 불투명도 변경도 화면에 반영됐다.
- 블록 크기 1·줄 불투명도 0의 월드 영역은 모두 OFF 화면과 픽셀 차이 0이었다. 비교한 불투명 HP 바·미니맵 영역은 모든 조합에서 차이 0이었다.
- F7으로 CRT만 OFF가 되고 기존 두 효과의 ON 상태와 수치가 유지됨을 확인했다. 조합별 패스 생략·순서는 소스 검토로 확인했다.
- 테스트 부팅기는 Play 런타임에만 생성했으며 씬에 저장하지 않았다. 종료 후 컨트롤러 0개·효과 해제와 원래 BootStrap 편집 상태(미저장 변경 없음) 복귀를 확인했다.
- GPU 시간·1440p·플레이어 빌드·MPPM은 미검증이다.

로컬 비교 화면과 수치: `Generated/crt-controls-20260915/`.

### 2026-09-14 — CRT 단독 적용 이력

아래는 기존 PixelScanline을 제거하고 CRT만 적용했던 시점의 결과다. 9/15 복원 변경의 검증 결과로 간주하지 않는다.

- Unity 6000.3.16f1에서 실제 C# 컴파일 완료: 오류 0, 기존 경고 4.
- Shader Graph 임포트 정상, `shader.isSupported = true`, 실제 맵 렌더 후 셰이더 메시지 0.
- 기존 DevSceneBooter를 임시로 사용해 로컬 호스트 → `4.MapScene` → 플레이어·카메라 생성 경로로 확인했다.
  테스트용 오브젝트는 원본 부팅 씬에 저장하지 않았다.
- Input System F7 입력으로 ON → OFF → ON 상태 전환 확인.
- 실제 Game View의 1920×1080 ON/OFF 프레임에서 월드 화면이 달라지고, 확인한 불투명 HP 바·미니맵 영역의 픽셀 차이는 0이었다.
- 맵 종료 후 컨트롤러 0개와 `IsEffectActive = false` 확인. BackBuffer 오류·CRT 런타임 오류 없음.
- 삭제한 PixelScanline 타입·핵심 GUID의 Assets 잔존 참조 없음.
- GPU 프레임 시간, 1440p, 플레이어 빌드와 MPPM 다중 클라이언트는 미검증이다.

로컬 비교 화면: `Generated/crt-test-20260914/crt-on.png`, `crt-off.png`.

## 변경 이해도 확인

1. 픽셀레이트와 기존 스캔라인을 켠 상태에서 F7을 누르면 무엇이 꺼지는가?
2. RGB 무늬를 더 약하게 하려면 어떤 값을 낮추는가?
3. 이 효과는 서버 RPC를 통해 모든 클라이언트에 동기화되는가?

정답: 1) Cyanilux CRT만 전환되며 기존 두 효과는 유지됨. 2) 머티리얼의 RGB Stripe Strength. 3) 아니오, 로컬 화면 연출임.
