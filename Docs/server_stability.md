# 서버·클라이언트 프로토타입 검증 및 안정성 메모

프로젝트 버전 **0.3.1.0** · 2026-09-25 작업본

## 검증 범위

Python 서비스와 WebSocket 명령 처리 함수, schema-v3 snapshot builder를 합성 환경에서 검증했다. 기본 API 서버는 `maps/fixtures/campus-synthetic-6.json`에서 V01~V03의 독립 합성 runtime을 활성화한다. M1 회귀 테스트의 `synthetic_fixture()`는 V01 단일 차량이다. Unity preview scene은 세 차량 prefab에 binding되어 있지만 Editor/Player 실행 및 세 차량 동시 물리 주행은 미검증이다.

검증 실행:

```powershell
python -m pip install -e ".\backend[dev]"
python -m pytest backend/tests -q -p no:cacheprovider
```

초기 Python 서비스 검증 기록은 44개 테스트 통과였다. 최신 suite 결과(backend와 MapData 포함), 새 WebSocket 주행 흐름 검사 및 재실행한 C# source smoke는 아래 최신 확인 항목을 따른다. 테스트 환경은 FastAPI 0.141.1, Starlette 1.7.0, Pydantic 2.13.5, httpx 0.28.1, pytest 8.4.2, Ruff 0.16.8이며 임시 virtual environment로 lockfile에 고정되지 않았다. Starlette/httpx TestClient deprecation 경고가 남아 있다.

테스트는 요청→A* 픽업 경로→픽업 서비스→목적지 이동→하차 서비스→완료, snapshot 경로/위치, edge 속도 제한, step-free 경로, 취소와 취소 후 재배정, 대기 요청의 순차 배정, idempotency, 잘못된/불가능한 요청, 승객별 snapshot 격리, Operator command 거부를 다룬다. 추가 ASGI 통합 테스트는 FastAPI `TestClient`로 HTTP create/list/cancel/owner filter와 WebSocket subscribe/ping/create ACK/snapshot, schema 거부·close code, Mobile subscriber 누락, oversized payload와 subscribe timeout, Operator ego-localization 수신 및 runtime pose snapshot 반영을 검증한다. localization은 unknown vehicle/map mismatch/runtime 미초기화/유한값/session tick, stale pause 및 관측 기반 pickup→dropoff→complete 경계를 검사한다. 기존 fake socket 단위 검증도 유지한다.

## 발견해 수정한 결함

- 같은 명령 ID를 서로 다른 승객이 쓰면 전역 명령 캐시가 ACK를 섞을 수 있었다. 캐시 키를 작업 종류·승객 ID·명령 ID에 scope하고 SHA-256으로 고정했다.
- 캐시된 ACK가 변경 중인 Request 객체를 공유해 중복 명령 재시도 시 최초 결과가 달라질 수 있었다. 캐시에 응답의 깊은 복사본을 저장한다.
- 이전 승객의 완료 요청이 같은 차량에 계속 연결되어, 그 차량이 다른 승객을 태운 뒤에도 차량 위치/경로가 이전 승객 snapshot에 노출될 수 있었다. 모바일 snapshot에는 해당 승객의 진행 중인 요청 차량만 투영한다.
- 합성 차량이 edge의 허용 속도보다 빠르게 이동할 수 있었다. 경로 geometry segment마다 edge 제한과 차량 합성 최고속도 중 낮은 값을 적용한다.
- 용량상 fleet 차량이 하나도 맞지 않는 요청은 거부하고, 현재 운행 runtime이 없는 화물 요청도 무기한 대기시키지 않고 명시적으로 거부한다. 적합한 차량이 모두 바쁜 요청은 대기하고 차량이 해제되면 Greedy 우선순위로 재배정한다.
- 같은 출발/목적 landmark 요청, 타인의 요청 취소, 승차 서비스 이후 취소를 거부하는 경계를 확인했다.
- localization 기반 경로에서 차량이 이미 픽업 node에 정차해 route가 단일 점인 경우에도 pickup service를 시작하도록 수정했다. 요청 접수 전에 픽업→목적지 구간의 도달 가능성을 검증해 픽업 후 실패할 요청을 사전에 거부한다. 이전에는 route point가 2개 미만이면 진행을 건너뛰어 요청이 정체될 수 있었다. 회귀 테스트에서 pickup service→2초 dwell→IN_TRANSIT 및 dropoff 경로 부여를 확인했다.
- ETA는 목적지 Stop 도착까지 남은 시간으로 정의하고 픽업 이동·승차 dwell·목적지 이동을 요청 상태별로 반영한다. ego pose를 active synthetic route에 투영해 ETA를 갱신하며, route match 허용 범위를 벗어나거나 역방향 위치 노이즈가 오면 진행도를 반영하지 않는다. 요청 생성 때 service type과 step-free 조건을 포함해 목적지 leg 도달 가능성을 검사하므로 픽업 후 막힐 수 있는 요청을 배정 전에 거부한다.

## 안정성 판정과 남은 한계

