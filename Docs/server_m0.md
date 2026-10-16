# Python 서버 M0 기반

프로젝트 0.2.2.0 · 2026-10-15 · OSM/Unity 지도 기반 FIFO/Dijkstra 가상 시간 실행

기본 차량은 **V01·V02·V03 세 대**, 기준속도 **30km/h**다. 합성 배터리 소모·왕복 후 15% 예비량 검사·복귀 거점 충전을 포함한다. campus.osm/campus.json과 확장 OSM·Unity 외곽 중심선을 사용하는 **inha-osm-unity-v2**가 기본이다. 하이테크·기숙사 1·2·3·인하대역을 포함한 14개 Stop 후보에서 FIFO로 요청을 선택하고 Dijkstra로 픽업·운송·복귀를 각각 탐색한다. 복귀 후 배차 가능한 차량은 가용 대기열 뒤에 넣는다. 충전이 필요하면 충전 완료 후 뒤에 넣는다.

도로 위치·연결은 원본 OSM/Unity 데이터에서 유도한다. 단, 저장소의 폭·통행·Stop·보행 접근은 아직 승인되지 않았다. `routing_mode=osm_geometry_simulation`은 실제 형상에 합성 정책을 적용하는 별도 실험 모드이며, `operationally_verified=false`를 유지한다. 알 수 없는 도로를 검증된 실지도 모드에서 개방한 작업이 아니다. Unity 차량을 움직이지 않으며 속도 30km/h(약 8.33m/s)와 차량 용량은 가정이다.

## 실행

```powershell
python -m pip install -r backend/requirements-dev.lock
python -m pip install --no-deps -e ".\backend"
python -m campus_sim.map_builder
campus-sim serve
```

서버는 기본적으로 `127.0.0.1:8765`에서만 수신한다. 모바일 실기기 연결을 위해 LAN에 열지 않는다.

생성된 `maps/inha_osm_runtime/`를 기본으로 읽는다. 원본·설정·생성 코드가 변경되면 지도 묶음을 재생성해야 한다. 누락·해시 불일치 시 시작을 실패시키며 합성 fixture로 대체하지 않는다. 복제본의 줄바꿈 변환 등으로 원본 바이트 해시가 달라도 재생성한다. 별도 지도 경로는 `CAMPUS_SIM_MAP_DIR`로 지정할 수 있으며 기본 서버는 저장소 원본 해시도 검사한다.

별도 터미널에서 아래 요청을 여러 번 보내려면 각 새 요청마다 `command_id`를 바꾼다. 첫 3건은 각각 가용 차량에 배정하고, 추가 요청은 차량 복귀 또는 충전 완료까지 QUEUED로 대기한다.

```powershell
$requestBody = @{
    command_id = 'example-1'
    owner_id = 'example-passenger'
    service_type = 'PASSENGER'
    pickup_landmark_id = 'main_gate'
    dropoff_landmark_id = 'library'
    party_size = 1
} | ConvertTo-Json
Invoke-RestMethod -Uri 'http://127.0.0.1:8765/v1/requests' -Method Post -ContentType 'application/json' -Body $requestBody
Invoke-RestMethod -Uri 'http://127.0.0.1:8765/v1/debug/virtual-state'
```

`/docs`에서 API를 직접 실행할 수도 있다. 상태 조회에는 각 구간 edge_ids/polyline/cost_s, 목적지 도착·복귀 시각, 복귀까지 남은 시간과 차량 가용 여부가 포함된다. 서버 lifespan의 0.1초 tick이 조회 없이도 복귀를 처리한다. 조회·명령 시에도 경과 시간을 반영한다. 실제 반영 지연은 이벤트 루프 스케줄링에 따르며 하드 실시간 보장이 아니다. 메모리 상태이므로 개발 서버는 단일 worker로 실행한다.

## 파일 구조와 실행 흐름

