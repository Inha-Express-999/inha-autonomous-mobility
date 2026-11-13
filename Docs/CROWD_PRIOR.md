# 도로 혼잡 예상(시간대 prior) 표시

프로젝트 0.5.7.0 · 2026-11-09 · `road-density` 브랜치. 범위는 **표시 전용 시간대 예상값**이다. 실측 혼잡, 센서 관측 혼잡, 경로 비용 반영은 포함하지 않는다.

## 흐름

```text
configs/crowd_prior.json ─┐
서버 시뮬레이션 시각 ──────┼→ campus_sim.crowd.CrowdPrior ─→ GET /v1/demo/crowd ─┬→ 웹 /demo/ SVG 도로 색
도로 그래프(404 방향 간선) ┘         (구역 밀도 → 도로 등급)                         └→ Unity PC CampusCrowdOverlay
                                       GET /v1/demo/unity-roads (정적 도로 형상, Unity X/Z) ─┘
```

- 시각: 서버 시계는 0초부터 센다. `start_time_of_day`(기본 08:30:00)를 더해 Asia/Seoul 시각으로 계산한다.
- 생성률: AGENTS §7 식을 사용한다. 다섯 전환 시각마다 피크 −300s/+300s의 가우시안 pulse를 주며 각 창 ±900s 밖에서는 0이다. `λ = base + peak × multiplier × Σpulse`.
- 밀도: `ρ_prior = ∫[t−체류, t] λ ds / 유효면적`(명/m², 사다리꼴 적분).
- 등급: §8 기준에 따라 0.2 미만은 NORMAL, 0.2~0.5는 CAUTION, 0.5 이상은 AVOID다. `biryong_plaza_front`는 혼잡 창 동안 밀도와 관계없이 AVOID(`CONGESTION_WINDOW`)다.
- **CLOSED는 만들지 않는다.** 폐쇄에는 현재 관측이나 명시적 폐쇄가 필요하며, prior가 1.0 이상이면 `exceeds_close_density`만 표시한다.
- 도로 단위: 같은 형상의 양방향 간선을 하나의 `road_id`로 묶는다(209개). 구역 원 안에 10m 이상 또는 길이의 50% 이상 들어간 도로만 그 구역 등급을 받는다. 여러 구역에 걸리면 가장 높은 등급을 쓴다.

## 데이터 출처와 한계

- 구역 8개는 각 랜드마크 Stop 후보의 도로 노드를 중심으로 한 **합성 원**이다. 검증된 polygon이 아니며 비룡플라자 앞 실제 경계도 아직 없다.
- 생성률·체류시간·유효면적은 모두 합성 가정이다(`verification: "synthetic"`). 실측 인원이나 공식 시간표가 아니다.
- 미등록 랜드마크를 기준으로 한 구역은 `unavailable_zones`에 `ANCHOR_NOT_IN_MAP`으로 보고하며, 다른 위치로 대체하지 않는다.
- Unity 보행자 Ground Truth를 읽지 않는다. 화면 문구는 "시간대 추정(실측 아님)"이다.
- 배차·A* 경로 비용·차량 제어에는 아직 반영하지 않는다(`crowd_penalty_s`, AVOID 구간 제외, D* Lite는 후속).

## 역할별 공개 범위

- 웹 `/demo/`: 도로 stroke 색을 바꾼다. 주의는 주황 실선, 회피는 빨간 점선이다. 범례와 시각 표시, "혼잡 예상" 토글을 제공한다.
- Unity PC(`WebClientHud` → `CampusCrowdOverlay`): 경로선 아래 UI 레이어에 표시한다. 회피는 점선이라 색 없이도 구분된다. 3초 이상 갱신이 없으면 숨기고 "갱신 중단"으로 표시한다. 지도 해시가 다르면 그리지 않는다. 차량 추적 시점에서는 경로선과 함께 숨긴다.
- Unity 모바일: 오버레이를 만들지 않고 `/v1/demo/crowd`도 조회하지 않는다(AGENTS §11/§13). 다만 현재 데모 API에는 역할 인증이 없다.

