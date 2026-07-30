# Third-Party Notices — Fog 시스템

이 포그 시스템은 아래 오픈소스 프로젝트의 **접근 방식과 수학(높이/거리/밀도 모드/노이즈,
URP RenderGraph 풀스크린 패스 배선)** 을 참조해 재작성했다. 셰이더그래프 원본을 그대로
복사하지 않았으며, 로컬 박스/스피어 볼륨·페인트 마스크·태양 인스캐터는 자체 구현이다.

## meryuhi/URPFog
- 저장소: https://github.com/meryuhi/URPFog
- 라이선스: MIT License
- 참조 범위: `FullScreenFogRendererFeature`의 RenderGraph 패스 배선 패턴
  (`ConfigureInput(Color|Depth)`, 중간 텍스처 + `AddBlitPass`), 거리/높이/밀도 모드 및 노이즈 개념.

```
MIT License

Copyright (c) meryuhi (URPFog)

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

> MIT 라이선스는 상용(Steam 포함) 사용·수정·배포를 허용한다. 위 고지 유지 조건만 충족하면 된다.
> 정확한 원문은 https://github.com/meryuhi/URPFog/blob/main/LICENSE.md 참조(빌드/배포 시 최종 확인).