```text
backend/src/campus_sim/
├─ domain.py           기존 요청·차량·Stop DTO
├─ battery.py          합성 에너지 제원·배터리 상태·충전 세션
├─ dispatch.py         FifoDispatch: 요청 생성 시각/ID → 차량 가용 대기열 순서
├─ planning.py         RoadGraph / CostSnapshot / 자체 Dijkstra
├─ map_coordinates.py  WGS84 ↔ AEQD ↔ 기존 Unity 로컬 좌표
├─ osm_roads.py        OSM 연결·방향·제한 공통 판독
├─ unity_coverage.py   같은 OSM way의 Unity 중심선 대조
├─ map_extension.py    고정 선분 구간 병합·미지원 역 지름길 제외
├─ map_builder.py      OSM/Unity 원본을 오프라인 방향 그래프로 변환
├─ map_data.py         지도 묶음·원본 해시·참조 검사
├─ connection_audit.py 확장 OSM 후보 왕복·도로 등급·Unity 중심선 자료 대조
├─ virtual_runtime.py  3구간 경로 탐색, VirtualTrip, 회귀용 합성 fixture
├─ service.py          요청 접수, 경로 가능성 확인, 시각별 완료/복귀/재배차
├─ api.py              기존 HTTP API + 자동 tick + 가상 상태 조회
└─ demo_fifo.py        실제 대기 없는 3대/충전 운행 데모

configs/osm_runtime.json       속도·후보 간격·차량 3대·배터리·충전 가정
maps/inha_osm_runtime/
├─ manifest.json              원본/생성 코드/산출물 해시, 좌표계, 출처·가정
├─ graph.json                 OSM node ID, 방향 edge, 원본 way/Unity feature 추적
├─ landmarks.json              14개 인벤토리와 후보 누락 사유
├─ stops.json                  도로 투영 후보와 출입구 간격, 왕복 가능 여부
├─ policy.json                 생성 당시 설정
├─ build_report.json           개수·제외 사유·연결 불가 현황
└─ ATTRIBUTION.txt             OSM/ODbL 출처
```

```text
요청 → QUEUED → 용량/Stop 접근성/픽업·운송·복귀 경로 확인 → ASSIGNED
     → 픽업 시각: IN_TRANSIT → 목적지 도착 시각: COMPLETED
     → 차량은 복귀 중으로 계속 점유 → 거점 복귀 → 에너지 확인/필요시 충전 → 차량 FIFO → 다음 요청
```

- FIFO는 `(created_s, request_id)` 순서다. 서버 생성 ID의 순번 접두사로 동일 시각 접수 순서를 보존한다. 수행 불가인 앞 요청은 대기시키고 가능한 다음 요청을 확인한다.
- 기본 출발·복귀는 정문의 `main_gate_road_candidate`다. 임무마다 출발 거점→pickup Stop, pickup→dropoff Stop, dropoff→출발 거점을 각각 탐색한다. 편도 비용을 두 배 하지 않는다.
- 비용은 AEQD 수평 길이 `length_m / speed_mps`다. 설정 속도는 30km/h(약 8.33m/s)이며 지원하는 숫자형 OSM 제한속도가 더 낮으면 이를 적용한다. 경사·Terrain 고도·가감속·신호 대기는 계산하지 않는다. 혼잡/대기 추가 비용은 CostSnapshot 확장 지점이며 현재 기본값은 없다.
- 기존 캠퍼스와 설정에 고정한 service/residential/secondary 선분만 사용한다. 도로 등급 전체를 자동 개방하지 않는다. 보행로·명시적 access 제한·지원하지 않는 회전/조건부 제한·장벽은 제외한다. OSM 공유 node ID로만 연결하며, 입체 교차나 가까운 두 도로를 좌표만으로 연결하지 않는다. 일방통행을 보존하고 Unity 조각의 원본 OSM 선분 일치를 검사한다.
- 실제 운송 지도 모드의 미검증/폭 미확인 도로 제외 규칙은 유지한다. 이번 실행은 별도 `osm_geometry_simulation` 모드에서 미확인 허용을 합성 정책으로 명시한다. RoadGraph의 기존 `synthetic=true`는 이 정책 허용 플래그이며 실제 도로 형상의 출처는 `synthetic_geometry=false`로 구분한다.
- 경로가 하나라도 없으면 배정하지 않는다. Stop 접근성과 경로는 대기 요청 재배정 시 다시 확인한다.
- `main_gate → library` 예제는 픽업 0초, 운송 약 61.5868초, 복귀 약 21.5020초다. 일방통행을 반영한 운송 길이는 약 513.2231m, 복귀 길이는 약 179.1834m다. 약 83.0888초까지 차량을 점유하며 그때 다음 요청을 배정한다. 시간은 run 시작 기준 가상 ETA이고 측정값이 아니다.
- 승하차 시간·가감속·신호·센서·안전·실제 위치 이동은 모델링하지 않는다. 가상 시각상 픽업/완료를 실제 도착 검증으로 해석하지 않는다.
- 배정 전 요청은 취소 가능하다. 배정 후 픽업 이전 취소도 요청은 취소하지만 차량은 기존 복귀 시각까지 예약을 유지한다. 경로 중간 회차는 후속 작업이며, 취소 직후 유휴 처리하지 않는다. 픽업 이후 취소는 기존 API에서 거부한다.
- 기본 합성 차량 3대의 각 용량은 승객 4명/휠체어 슬롯 1개/화물 20kg이며 실차 제원이 아니다. 한 요청만 처리하므로 승객·화물을 동시에 싣지 않는다. 기본 서버/데모는 3대와 배터리를 사용한다. 과거 단일 차량 회귀는 `from_osm_map(vehicle_count=1, battery_enabled=False)`로 명시하며, 합성 fixture 기본은 기존 1대·배터리 제외를 유지한다.

