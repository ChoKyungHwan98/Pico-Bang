# Pico-Bang! — 작업 규칙

## 유니티 에디터 조작은 Unity CLI로 한다

공식 Unity CLI를 쓴다. 서드파티 MCP for Unity(`com.coplaydev.unity-mcp`)는 쓰지 않는다.

```
C:\Users\Admin\AppData\Local\Unity\bin\unity
```

- `unity list` — 사용 가능한 명령 151개 목록
- `unity command <이름> --json` — 명령 실행
- `unity command` (인자 없이) — 명령별 파라미터 확인

에디터가 켜져 있어야 한다. 컴파일/도메인 리로드 중에는 30초 타임아웃이 나므로,
`editor_status`로 `"status": "ready"`를 먼저 확인하고 본 작업을 보낸다.

명령 이름은 추측하지 말 것. `list_open_scenes`는 있지만 `get_open_scenes`는 없다.

## 환경

- 프로젝트: `C:\UnityProjects\Pico-Bang` (유니티 6000.2.10f1, URP 17.2.0)
- Pipeline 패키지: `com.unity.pipeline` 0.7.0-exp.1
- 빌드 산출물: `C:\Users\Admin\Documents\Pico-Bang!` — 소스 아님, 여기서 작업하지 말 것
- 저장소는 비공개. 유료 에셋이 들어 있어 공개 전환 금지.

## GitHub 작업 흐름 — 결과는 main에 올린다

사용자는 GitHub Desktop에서 **main을 Pull하는 것만으로** 작업을 받아 간다.
브랜치에만 올리면 사용자 컴퓨터에 들어오지 않는다(2026-09-24 클라우드 작업이 `claude/...` 브랜치에만 있어 누락됨).

- 작업을 마치면 main에 반영해 푸시한다. main이 그사이 앞서 나갔으면 먼저 main을 받아 합친 뒤 올린다.
- 강제 푸시(`--force`)는 하지 않는다.
- main에 직접 푸시할 수 없는 환경이면, 마지막 답변에 **브랜치 이름**을 적어 사용자가 병합할 수 있게 한다.
- 유니티 에디터가 없는 환경(클라우드)에서는 컴파일을 확인할 수 없다. 그 경우 커밋 메시지에 "유니티 컴파일 미확인"을 적는다.
