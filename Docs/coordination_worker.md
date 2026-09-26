# 서비스 연결 CBS 작업 프로세스

2026-09-26 · v0.4.1.0 체크포인트

## 역할과 동작

`ServiceCoordination`을 서비스에 명시적으로 설정하면 서비스 tick이 현재 운송 요청과 정차 위치에서 계획 입력을 만든다. 계산은 별도 Python 프로세스에서 수행한다. 결과는 검토용 proposal이며 `executable=false`다. 경로·예약·제어 명령으로 변환하지 않는다.

작업 입력에는 버전 고정 합성 graph, 현재 ASSIGNED/IN_TRANSIT 요청의 출발/목적지 node, 승객/화물·접근성, 명시적 자원 overlay, 비용과 폐쇄 edge가 들어간다. 알려진 점유 정책의 차량 폭을 사용하고, 폭이 없는 합성 입력은 기존 planner의 미지정 폭 범위를 유지한다. 동적 객체 Ground Truth는 받지 않는다.

자체 CBS의 전처리와 탐색을 작업 프로세스에서 수행한다. 서버에는 실행 중인 작업 한 개와 최신 대기 작업 한 개만 둔다. 중간 요청은 합치며, 실패 후 동일 입력 재시도는 proposal 유효 기간 동안 억제한다. 프로세스 오류는 서비스 시계를 중단하지 않는다. 깨진 프로세스 pool은 다음 요청에서 재생성한다. 종료 시 서버 시계를 먼저 멈추고 비동기 정리로 작업 프로세스 종료를 기다린다.

## 입력과 결과의 유효성

- 합성 지도, 최대 256 nodes/1,024 edges, 현재 localization과 유효 센서가 필요하다.
- 모든 참여 차량은 명시한 node 오차(0초과~0.15m 이내) 안에서 정차 속도 0.01m/s 이하이어야 한다. 임의 중간 edge 연결을 만들지 않는다.
- 요청/목적지/접근성, route ID·geometry·speed profile·progress, ego pose·session, 서버 run, map/비용/폐쇄, 예약 claim·대기·lease·정책·영역을 동결한다. 다음 tick 및 매 결과 조회에서 동일성을 재검사한다.
- 위치/센서 tick 번호만 갱신되고 정차 pose와 유효성이 같으면 기존 작업을 유지한다. 센서-위치 handoff 도중에는 유효성 검사가 실패할 수 있고 결과를 폐기한다.
- 센서 오류·stale, 이동, 노드 이탈, context 변경 또는 제출 후 유효 기간 초과 결과는 폐기한다. 기본 결과 나이는 실제 단조 시계 기준 5초다.
- 이미 진입한 lease와 미계획 점유는 아직 시간 제약으로 변환하지 못하므로 입력을 거부한다. 진입 전 grant의 순서를 실행 계획에 강제하지 않으므로 proposal은 기존 grant를 대신할 수 없다.
- bounded AVOID 우회 정책은 CBS와 아직 결합하지 않았다. 해당 상태에서는 작업을 제출하지 않는다. 현재 혼잡 비용과 명시적 폐쇄는 snapshot에 반영한다.

## 설정 예시

기본 서버 동작은 변경하지 않는다. Python 시나리오에서 필요한 합성 자원 overlay를 검증한 뒤 다음처럼 opt-in 한다. 기존 benchmark의 node/edge ID는 해당 graph에서만 유효하다.

```python
from campus_sim.coordination_runtime import ServiceCoordination

service.coordination = ServiceCoordination({
    "quantum_s": 2, "horizon": 30, "clearance_ticks": 1,
    "edge_resources": edge_resources, "node_resources": node_resources,
    "node_tolerance_m": 0.1,
    "provenance": "Explicit synthetic time and resource assumptions",
    "algorithm": "cbs-disjoint",
    "limits": {"ct_nodes": 2000, "low_level_expansions": 50000, "wall_time_s": 3},
})
# Service.advance polls automatically; every read rechecks service context.
proposal = service.coordination.read(service)
```

API lifespan은 enabled coordinator를 종료한다. 독립 스크립트에서는 `await service.coordination.close()`가 필요하다. 별도 CLI 플래그와 클라이언트 proposal UI는 아직 없다. 검증/탐색 한도는 cooperative deadline이며 강제 process hang watchdog은 아니다.

## 검증과 남은 통합

실제 spawn 프로세스에서 세 차량 CBS를 계산하는 동안 서비스 시계와 센서 stale 정지가 계속 동작했다. 서비스에 연결한 세 운송 임무(승객 2, 화물 1)에서 새 telemetry를 공급하며 proposal 수신을 검증하고, invalid 센서 입력 후 폐기했다. 이 테스트의 임무는 승차 완료 상태로 준비하며 Unity Physics 운송 자체를 재검증하지 않는다. 입력 동결, 변경/만료/오류 폐기, 작업 병합·재시도 제한, 종료 및 점유 상태 거부도 검사한다.

실제 시간 계획에 따른 경로/예약 실행, 이동 중 남은 경로 재계획, 점유 제약 병합, AVOID 정책, Physics 지연·제동 후 복구, 전체 부하 측정이 남는다. T25/E4/M5 및 전체 MVP 완료가 아니다. 증거는 `artifacts/validation/2026-09-26-coordination-worker/`를 따른다.
