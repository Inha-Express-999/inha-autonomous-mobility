# CBS 오프라인 작업 체크포인트

2026-09-26 · project version 0.4.0.0

자체 시간 확장 A*와 두 차량 제약 분기를 사용하는 유한 horizon CBS, 고정 우선순위 예약 비교를 구현했다. 목표는 도착 tick 합 최소화다. 노드 점유, 동일/역방향 edge의 전체 시간 구간, 명시적 공유 자원 및 clearance tick을 검사한다. 목적지 점유는 horizon 끝까지 유지한다. 합성 지도와 정적 통행/차량/접근성 제약만 허용한다.

실행: `campus-sim coordination-compare --scenario configs/coordination_benchmark.json --repetitions 5`

작은 그래프에서는 독립 전수 탐색의 최적 비용과 비교하고, 경로 불가·영구 목적지 점유·다중 tick·공유 자원·탐색 한도를 테스트했다. 전체 Python/MapData 374개가 통과했다.

세 차량 통로 fixture의 두 우선순위 사례를 각각 5회 실행했다. 우선순위 방식은 각 5/5 성공했으나 CBS는 각 5/5 CT 500개 한도를 초과했다. 짧은 horizon 사례는 양쪽 모두 성공하지 못했다. 실패 결과에는 실행 가능한 부분 계획을 반환하지 않는다. 결과와 입력/source hash는 `artifacts/validation/2026-09-26-cbs/`에 있다.

이는 오프라인 양자화 비교 기반이다. edge 비용을 tick 시간으로 올림하고 수기 자원 overlay를 사용한다. 연속 footprint, 센서 지연, 실제 물리 충돌, live lease/교착 복구와 제어 재계획을 검증하지 않는다. 모든 결과는 executable=false이며 live ResourceReservations 또는 actuator에 연결하지 않는다. CBS 성능 개선 또는 M5 완료를 주장하지 않는다.
