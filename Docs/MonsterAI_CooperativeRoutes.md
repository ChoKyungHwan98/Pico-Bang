# 몬스터 AI: 경로와 참여자 통합 배정

> 이전 정책 기록이다. 현재 동작은 `MonsterAI_MultiApproach_NoTeleport.md`를 따른다. 게임 중 텔레포트는 제거됐고, 비참여 몬스터는 사냥 중 다른 통로를 도보로 준비한다.

구현·검증일: 2026-09-20. 실제 NavMesh 검증 씬: `Assets/Scenes/ShooterInGame_AITest.unity`.

현재 정책: `joint-routes-restored-2026-09-20`. 21시 플레이 피드백에 따라 직전의 2초 위치 예측, 3.5~5초 협공 제외, 조기 추격자 교체를 철회하고 20:25 플레이 당시 배정 방식으로 복원했다. 카메라/정지 시간 기록 수정과 진단 이벤트는 유지한다.

## 고정할 플레이 의도

제한시간 안에 움직이며 좁혀오는 몬스터 사이에서 빈틈을 찾아 과녁을 부수고 탈출한다.
기존 5마리를 사용한다. 항상 3마리를 모으는 것이 목표가 아니다. 쫓는 압박은 유지하고,
지원은 실제로 다른 경로를 사용할 수 있을 때만 참여시킨다.

- 압박 추격 1마리 + 경로가 겹치지 않는 지원 최대 2마리.
- 방향 슬롯, 플레이어의 앞/옆 좌표, 가까운 두 마리 선발을 사용하지 않는다.
- 지원 경로가 실패하면 직행 추격으로 대체하지 않는다. 참여하지 않거나 자기 구역으로 복귀한다.
- 돌아오는 몬스터는 실제 게임 카메라에 출발·도착 모습이 모두 보이지 않을 때 자기 구역으로 순간이동한다.
- 소리와 과녁 파괴도 동일한 배정 경로로 들어간다. 별도의 가까운 두 마리를 추가로 부르지 않는다.

## 책임 경계

| 파일 | 책임 |
|---|---|
| MonsterRoutePlanner.cs | 실제 NavMesh 후보, 완전 경로, 우회 검증, 대칭적인 경로 충돌 검사 |
| MonsterDirector.cs | 모든 가능한 몬스터·경로 조합 평가, 압박/지원 명령, 정보 수명, 줄서기 해소, 복귀 가시성 |
| MonsterAI.cs | 배정받은 코너 실행, 추격의 기억, 시야, 기절, 점프, 복귀 이동 |
| NoiseSystem.cs | 소음 사건을 Director에 전달 |
| AITestDebug.cs | 기존 테스트 씬에서 배정 경로·압박/협공 역할 표시 |
| AgentScripts/MonsterAIRegression.cs | Unity CLI로 실행하는 실제 맵 회귀 검사. Assets 밖이므로 제품 빌드에 포함되지 않음 |

## 한 번의 배정 절차

1. 마지막 목격/발사 사건 위치를 기준점으로 사용한다. 확인되지 않은 미래 위치를 목표로 쓰지 않는다. 정보가 없어진 후 실제 플레이어 위치를 몰래 계속 읽지 않는다.
2. 기절·복귀 잠금·NavMesh 이탈 개체를 제외한다. 팀원이 OffMeshLink를 넘는 중이면 기존 배정을 유지하고 0.15초 뒤 재평가한다.
3. 각 개체의 최단 경로와 우회 경로를 만든다. 우회 후보는 NavMesh 삼각형의 중심/변 중점에서 추출한다.
4. 후보는 기준점으로부터 7~42m 범위, 공간 격자로 중복 제거한다. 가까운 6개와 전체 거리 범위에 분산한 후보를 합쳐 최대 20개를 평가한다. 개체별 최종 경로는 최대 7개다.
5. 개체의 agent type, area mask, area cost를 사용한다. 불완전 경로, 목적지를 먼저 통과하는 우회, 뚜렷한 왕복을 제외한다. 지원 경로의 길이 한도는 `huntFarSpeed * detourTimeLimit`(기본 14×8=112m)이다. 거리/최대 속도는 도착 시간의 하한 추정일 뿐 실제 도착 보장이 아니다.
6. 압박 후보와 지원 경로 0~2개를 함께 비교한다. 같은 개체를 두 역할에 배정하지 않는다. 압박 후보는 가까운 경로보다 15m 넘게 먼 개체로 바꾸지 않는다(유효한 현재 추격자의 3초 역할 유지 구간 제외).
7. 지원 수와 경로 길이, 기존 배정 유지 보너스를 고려한다. 기존 경로도 충돌 검사를 통과해야 한다. 압박 추격자는 먼 지원을 기다리거나 속도를 낮춰 도착 시간을 맞추지 않는다.
8. 지원은 배정된 코너를 순서대로 지난다. 최종 접근에서도 임의로 플레이어의 새 최단 경로를 잡지 않는다. 새 위치는 다음 통합 배정에서 반영한다.