현재 결과는 **합성 3차량의 서비스·Greedy 배정 및 HTTP·ASGI WebSocket 처리 경로를 검사한 프로토타입**이다. 최신 Python 테스트와 source-level C# smoke는 Unity Player/실제 Physics 주행, 실제 지도 또는 부하 목표 달성을 증명하지 않는다.

2026-09-25 최신 확인: 현재 working tree 기준 Python backend 77개와 MapData 9개 테스트(총 **86 passed, 1 deprecation warning**). 테스트에는 V01~V03별 독립 요청 흐름, 기본 API fleet 초기화, 600초 장기대기/매 3번째 배차 fairness가 포함되며 Ruff도 통과했다. 추가된 ASGI WebSocket 통합 테스트가 PC ego 위치 보고·요청 생성·pickup dwell·목적지 이동·dropoff dwell·`COMPLETED` snapshot을 끝까지 확인하고, 별도 PC sensor-observation 경로가 현재 map/session/ego tick에 결합한 bounded frame을 ACK/거부하는지 검사한다. polar/local 좌표 불일치, LIDAR 상대속도 포함, 64 detection 초과, 중복 sensor tick, stale ego 및 pose tick mismatch를 거부한다. 최신 회귀 테스트는 센서 프레임이 아직 도착하지 않은 새 ego pose tick에서 주행 상태가 REPLANNING으로 유지되는지 확인한다. `/health` 응답의 선택된 synthetic `map_version`·data status·graph 크기도 검증한다. Unity C# domain/networking/presentation source compile과 sensor DTO edge-case self-test가 통과했다. loopback ASGI smoke도 PC/Mobile 동시 접속, ego localization·fixture sensor frame ACK, PC 승객/화물 생성·취소, Mobile 최초 ACK 유실 뒤 재접속·동일 명령 재전송 및 단일 요청 생성을 통과했다. 단, sensor frame은 harness에서 만든 입력이며 Unity Physics Raycast 결과가 아니다. OSM review validator는 공식 접근 근거와 현장 측정 근거를 모두 요구하고 원본 명시적 접근 금지 태그와의 충돌을 검출한다. 기존 44/46 테스트 기록은 당시 범위의 이력으로 보존한다.