## 차량 FIFO와 배터리

요청은 접수 순서, 차량은 **배차 가능한 상태로 대기열에 들어온 순서**다. 초기 V01→V02→V03에서 V01이 복귀했을 때 V02/V03가 계속 대기 중이면 다음 순서는 V02→V03→V01이다. 앞 차량의 에너지·용량·접근성·경로가 불충족이면 가능한 다음 차량을 선택한다. 충전 중 차량은 대기열에서 빠지고 완충 후 뒤에 들어간다.

`configs/osm_runtime.json`의 설정 schema_version=2에서 `vehicles`와 `battery`를 수정하고 지도를 재생성한다. 단일 `vehicle` 필드는 제거했다. 다음은 **실측 제원이 아닌 합성 가정**이다.

| 설정 | 기본값 |
|---|---|
| 차량 수 / 차량별 승객·화물 용량 | 3대 / 4명 또는 20kg |
| 용량 / 초기 잔량 | 각 2,000Wh / 2,000Wh |
| 거리 소모 / 주행 보조전력 | 0.35Wh/m / 50W |
| 운송 구간 거리 소모 보정 | 승객 1명당 +5%, 화물 1kg당 +1% |
| 배정에 필요한 잔량 | 픽업+운송+정문 복귀 에너지 + 용량의 15% |
| 충전 시작 | 다음 요청 에너지 부족 또는 미배정 SOC 30% 미만 |
| 충전 거점 / 포트 / 전력 | 정문 / 독립 3포트 / 각 순 저장 1,000W |
| 충전 종료 / 대기 전력 | 완충 / 대기 차량 전원 OFF 가정으로 0W |

각 구간 에너지는 `거리 × Wh/m × 적재 보정 + 주행 시간 × 보조전력 / 3600`이다. 적재 보정은 운송 구간만 적용한다. 구간 안에서는 시간 비율로 소모량을 차감하고, 배정 시 전체 왕복 에너지를 먼저 확인한다. 거점은 기존 복귀 목적지라 충전을 위해 별도 직선 연결이나 순간이동을 만들지 않는다. 충전 시간은 `부족한 Wh / 충전 W × 3600`초다. 실제 시설 존재·배터리 성능·전력 손실 검증은 아니다.