### 같은 길 판정

단순 직선거리나 한쪽 경로의 겹침률만으로 판단하지 않는다.

- 경로 양쪽을 검사하고 큰 겹침률을 사용한다. 긴 우회가 짧은 공통 통로를 희석하지 못한다.
- 같은 방향이고 3m 이내인 구간을 후보로 비교한다. NavMesh.Raycast가 사이의 벽을 만나면 다른 통로로 취급한다.
- 목표까지 남은 경로 거리 2.5m 이내만 최종 접촉 구간으로 제외한다.
- 겹침률 0.38 초과, 연속 공유 구간 5m 이상, 목표 전 9m 구간 중 공유 길이 3m 이상이면 함께 배정하지 않는다.
- 수치들은 `MonsterRoutePlanner`의 정책이다. 예전 Inspector의 방위각/8m 제외 반경 등은 숨겨 두었으며 사용하지 않는다.
- 이동 중 7m 내에서 같은 방향으로 뒤따르는 지원이 1초 이상 지속되면 지원을 복귀시키고 재배정한다. 실제 값은 전역 몬스터의 followDistance/followHoldTime이다.
- 통합 배정 결과 팀에서 빠진 경우 `assignment_released`, 실제 같은 통로 줄서기를 해소한 경우 `follow_break` 이벤트를 기록한다. `support_route_estimate`는 경로 길이/최대 속도의 하한 추정이다. 포트폴리오 분석에서 이를 실제 도착 시각으로 취급하지 않는다.

경로가 하나인 곳에서는 포위를 억지로 만들 수 없다. 여기서는 추격을 유지하면서 다른 몬스터가 줄줄이 합류하지 않게 한다. 경로 검색은 비용이 제한된 휴리스틱이므로 모든 가능한 우회를 완전 탐색한다고 주장하지 않는다.

## 정보·기절·복귀 규칙

- 과녁 파괴가 전달하는 것은 **발사 당시 위치·시간**이다. 늦게 도착한 발사체는 더 최신 목격 정보를 덮어쓰지 못한다.
- 실제 새 사격/목격 정보는 추적 시간을 갱신한다. 같은 배정을 반복 실행하는 것만으로 추적 시간을 갱신하지 않는다.
- 마지막 정보로부터 huntMemory가 지나면 사냥을 끝낸다. 시야를 끊고 사격도 멈추면 벗어날 수 있다.
- 기절한 개체의 경로는 폐기한다. 회복 후 현재 상황에서 새 배정을 받으며, 기절 전 경로를 복원하지 않는다.
- 기존의 20초 주기 강제 흩어짐과 구역 거리만으로 하는 인계를 제거했다. 실제 경로를 평가한 결과에 따라 교대한다.
- 복귀 순간이동은 Return 상태만 허용한다. 순찰/추격/기절/점프 중에는 순간이동하지 않는다.
- 실제 게임 카메라의 프러스텀과 몸체 여러 지점의 가림을 검사한다. 테스트 씬의 탑다운 카메라는 사용하지 않는다. 게임 카메라가 없으면 걸어서 복귀한다.
- 출발점이 teleportUnseenTime 동안 보이지 않아야 한다(현재 전역 설정 3초). 도착점은 자기 zoneCenter 주변 3m NavMesh, 자기 구역 안, 플레이어와 5m 이상, 다른 몬스터와 2m 이상 떨어져 있어야 한다. 도착점이 보이면 이동하지 않는다.
- 순간이동 성공 여부를 확인한 뒤 Rigidbody 위치도 맞추고 새 순찰 목적지를 잡는다.
- 전역 몬스터처럼 zoneCenter가 자기 자신/자식 Transform을 참조하면, 시작 시 그 위치에 고정된 별도 복귀 지점을 만든다. 움직이는 자기 위치를 집으로 오인하지 않는다.

## 조절해도 되는 설정

전역 몬스터의 huntTeamSize(1~3), huntNearSpeed/huntFarSpeed, huntNearDistance/huntFarDistance,
detourTimeLimit,
layoutRefreshInterval(실제 적용 0.3~1초), huntMemory, pursuitPersistence,
persistenceDecay/persistenceMin, followDistance/followHoldTime, teleportUnseenTime.

기존 인원·속도·씬 배치 자산은 변경하지 않았다. 사용하지 않는 구형 필드는 씬 직렬화 호환을 위해 남기고 Inspector에서 숨겼다.

## 재현 검사

Unity CLI 경로는 저장소 `CLAUDE.md`를 따른다. AI 테스트 씬을 Play한 뒤 Pause한다.
`GameFlowManager.Instance.DebugBeginTest()`를 호출해 홈 화면 상태를 건너뛴다.