| 항목 | 현재 상태 | 다음 검증/조치 |
|---|---|---|
| FastAPI HTTP/ASGI WebSocket 및 lifespan clock | 확인 범위 내 통과. 최신 실행은 backend 61개와 MapData 9개 테스트로 총 70개이며, HTTP·WebSocket·clock·위치 기반 trip 완료를 검증한다. clock은 클라이언트가 없어도 진행하며 snapshot read는 상태를 변경하지 않는다. | 외부 네트워크 10Hz cadence/load 및 모바일 LAN 연결은 별도 검증한다. Starlette/httpx TestClient deprecation 경고가 남아 있다. |
| Unity C# WebSocket client/server 왕복·재접속 | Adapter에 connection generation 격리, 0.5초 시작·최대 15초 exponential backoff, ACK 전 pending command 보관 및 재접속 후 동일 message ID 재전송을 추가했다. [Mono smoke harness](../AgentScripts/UnityWebSocketSmoke/README.md)에서 C# source compile 및 loopback Python ASGI 연결을 실행해 PC·Mobile 동시 연결, snapshot, ego localization ACK, constructed sensor frame ACK, PC passenger/cargo create/cancel, 최초 passenger ACK drop 후 재접속·동일 명령 재전송(accepted ACK 1회, server request 1건)을 확인했다. DTO invalid polar pair/LIDAR relative speed/65 detections edge-case self-test도 통과했다. | Unity Editor/Player에서의 실제 Physics hit, SynchronizationContext, 씬 수명주기, Unity UI·모바일 실기기는 이 harness 범위에 없다. |
| Unity→Python ego localization runtime alpha | PC_Operator C# source가 camelCase `ego_localization`을 전송하고 서버가 vehicle/map/sessionId/observedTick/position/heading/speed를 검증해 runtime x/y/z/heading/speed와 snapshot pose를 갱신한다. Localization mode는 server synthetic pose integration을 멈춘다. fresh 관측에서만 graph-route endpoint 1.0m 이내·≤0.1m/s를 도착으로 간주하고 2초 service를 진행한다. 0.5초 stale이면 service/도착 전이를 멈추고 snapshot에 `REPLANNING/STALE_LOCALIZATION`을 표시한다. 노드에서 1m 초과 pose는 unverified connector를 만들지 않고 route 생성 거부한다. Localization pose는 합성 route에 1m 이내로 투영하고 진행 거리를 단조 증가시켜 잔여 route ETA를 갱신한다. 이 값들은 합성 초기 threshold다. 최신 Python 통합 테스트는 위치 보고→요청→service→완료 snapshot까지 확인한다. | PC operator scene의 mapVersion guard는 비어 있어 합성 map 좌표를 캠퍼스 장면에 표시하지 않는다. 별도 synthetic preview는 exact mapVersion·V01 binding에서만 route follower를 허용한다. presentation/test assembly는 새 코드를 compile했지만 Unity Test Runner·씬 재임포트·Player telemetry 왕복은 미실행이다. node waypoint까지 저속 kinematic 이동은 합성 시각 alpha이며 실제 Unity Physics Raycast 검증·TTC/RRT 충돌 판정이 없고 follower now applies bounded kinematic deceleration after authority revocation or route staleness; vehicle-dynamics braking remains unverified. Python safety gate는 localization 차량의 fresh/valid 센서 요구, 정지거리 내 forward-sector hit stop 및 1초 clear resume hold를 snapshot motion authority에 연결했다. 다음 단계는 Unity Editor/PlayMode에서 SensorRig Physics hit와 PC WebSocket ACK를 검증하고 경로/footprint-aware TTC·제동·횡단 위험 처리로 확장하는 것이다. |
| SensorObservation 생성·입력 계약 | PC_Operator WebSocket은 검증된 `sensor_observation`을 받고 session/map/ego tick에 바인딩한다. Unity source의 `VehicleRaycastSensorRig`는 최대 64 beam·10Hz 목표의 2D RaycastNonAlloc LiDAR abstraction을 만들고 latest ego pose tick과 함께 전송한다. buffer 포화 프레임은 invalid 처리한다. Mono loopback은 constructed frame의 C# serialization과 server ACK를 통과했고 Python 63-test suite에서 계약 경계를 검사하며 차량별 sensor stream 상한과 세션 교체 시 이전 frame 제거도 확인한다. | ProjectSettings의 Vehicle/Pedestrian layer를 추가했고 spawner는 차량 collider hierarchy를 Vehicle layer로 둔다. pedestrian actor가 없으므로 그 분류와 실제 Unity Physics hit, 센서 freshness는 Python 0.3초 gate와 stale/invalid stop, 1초 clear resume hold까지 구현했다. 다만 실제 Physics hit, 경로/footprint-aware TTC·RRT·제동과 센서 정확도는 미검증/미구현이다. |
| 인증·권한 | 미구현. `subscriberId`는 구독 메시지에서 클라이언트가 정하는 식별자이며 인증 토큰이 아니다. snapshot 필터는 편의용 분리일 뿐 보안 경계가 아니다. | 실제 LAN 공유 전 인증된 세션 ID와 서버 측 소유권 검증을 구현한다. 현재 서버는 신뢰된 로컬 개발 환경에서만 사용한다. |
| 지속성·메모리 상한 | 요청과 idempotency 캐시는 프로세스 메모리에 무기한 쌓이며 재시작 시 모두 사라진다. | 저장소/TTL·상한·정리 정책, 프로세스 재시작 후 복구를 설계한다. 동시 세션 부하 때 메모리를 측정한다. |
| 시뮬레이션 tick | 20Hz 고정-step clock은 FastAPI lifespan에서 실행되며 연결된 snapshot 구독자가 없어도 진행한다. snapshot 생성은 read-only다. | 실제 배포/부하 상황에서 tick jitter와 loop p95를 측정하고 Unity Physics tick 동기화 계약을 검증한다. |
| 취소 후 경로 재개 | 취소 도중 차량 현재 위치를 유지하고 가장 가까운 graph node로 잇는 직선 synthetic recovery segment를 만든다. 이는 검증된 도로 edge가 아니며 실제 지도나 접근성 판단에 사용할 수 없다. | 실제 시뮬레이션에서는 edge 위 투영/안전 정차와 localization 계약으로 교체하고, 계획 결과에 검증되지 않은 connector가 들어가지 않는지 검증한다. |
| 차량 물리·센서·실지도 | 분리된 합성 미리보기에서만 서버 polyline을 따라 V01 kinematic Rigidbody를 최대 1m/s로 waypoint 이동시키는 alpha가 있다. 서버 위치 관측과 synthetic request transition 연결은 코드상 준비됐으나 Editor/Player 왕복은 아직 검증되지 않았다. SensorRig source는 RaycastNonAlloc hit frame을 만들고 vehicle collider layer 분류를 준비했지만 pedestrian prefab/생성기와 PlayMode hit 검증은 없다. 이 follower는 waypoint 정렬, 0.5초 route timeout, 제동거리 기반 감속만 처리하고 충돌 안전을 제공하지 않는다. ETA와 2초 승하차 시간도 합성 가정이다. | Editor/PlayMode에서 합성 request→drive→arrival→complete와 SensorRig hit/ACK를 검증한다. pedestrian actor 분류와 축·ground contact를 확인하고 TTC/RRT, 긴급 정지·재출발 및 실제 지도 검증을 구현한다. |
| 동시성·성능·재접속 | 단일 C# client source smoke에서 서버 재기동 reconnect와 pending command replay는 확인했다. 여러 WebSocket 공유 상태, 재접속 폭주 및 50세션/300 보행자 목표는 미검증이다. | 최대 세션 및 재접속 폭주, 중복 명령, 요청 flood, snapshot p95/FPS·메모리를 따로 측정한다. |

Unity transport/UI 연동과 인증 전에는 실사용 준비 완료로 판정하지 않는다. 테스트는 `backend[dev]`에 선언한 pytest/FastAPI/httpx 의존성을 설치한 개발 환경에서 실행한다.
