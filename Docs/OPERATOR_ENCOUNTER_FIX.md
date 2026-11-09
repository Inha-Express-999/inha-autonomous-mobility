# 관제 장애물 표시·보행자 횡단·날씨 전환 보완

2026-11-02 · VERSION 0.5.6.0 작업 트리 · 미커밋

## 원인과 변경

- 자동 점군/흑백 표시의 `encounter` 조건은 차량 전후방 7m와 차폭+0.8m 안의 모든 반환점에 반응했다. 정적인 연석·보도·외벽도 이 조건을 반복 충족했다. 5호관 Stop `(28.69, 126.12)` 부근의 실제 asphalt Collider 위 11개 정적 Raycast 표본에서 주로 `Junction aware kerbs`와 `Continuous pedestrian network` 윗면을 확인했다. 예를 들어 차로 방향으로 0m인 표본의 연석 반환은 센서 기준 약 `(2.04, -1.77, 2.51)m`, 법선 Y≈1이었다. 이는 보행자가 새로 나타났다는 근거가 아니다. 이 표본은 실제 차량 주행·물리 접지 시험과 구분한다.
- 정적 지도 반환만으로 자동 표시를 켜지 않는다. 전방의 보행자·차량·미분류 반환은 조기 표시를 유지하고, 고정 구조물도 Python이 실제 위험을 보고하면 표시한다. 센서 반환을 삭제하거나 Python TTC·정지거리·5회 clear scan 및 제동 여유를 완화하지 않는다. 따라서 벽에 실제로 접근하는 경로의 제동은 계속 유지한다.
- 보행자 등장 버튼은 선택 차량을 실제로 follow할 때만 표시한다. 추적 해제 후 선택값이 남아 있거나 클릭 이벤트가 직접 호출되더라도 생성하지 않는다. 실행 결과/거절 사유와 대기·횡단·완료 상태는 지도 아래 안내에 표시한다. 선택 차량의 속도 패널에는 대기·충전·이동·양보 상태를 함께 표시한다.
- 횡단 시험은 지도 도로 edge에서 전방 중심점과 접선 방향을 구하고, 실제 `Continuous asphalt network` Collider의 양쪽 경계를 찾는다. 차량 yaw 대신 도로 접선에 수직으로, 차량 차로에 가까운 가장자리에서 반대편까지 걷는다. 기존 5초 주변 확인 애니메이션을 유지하며 이동 중에는 ego 속도×5초만큼 생성 위치를 앞당긴다. 인근 후보를 5개 확인하고, 0.25m 간격으로 실제 지면 높이를 따라 걷는다. 보행자 footprint 전체의 지지 높이를 확인하여 낮은 연석에 capsule이 걸렸다는 이유로 생성 자체를 거절하지 않는다. 0.35m 이상 연속 단차·벽·지면 누락은 계속 거절한다. 배치용 Unity Ground Truth를 Python에 전달하지 않는다.
- COZY의 `Stop()`은 기존 파티클이 수명을 다할 때까지 남긴다. 선택이 바뀌는 프레임에 해당 weather sphere의 particleFXParent 아래 ParticleSystem을 `StopEmittingAndClear`로 정리한 뒤 새 효과를 적용한다. 같은 날씨를 반복 적용할 때에는 정리하지 않는다. 공유 패키지 원본은 수정하지 않는다.
- 눈 파티클의 시작 크기를 원본의 1.8배로 적용한다. 반복 선택 시 누적 확대하지 않고, 입자 수와 방출량은 유지한다.

## 검증

- Unity Editor 6000.3.21f1: `InhaExpress/Validation/Run Operator Encounter Regression` 메뉴. 관제 UI/센서 7개, COZY 1개, 보행자 2개를 묶어 실행한다. 원본 XML은 `artifacts/validation/2026-11-02-operator-encounter/playmode.xml`에 저장한다.
- 기울어진 차량 heading과 도로 방향이 달라도 횡단 양 끝이 도로 양쪽에 있고 이동이 도로에 수직인지 검사한다. 추적 해제 후 클릭 방어, 정적 반환 보존과 위험 피드백 표시, 날씨 전환 직후 기존 파티클 수 0을 검사한다.
- Python: `PYTHONPATH=backend/src;.utmp/fifo-dijkstra-deps`에서 `python -m pytest backend/tests/test_lidar_corridor.py backend/tests/test_pc_lidar.py -q -p no:cacheprovider -k 'not websocket'` — 68 통과, WebSocket 1개 제외. Python 제어 로직은 이번 수정으로 바꾸지 않았다.
- 실제 Unity/Python 운행: V01이 169초 기록 후 5호관 Stop에 `ARRIVED`했다. 830개 표본 중 `YIELDING`은 15개이며, 중간에 사용자가 생성한 `Synthetic Pedestrian 1`의 실제 Raycast 관측·감속·정지·재출발이 기록됐다. 따라서 무보행자 통제 실험으로 보고하지 않는다. 5호관 도착 구간은 연석/외벽 반환을 유지한 상태에서 자동 표시가 꺼져 있었다. 원본 `live-building5.tsv`와 `live-building5-summary.json`을 보존한다. 이 실행은 후속 연석 보행 경로·화면 안내·눈 크기 보완 이전의 센서 표시 수정 상태다.

프로토콜·지도 schema 및 프로젝트 버전을 변경하거나 커밋하지 않았다. PC/모바일 Player 빌드, FPS/배치/메모리와 전체 캠퍼스 경로의 안전성은 이 회귀시험의 검증 범위가 아니다. 새 렌더링 에셋이나 추가 상시 탐색 루프는 도입하지 않았으며 도로/횡단 검색은 버튼 호출 시에만 수행한다.
