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

## 역할 — 기획·테스트는 사용자, 구현은 Claude

- 사용자가 정한 규칙을 임의로 더하거나 빼지 않는다. 버그를 찾으면 보고하고, 넣을지는 묻는다.
- 몬스터 AI 설계는 `Docs/몬스터AI_기획서.md`가 기준이다. 규칙을 바꾸면 이 문서도 같이 고친다.

## 플레이 QA — 사용자가 "이 즈음에서 이런 일이 있었다"고 말할 때

사용자는 **화면 타이머(남은 시간, 예 "04:22")** 로 시점을 말하거나, 플레이 중 **F8**로 표시해 둔다.
스크린샷에도 타이머가 보인다. 기록은 `PlaytestRecordings/<날짜_시각_id>/trace.jsonl`(플레이마다 자동 저장, 커밋하지 않음).

```
python Tools/QA/pico_qa.py list                 # 최근 기록 (가장 최근이 사용자의 마지막 판인지 시각으로 확인)
python Tools/QA/pico_qa.py summary [기록]       # 판단 횟수 + 의심 장면(보면서 복귀·줄줄이·멈춤·와리가리·Idle)
python Tools/QA/pico_qa.py marks [기록]         # F8 표시마다 전후 5초 상세
python Tools/QA/pico_qa.py at 04:22 [기록] [초] # 그 시각 전후: 몬스터별 역할·상태·거리·방향·시야 + 감독 판단 문장
python Tools/QA/pico_qa.py decisions [기록]     # 감독 판단 전체
```

- `[기록]`은 폴더 이름 일부(예 `18-46`) 또는 `-2`(두 번째로 최근). 자동 테스트도 기록을 남기므로 시각으로 사용자 판을 고른다.
- 감독의 모든 판단은 `decision` 이벤트로 `code|문장` 형태로 남는다. 원인을 찾을 때 문장을 그대로 인용해 설명한다.
- 사용자에게 설명할 때는 화면 타이머 시각과 몬스터 이름(A·B·C·D·Global)으로 말한다.