```powershell
unity command run_script --file AgentScripts/MonsterAIRegression.cs --entry MonsterAIRegression.StaticScenarios --result-only --json
unity command run_script --file AgentScripts/MonsterAIRegression.cs --entry MonsterAIRegression.CorridorRegression --result-only --json
unity command run_script --file AgentScripts/MonsterAIRegression.cs --entry MonsterAIRegression.MovingScenario --result-only --json
unity command run_script --file AgentScripts/MonsterAIRegression.cs --entry MonsterAIRegression.StunRegression --result-only --json
unity command run_script --file AgentScripts/MonsterAIRegression.cs --entry MonsterAIRegression.FollowingRegression --result-only --json
unity command run_script --file AgentScripts/MonsterAIRegression.cs --entry MonsterAIRegression.ReturnAndKnowledgeRegression --result-only --json
```

움직임/기절 검사는 일시적으로 백그라운드 실행을 켜고 시간이 실제로 진행했는지도 검사한다.
검사가 끝나면 다시 Pause한다. 런타임 위치·카메라·디버그 상태를 조작하므로 **검사 후 Play를 종료**한다. 씬을 저장하지 않는다.

2026-09-20 실제 실행 결과:

| 기준 위치 | 선택 결과 |
|---|---|
| 중앙 (-3, 1) | C 압박 + Global/A 별도 경로 |
| 서쪽 (-50, 20) | B 압박 + C 별도 경로 |
| 남쪽 (10, -45) | D 단독 압박 |
| 북쪽 (20, 50) | A 압박 + C 별도 경로 |
| 동쪽 (65, 0) | Global 압박 + A 별도 경로 |
| 북쪽 끝 (28, 92) | A 단독 압박 |

- 6개 배정 모두 압박 1개, 전체 1~3개, 배정 경로 쌍의 충돌 없음, 설정된 지원 경로 길이 한도 준수 통과. 단독 압박은 유효한 독립 경로가 없을 때 사용한다.
- 기존 오류 사례인 13m 경로와 52.6m 경로의 같은 마지막 통로: 양쪽 순서 모두 충돌, 겹침률 1.0. NavMesh 밖 시작점 거부 통과.
- 보이는 출발/도착의 순간이동 금지, 완전 가림, 시야 밖 복귀·순찰 재개, 순찰 중 순간이동 금지 통과.
- 낡은 발사 사건 무시, 정보 만료로 사냥 종료 통과.
- 12초 실행: 4초 정지 사격 후 여러 구역을 잇는 실제 NavMesh 경로로 11m/s 이동, 1.5초마다 목격 정보를 갱신. 각 기록 시점에 압박 배정 유지와 최대 인원을 검사한다. 실전 전체 라운드 플레이 테스트를 대체하지는 않는다.
- 기절 중 배정/순간이동 제외 및 약 3.5초 뒤 회복 검사 통과.
- 같은 통로를 따라가는 잘못된 지원 경로를 강제로 주입한 검사: 약 1.2초 뒤 뒤쪽 지원을 복귀시키고 앞쪽 압박 추격은 유지. 움직이는 복귀 중심 방지 검사도 통과.

계산 비용도 측정했다. 해당 에디터에서 정적 배정은 약 10~35ms(첫 실행 포함), 이동 검사 표본은 약 3~25ms였다. 공유 경로 캐시와 후보 수 제한을 적용했지만 엄격한 60fps 프레임 예산을 보장하는 결과는 아니다. 맵/몬스터 수 확대 시 후보 생성의 프레임 분할과 빌드 환경 프로파일링이 다음 성능 작업이다.

## 후속 작업자가 지킬 완료 기준

1. 가까운 두 마리 선발, 방향 슬롯, 경로 실패 시 직행 fallback을 다시 넣지 않는다.
2. 팀 인원 숫자를 채우려고 압박 추격자를 멀리 보내거나 지원을 같은 통로에 추가하지 않는다.
3. 지원의 SetDestination(player.position) 자동 갱신을 넣지 않는다. 경로 갱신은 참여자 선택과 같은 판단에서 한다.
4. 기존 역할 유지나 근거리라는 이유로 경로 충돌 검사를 생략하지 않는다.
5. 시야 밖 복귀 테스트는 출발점과 도착점을 각각 독립적으로 검증한다.
6. 수치/경로 판정을 바꾸면 위 검사를 다시 실행하고 실제 실행 결과와 남은 한계를 갱신한다.
7. 포트폴리오 영상은 기존 탑다운 테스트 화면과 실제 플레이 화면을 함께 사용한다. 상태명뿐 아니라 배정 경로와 플레이어의 경로 변경이 함께 보여야 한다. '경로를 바꾼 횟수'의 인과 판정/영상 태깅은 이번 코드 수정에 포함하지 않았다.
