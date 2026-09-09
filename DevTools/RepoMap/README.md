# RepoMap

`Assets/**/*.cs`를 tree-sitter로 파싱해 `Docs/tech/repo_map.md`(사람/AI 가독용 요약) +
`Docs/tech/repo_map.json`(원본 데이터)를 생성하는 devtool.

설계 근거: [PLAN.md](../../PLAN.md) 「레포지토리 맵(Repo Map) 생성 도구」.

## 사용법

```
cd DevTools/RepoMap
npm install   # 최초 1회 (node_modules는 git에서 제외됨)
npm run generate
```

## 표시 기준

- `interface`는 **공개 계약**이라 멤버 시그니처를 전부 보여준다.
- `class`의 메서드는 아래 "wrapping 함수" 기준에 맞을 때만 시그니처 + 위임 대상을 보여주고,
  나머지 내부 구현은 개수만 남긴다(Deep Module 원칙 — [architecture.md](../../Docs/tech/architecture.md)
  "합성 우선" 설계와 맞물림).
- **wrapping 함수 판정**: 메서드 본문이 `=> Foo();` 형태(문법상 항상 단일 식, `[wrapper]`) 또는
  블록 본문이 statement 1~2개뿐이고 분기/반복이 없으며 다른 멤버를 호출하는 경우(`[wrapper?]`).
  XML 문서 주석에 "래퍼"/"wrapper"/"호환"이 있으면 `[wrapper·주석확인]`로 신뢰도를 올린다.
- 서드파티 에셋 폴더(`generate.js`의 `EXCLUDE_PATH_SUBSTRINGS`)는 스캔에서 제외한다.
  새 서드파티 플러그인이 들어오면 그 배열에 추가할 것.

## 한계 (v1)

- 수동 재생성만 지원 — git hook/CI 자동화는 범위 밖.
- 휴리스틱이라 과탐/누락 가능 — 사람이 훑어보는 보조 지도로 쓸 것.
- C# 외 파일(셰이더, 씬)은 다루지 않는다.