완충해도 왕복/예비량이 부족하면 `BATTERY_CAPACITY_INSUFFICIENT`로 대기하며 반복 충전하지 않는다. 현재는 구조적으로 불가능한 요청을 자동 거부로 전환하지 않는다. 주행 중 예상 밖 방전/고장 대응과 다중 차량 충돌/경합 처리는 후속 범위다. 자세한 정책과 한계는 [ADR 0004](ADR/0004-battery-three-vehicle-fifo.md)를 따른다.

debug 응답의 `batteries`는 차량별 잔량·SOC·누적 소모/충전량·READY/ON_TRIP/CHARGING을, `vehicle_fifo`는 가용 차량 순서를, `charging_sessions`는 충전 시작/완료 시각을 제공한다. 기존 Unity snapshot DTO를 변경한 응답이 아니다.

## 현재 지도 범위와 좌표

v2는 OSM way 59개, 노드 187개, 방향 간선 402개다. Unity 인벤토리 14곳 모두 도로 투영 Stop 후보가 있고 정문에서 왕복 가능하다. 왕복 가능은 그래프 계산 결과이며 실제 승하차 승인이나 접근성 판정이 아니다.

- 왕복 후보: 기존 9곳에 하이테크·기숙사 1·2·3·인하대역을 추가했다. 다섯 곳을 출발지로 쓰는 요청도 같은 3구간 탐색을 수행한다.
- `extension_corridors`에 93개 원본 선분 구간을 고정했다. 같은 OSM 선분의 중복/겹침은 병합하고 실제 빈 구간은 유지한다. core/scoped/restored/일반 export의 같은 way 중심선과 대조하며 미지원 구간은 생성 실패시킨다. export의 존재는 활성 씬 메시 적용 증명이 아니다.
- 역 복귀는 955217469(독배로) → 281621314(소성로) → 481241683(정문 연결도로)의 약 407.16m 경로다. Unity export에 없는 1215102704는 지도에서 제외한다.
- Stop은 일반 출입구 후보를 기존 차량 도로 선분에 투영한 위치다. 출입구까지 직선을 차량 도로로 만들지 않는다. 간격은 보행 거리/보행 접근성 검증이 아니다. 접근 가능한 Stop이 검증되지 않았으므로 step-free 요청은 `NO_ACCESSIBLE_ALTERNATIVE`로 거부한다.
- WGS84는 `always_xy=True`로 AEQD에 변환한다. 원점은 경도 126.6535/위도 37.4506이다. Unity의 기존 `111320*cos(lat0)`·`110980` 로컬 그리드와 AEQD는 구분하고 명시적으로 변환한다. debug 각 경로의 polyline은 AEQD XY, unity_polyline_xz_m은 기존 Unity XZ다. 높이는 제공하지 않는다.
- 기준 좌표 3개의 왕복 오차 ≤0.01m와 Unity 도로/OSM 중심선 최대 약 0.00066m 잔차를 검사했다. 이는 파일 좌표/반올림 일치 검사이며 현장 지도 정확도·씬 실시간 표시 검증이 아니다.

### 확장 OSM의 미연결 5곳 조사

```powershell
python -m campus_sim.connection_audit
```

이 명령은 전체 확장 캐시의 대안 조사용이다. 초기 [조사 보고서](MapResearch/CONNECTION_AUDIT.md)의 후보를 사용자 요청에 따라 v2 기본 지도에 반영했다. 현재 실행 증거는 `artifacts/osm_extension_runtime_validation.json`이며 이전 기록은 보존한다. 지도 생성은 조사 결과 파일을 읽지 않고 설정에 고정한 OSM 선분과 원본 자료를 사용한다.

기준 순항속도는 사용자 요청으로 30km/h로 변경했다. 이는 최소 강제 속도가 아니며 원본의 더 낮은 숫자형 제한을 우선한다. 실제 차량 가감속·혼잡·안전 제어는 별도 미구현이다. 비용 snapshot ID에는 설정 hash를 포함해 이전 속도 계산과 구분한다. 기존 `artifacts/osm_fifo_map_validation.json`은 2m/s 시점의 기록으로 보존한다.

## 기다리지 않고 흐름 확인

```powershell
python -m campus_sim.demo_fifo
python -m campus_sim.demo_fifo --extensions
python -m campus_sim.demo_fifo --battery
```

