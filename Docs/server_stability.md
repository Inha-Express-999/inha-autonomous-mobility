# 서버·클라이언트 프로토타입 검증 및 안정성 메모

프로젝트 버전 **0.2.2.0** · 2026-09-24

## 검증 범위

Python 서비스와 WebSocket 명령 처리 함수, schema-v3 snapshot builder를 합성 환경에서 검증했다. 서버 경로는 `maps/fixtures/campus-synthetic-6.json`이고, 차량 1대 V01만 실제 운행 상태로 시뮬레이션한다.

검증 실행:

```powershell
python -m pip install -e ".\backend[dev]"
python -m pytest backend/tests -q
```

결과: **20개 테스트 통과**. Python `compileall`과 `git diff --check`도 통과했다. 회귀 확인으로 `route-compare`를 다시 실행해 합성 지도 30개 directed OD 모두 reachable, Dijkstra/A* 경로 비용 일치 30/30, reachability mismatch 0을 확인했다. 이는 이전과 같은 합성 fixture에서의 단일 실행 결과이며 성능 보증이 아니다.

테스트는 요청→A* 픽업 경로→픽업 서비스→목적지 이동→하차 서비스→완료, snapshot 경로/위치, edge 속도 제한, step-free 경로, 취소와 취소 후 재배정, 대기 요청의 순차 배정, idempotency, 잘못된/불가능한 요청, 승객별 snapshot 격리, Operator command 거부를 다룬다. 추가 ASGI 통합 테스트는 FastAPI `TestClient`로 HTTP create/list/cancel/owner filter와 WebSocket subscribe/ping/create ACK/snapshot, schema 거부·close code, Mobile subscriber 누락, oversized payload와 subscribe timeout을 검증한다. 기존 fake socket 단위 검증도 유지한다.

## 발견해 수정한 결함

- 같은 명령 ID를 서로 다른 승객이 쓰면 전역 명령 캐시가 ACK를 섞을 수 있었다. 캐시 키를 작업 종류·승객 ID·명령 ID에 scope하고 SHA-256으로 고정했다.
- 캐시된 ACK가 변경 중인 Request 객체를 공유해 중복 명령 재시도 시 최초 결과가 달라질 수 있었다. 캐시에 응답의 깊은 복사본을 저장한다.
- 이전 승객의 완료 요청이 같은 차량에 계속 연결되어, 그 차량이 다른 승객을 태운 뒤에도 차량 위치/경로가 이전 승객 snapshot에 노출될 수 있었다. 모바일 snapshot에는 해당 승객의 진행 중인 요청 차량만 투영한다.
- 합성 차량이 edge의 허용 속도보다 빠르게 이동할 수 있었다. 경로 geometry segment마다 edge 제한과 차량 합성 최고속도 중 낮은 값을 적용한다.
- 용량상 fleet 차량이 하나도 맞지 않는 요청은 거부하고, 현재 운행 runtime이 없는 화물 요청도 무기한 대기시키지 않고 명시적으로 거부한다. V01이 바쁜 동안 V01이 처리할 수 있는 승객 요청은 대기 후 재배정한다.
- 같은 출발/목적 landmark 요청, 타인의 요청 취소, 승차 서비스 이후 취소를 거부하는 경계를 확인했다.

## 안정성 판정과 남은 한계

현재 결과는 **합성 단일 차량의 서비스·HTTP·ASGI WebSocket 처리 경로를 확인한 프로토타입**이다. 아래 항목은 20개 테스트로 증명되지 않았다.

| 항목 | 현재 상태 | 다음 검증/조치 |
|---|---|---|
| 실제 FastAPI HTTP/ASGI WebSocket 경로 | 확인 범위 내 통과. 임시 테스트 의존성 FastAPI 0.141.1, Starlette 1.7.0, httpx 0.28.1, pytest 8.4.2로 20개 테스트를 실행했다. | 브라우저·Unity NativeWebSocket 실소켓과 모바일 LAN 연결은 별도 검증한다. 테스트 실행에서 pytest assert rewrite와 Starlette/httpx TestClient deprecation 경고가 발생했다. |
| Unity WebSocket 직렬화·메인 스레드 snapshot 적용·화면 상태 | Unity Editor log에서 `FixtureStatusPresenter`의 `IClientCommandSource` namespace 오류를 발견해 `using InhaExpress.Client.Networking`을 추가했다. 수정 후 Editor 재컴파일 성공은 아직 확인되지 않았고 Player 실행도 하지 않았다. | Unity Editor의 후속 compile에서 오류 0을 확인하고 PC/Mobile transport, JSON roundtrip, snapshot sequence 및 UI 상태를 실행 검증한다. |
| 인증·권한 | 미구현. `subscriberId`는 구독 메시지에서 클라이언트가 정하는 식별자이며 인증 토큰이 아니다. snapshot 필터는 편의용 분리일 뿐 보안 경계가 아니다. | 실제 LAN 공유 전 인증된 세션 ID와 서버 측 소유권 검증을 구현한다. 현재 서버는 신뢰된 로컬 개발 환경에서만 사용한다. |
| 지속성·메모리 상한 | 요청과 idempotency 캐시는 프로세스 메모리에 무기한 쌓이며 재시작 시 모두 사라진다. | 저장소/TTL·상한·정리 정책, 프로세스 재시작 후 복구를 설계한다. 동시 세션 부하 때 메모리를 측정한다. |
| 시뮬레이션 tick | 요청 진행은 snapshot 생성 때 `advance()`를 호출해 진행한다. 연결된 snapshot 구독자가 없으면 HTTP로 만든 요청은 자동 진행하지 않는다. | Unity 권위 tick 또는 독립적인 서버 clock/tick 계약을 정하고 서버 연결 유무와 무관한 진행 의미를 검증한다. |
| 취소 후 경로 재개 | 취소 도중 차량 현재 위치를 유지하고 가장 가까운 graph node로 잇는 직선 synthetic recovery segment를 만든다. 이는 검증된 도로 edge가 아니며 실제 지도나 접근성 판단에 사용할 수 없다. | 실제 시뮬레이션에서는 edge 위 투영/안전 정차와 localization 계약으로 교체하고, 계획 결과에 검증되지 않은 connector가 들어가지 않는지 검증한다. |
| 차량 물리·센서·실지도 | 합성 polyline을 제한속도로 따라가며 정지/가속·회전·충돌·보행자·센서·Unity Physics를 계산하지 않는다. ETA와 2초 승하차 시간은 합성 가정이다. | Unity 차량 prefab/Physics, 위치·heading 왕복, 센서 안전제어 연결 전까지 실제 주행으로 표현하지 않는다. |
| 동시성·성능·재접속 | 여러 WebSocket 공유 상태, 재연결, 50세션/300 보행자 목표는 미검증이다. | 최대 세션 및 재접속 폭주, 중복 명령, 요청 flood, snapshot p95/FPS·메모리를 따로 측정한다. |

Unity transport/UI 연동과 인증 전에는 실사용 준비 완료로 판정하지 않는다. 테스트는 `backend[dev]`에 선언한 pytest/FastAPI/httpx 의존성을 설치한 개발 환경에서 실행한다.