## 혼잡 시간대로 시각 변경

PC Unity 모드에서는 차량이 연결되면 서버 시계가 **1배속으로 고정**된다(`DemoSession.control`). 그래서 시계를 빨리 감는 대신 **혼잡 예상의 시각만** 바꾼다.

- 웹 지도 헤더와 Unity PC 지도 헤더에 **"다음 혼잡 시간대"** 버튼이 있다. 누를 때마다 08:55 → 10:25 → 11:55 → 13:25 → 14:55 → 설정 시각 순으로 이동한다. 각 시각은 피크 5분 전의 pre-peak 중심으로, 밀도가 가장 높은 시점이다.
- 시각을 바꾸면 **"기본 시각"** 버튼이 나타난다. 누르면 설정 시작 시각 기준으로 돌아간다. 상태 문구에는 "혼잡 시각 HH:MM"으로 표시된다.
- API: `POST /v1/demo/crowd/time`. 본문은 `{"action":"next_peak"}`, `{"action":"reset"}`, `{"action":"set","time_of_day":"HH:MM[:SS]"}` 중 하나다. 바꾼 뒤에도 혼잡 시각은 시뮬레이션 시간과 함께 흐른다. 스냅샷의 `time_source`는 `operator_override` 또는 `configured_start`다.
- **운행 시계·차량·배터리·이벤트·센서 관측 나이·명령 만료는 바뀌지 않는다.** prior는 인구 상태가 없는 시간 함수라서 §7의 "인구를 둔 채 시계만 바꾸지 않는다" 규칙과 충돌하지 않는다. 보행자 시뮬레이션이 생기면 그 인구는 이 버튼으로 바뀌지 않으며, 별도 run 시작으로 처리해야 한다.
- 웹과 PC가 같은 서버 세션을 보므로, 한쪽에서 바꾸면 다른 쪽도 1초 안에 따라온다. 세션을 초기화하면 기본 시각으로 돌아간다.

## 검증(2026-11-09, Windows · Python 3.14 · Unity 6000.3.21f1)

- `python -m pytest backend/tests -q -p no:cacheprovider`: 407개 통과(신규 `test_crowd.py` 17개 포함).
- `ruff check`/`ruff format --check`(신규 파일): 통과. `api.py`·`web_demo.py`의 import 정렬 지적 2건은 HEAD에도 있던 것이다.
- 실행 중인 Editor에서 `unity recompile`: 오류 0건. EditMode `run_tests`: 59/59 통과(신규 `CrowdOverlayTests` 2개 포함).
- 웹 `/demo/`(포트 8799)를 10:31로 진행: 주의 12개·회피 4개 도로 표시를 화면으로 확인했다.
- PC_Bootstrap Play(시작 시각을 검증용으로 08:54로 임시 변경했다가 되돌림): 차량 3/3 연결, 19개 구간 표시, 범례와 상태 문구를 `ScreenCapture`로 확인했다.
- 시각 변경 버튼(2026-11-09 추가): backend pytest 409개, EditMode 60/60 통과. PC Play에서 버튼으로 08:30 평시 → 08:55(19개 구간) → 기본 시각 복귀와 "기본 시각" 버튼 표시/숨김을 확인했다. 서버 운행 시계는 0초 그대로였다. 같은 서버의 웹 화면에서 버튼으로 10:25로 이동하는 것과 PC에 반영되는 것도 확인했다.
- 미검증: 모바일 Player, PlayMode 자동 테스트, 프레임 시간·배치 수 측정, 장시간 실행.
- 참고: Play 진입 때 Editor의 거점 위치 동기화가 `maps/inha_osm_runtime/policy.json` 등의 부동소수점 끝자리를 다시 쓴다. 그러면 지도 재생성 결정성 테스트 3개가 실패한다. 이번 작업과 무관한 변경이라 매번 HEAD로 되돌렸다.