정문→도서관 요청 3개를 접수하고 첫 목적지 도착과 각 복귀 시각을 출력한다. 마지막에는 3건 완료와 V01~V03 복귀를 확인한다. 코드에서 `MobilityService.from_osm_map(clock=lambda: 0.0)`와 `service.step(원하는_시각)`으로 직접 검증할 수 있다. 큰 시간 간격도 복귀와 충전 완료 이벤트를 시간순으로 처리한다. 시각은 유한한 비감소 값이어야 한다. 이전 가상 6거점은 `python -m campus_sim.demo_fifo --synthetic`으로 명시해서 실행한다.

`--extensions`는 정문에서 출발·복귀하는 하이테크/기숙사 1/기숙사 2/기숙사 3/역 요청을 3대에 FIFO 배정한다. 30km/h 가정 왕복 시간은 약 157.02/62.55/236.57/219.50/97.72초다. 승하차·신호·가감속은 미반영이다.

`--battery`는 하이테크 왕복 18건을 접수하여 각 차량 6회 운행과 총 6회 충전을 재생한다. 첫 충전은 가상 시각 약 471.06초에 시작하고 5,564.42초에 끝난다. 18건의 마지막 복귀는 약 6,035.48초, 마지막 충전 완료는 약 11,128.84초다. 모두 가상 시간이며 명령 실행 중 실제로 기다리지 않는다. 재현 기록은 `artifacts/battery_fleet_validation.json`이다. 과거 `osm_extension_runtime_validation.json`은 1대·배터리 제외 시점의 기록이다.

## 현재 API

- `GET /health`
- `GET /v1/landmarks`
- `GET /v1/debug/virtual-state` — 로컬 가상 운행 전용. 경로·출처·가정·Stop 후보·대기 사유를 포함하며 인증/세션 격리는 없다. synthetic는 시간 실행이 합성이라는 뜻이고 synthetic_geometry는 형상 출처를 구분한다.
- `POST /v1/requests`
- `GET /v1/owners/{owner_id}/requests`
- `POST /v1/requests/{request_id}/cancel?command_id=...&owner_id=...`

`command_id`는 요청 생성과 취소를 멱등하게 처리한다. 같은 명령을 다시 보내면 상태 전이를 반복하지 않고 처음 결과를 반환한다.

## 검증과 한계

```powershell
python -m pytest backend/tests -q -p no:cacheprovider
python -m ruff check backend/src backend/tests
```

Python 3.14.4에서 테스트 91개, Ruff 검사/format check, 기본 및 확장 거점 데모 실행을 확인했다. 원본 변경/누락/잘못된 좌표/보행로 허용/검증 승격 거부, 지도 재생성 바이트 일치, 세 좌표 기준점, 일방통행 및 실제 도로 길이에 따른 복귀·재배차를 검사했다. 100개 합성 OD의 Dijkstra 비용은 독립 Floyd–Warshall과 대조했으며 A* 비교 T03 전체 완료는 아니다. HTTP TestClient와 서버 tick 자동 복귀를 검증했다. 이 환경에서 TestClient는 Windows 로컬 소켓 권한과 쓰기 가능한 별도 pytest basetemp가 필요했다. Starlette httpx TestClient deprecation 경고 1개가 있다. 의존성 고정은 이 Windows/Python 환경의 smoke test 기준이며 Python 3.11·다른 OS 실행은 미검증이다.

기존 owner_id 입력 기반 API에는 인증된 principal, 사용자별 command_id 충돌 검사, 영속 저장이 없다. Unity schema_version=3과 fixture는 변경하지 않았으며 debug 응답은 별도다. 새 runtime schema_version=4를 확정하거나 도입한 작업이 아니다.

## 다음 단계

A* 비교, 실제 지도/Stop 검증, Unity WebSocket·차량 제어·센서 기반 도착 판정을 연결한다. 실차 배터리 제원·충전소 검증, 대기 전력 실측과 다중 차량 충돌/자원 경합 처리를 확장한다.
