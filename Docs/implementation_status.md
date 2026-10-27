# 구현 현황

프로젝트 0.5.1.0 · 2026-10-27 커밋 체크포인트. 아래 미커밋 표시는 개발 당시 기록이며 이번 커밋에 포함한다. 과거 실행 버전과 검증 범위는 소급 변경하지 않는다. 커밋 검증: 기존 의존성 경로에서 `python -m campus_sim.map_builder` 재생성 및 `python -m pytest backend/tests/test_release_version.py -q -p no:cacheprovider` 1개 통과, `node --check backend/src/campus_sim/web/app.js` 통과. 전체 backend 및 물리 운행 대상 회귀는 장시간 실행 후 중단해 결과 미확정이며 통과로 간주하지 않는다. 기본 Python의 의존성 누락과 재생성 전 지도 입력 해시 불일치는 실행 경로 정정 및 기존 빌더 재생성으로 해소했다.

2026-10-27 배차 후 무한 대기 수정(미커밋, 해당 결함 VERIFIED): Unity MCP에서 세 차량의 연결·접지는 정상이나 `No automatic mission`, 60주년→비룡플라자 요청은 QUEUED인 상태를 확인했다. 물리 배차 후보 캐시가 매 관측의 Transform 부동소수점 오차를 차량 형상 변경으로 판단해 첫 후보를 반복 계산했다. 형상 비교에 절대 수치 허용 오차 1e-4(m/rad)를 적용하고, 경로 계산 중 관측이 잠시 오래되어도 계산 후보를 보존한다. 실제 배차/출발의 최신 관측 검사와 경로 제약은 유지한다. 상태 응답에 후보 수 진단을 추가하고 임무 시작 실패 사유를 보존한다. 관련 pytest 42개 통과(기존 Starlette 경고 1개); 수치 오차/실제 형상 변경/일시적 stale 회귀 3개 포함. 실제 새 세션에서 같은 요청을 V02에 배정해 TO_PICKUP/DRIVING, 시뮬레이션 3.67초 시점 실측 16.74km/h와 위치 이동을 확인했다. 근거: `artifacts/dispatch_wait_fix/before.json`, `after-start.json`. 검증 명령: `python -m pytest backend/tests/test_physical_dispatch.py backend/tests/test_physical_battery.py backend/tests/test_external_execution.py backend/tests/test_unity_drive.py backend/tests/test_drive_protocol4.py -q -p no:cacheprovider`. 기존 장애물 비활성 시뮬레이션 프로필을 유지했으며 전체 운송 완료·모바일·성능 검증을 의미하지 않는다. 커밋/릴리스 버전 증가는 수행하지 않았다.

프로젝트 0.5.0.0 · 2026-10-27 미커밋 추가: Unity Edit Mode에서 정류장·허브 마커를 이동·저장하고 Play하면 Python 지도 재생성·검증·서버 지도 동기화 후 실행한다. 같은 종류·ID의 포인트는 유지하고 쉼터 14개·차고 3개를 각 포인트의 자식으로 연결했다. 지도 갱신 후에도 모델 ID와 로컬 배치를 보존한다. 도로 밖 위치와 사용 중인 서버의 지도 교체는 거부한다. 차량·스폰은 별도이며 이동지원 접근성을 자동 승인하지 않는다. 상세: `Docs/STOP_HUB_EDITING.md`.

2026-10-26 허브 차고·배차·배터리 후속(미커밋, PARTIAL): 정문/후문/운동장 옆 주차장에 개방형 차고 3개를 만들고 V01/V02/V03 WheelCollider 차량을 각 차고의 기존 도로 위에 초기 배치했다. 세 차량의 `PARKED`/fresh WebSocket 연결을 확인했다. Unity의 FIFO/Greedy **드롭다운**이 웹과 같은 배차 정책을 바꾸며, 실제 물리 배차에도 요청/가용 순서 또는 공통 비용·우선순위 기반 선택을 적용한다. 후보 왕복 계획은 한 tick에 한 후보씩 계산해 제어 관측을 막지 않고, 실제 출발 직전 최신 pose를 다시 검사한다. 웹 공통 2,000Wh/15% 예비량/1,000W 충전 정책을 실제 이동 거리·차고 정차 관측에 연결하고 Unity 카드에 SOC/충전 상태를 표시했다. 직접 지정한 주행도 에너지를 소모한다. 관련 주행/배차/웹/배터리 회귀 **158개 통과**(의존성 deprecation 경고 1개).

실제 Greedy 요청(60주년→비룡플라자)은 후문 V02를 선택해 픽업·회차·하차했다. 복귀 중 기존 하이테크 외벽/도로 겹침이 재현되어 원본 에셋을 보존한 별도 메시로 서쪽 외벽과 MeshCollider를 함께 보정했다(서쪽 X 경계 281.614→291m, 동쪽 경계 유지). 차량 이동 없이 같은 임무가 재개되어 후문 차고 도착·예약 해제·충전 전환을 확인했다. 약 60.39초 동안 16.67Wh 충전됐으며, 이 실행에는 진단 정지가 포함되어 전체 소요시간을 성능 비교로 사용하지 않는다. 근거: `artifacts/high_speed_drive/garage-final-V02.csv`, `garage-charging-final.json`, `garage-dropdown-charging.png`, `hitech-clearance-saved.json`. 전체 거점 왕복과 다중 차량 충돌 조정, 모바일 성능은 미검증이다. 상세: `Docs/HOME_GARAGES.md`.

그보다 앞선 V01 연속 검증은 정문 차고→60주년 픽업→비룡플라자 하차→정문 차고 복귀를 완료했다(`cusp-service-progress.json`, `cusp-service-anniversary-biryong.csv`). 승객 요청 COMPLETED 후 READY와 예약 해제를 확인했다. 후속 코너 제한은 경로 검증 모드에서 2.4→3.6m/s²로 높여 동일 곡률 속도 상한이 약 22% 증가한다. 실제 V02 운송 구간은 회차 포함 약 81.67초였으며 모든 경로의 22% 시간 단축을 뜻하지 않는다.

2026-10-26 회차 구간 재계획 진단(미커밋, PARTIAL): 조향 여유 반경을 2.0/1.5/1.35/1.15배 순서로 검사하고, 방향 전환 정지 시 실제 후륜 축 pose에서 다음 구간을 cubic 곡선으로 다시 맞춘다. 조향 곡률과 차체 전체 도로 경계 검사를 통과한 경우만 출발한다. 매우 작은 오차에서는 기존 검증된 원호를 유지하며 큰 오차에서 재계획 불가 시 정지한다. 실제 실패 자세 재현을 포함한 관련 Python 69개 검사 통과. 별도 초기 자세(60주년 도착 실측 pose)를 사용한 Unity 진단에서 회차를 완료하고 후속 경로 39.82km/h 주행을 확인했다(`cusp-progress.json`, `turn-cusp-fixture.csv`). 전체 승객 운송/차고 복귀 완료와 구분한다.

2026-10-26 회차·표시 후속(미커밋, 검증 중): 실제 계측에서 기존 회차가 최대 조향각 30°에 포화되어 오차 보정 여유가 부족했다. 차체 전체가 도로 안에 드는 조건을 유지하면서 최소 반경의 1.5배, 1.35배, 1.15배 순서로 가능한 회차를 선택하도록 변경했다. 관련 회차/운송 15개 테스트 통과 후 별도 초기 자세의 Unity 회차 진단을 진행한다. 이 진단은 전체 운송이나 자동 복구 성공으로 세지 않는다. 차량 추적 화면의 허브 이름표 숨김은 컴파일과 실제 화면에서 확인했다(`hub-follow-result.json`: following=true, hub_pins=3, shown=0; `hub-follow-hidden.png`). 정류장 shelter 외형은 이동 가능하지만 정차 좌표는 Python Stop/도로 연결을 별도로 수정해야 한다.

2026-10-25 사용자 주행 피드백 후속(미커밋, 검증 중): 60주년→비룡플라자의 826m 전진 우회를 회차 비용과 비교해 약 351m+회차 8m로 줄이는 선택을 추가했다. 경로 검증 모드 코너 횡가속도 제한 1.2→2.4m/s², Unity 휠 조향 변화 상한 120°/s를 적용했다. 첫 실제 재주행은 픽업까지 약 118초→102초로 단축됐으나 회차 끝 자세 오차로 정지했다. 가상 원호 목표 보완 후에도 실제 짧은 후진 구간 끝에서 약 19° 방향 오차로 정지했다. 후속으로 원호 곡률 feedforward + 횡오차 + 전진/후진 방향별 heading feedback으로 바꾸고, 조향 지연/진입 방향 오차를 포함한 관련 테스트 24개를 통과했다. 재주행 검증이 남아 있으며 완료로 판정하지 않는다. 앞선 UI 운송은 하차 완료 후 복귀 중 하이테크 건물 충돌체에 막혔다. V01 정문 차고 복귀와 전체 경로 검증은 이 결함을 포함해 계속 진행한다.

2026-10-25 Unity 요청 UI 연결(미커밋, PARTIAL): PC 우측 패널에 웹과 같은 출발/도착 거점, 승객 이동/물품 배송, 인원/적재량, 요청 접수와 상태 표시를 추가했다. 별도 탭에서 기존 차량 목적지 직접 지정과 30/40/50km/h 설정을 유지한다. `/v1/demo/requests`에 Landmark ID를 전송하고 불확실한 통신 실패의 재시도에는 동일 command_id/본문을 재사용한다. 대기열/요청 목록은 서버 객체 배열 형식으로 읽는다. Unity 컴파일 및 3840×2160 화면 확인 후 UI에서 접수된 60주년→비룡플라자 요청(owner_id=unity-operator)의 V01 배차와 실제 39.82km/h 이동을 확인했다. `request-ui-guard.json`에서 예약 중 목적지 변경 호출 전후 IsDriving=true 유지 및 차단 안내를 확인했다. 근거는 `artifacts/high_speed_drive/request-ui-before.png`, `request-ui-live-state.json`, `request-ui-guard.json`. 이 시점에는 해당 요청의 하차·복귀, 배송/재시도/전체 거점 검증이 아직 남아 있다. 모바일 요청 폼은 미연결이다.

2026-10-25 관측 기반 승객 운송(미커밋, PARTIAL): FIFO 물리 실행기를 연결하고 Unity V01의 출발→도서관 픽업→하이테크 하차→3점 회차→정문 복귀를 실제 Physics 관측으로 완료했다. 총 1,915.93m, 최고 40.63km/h, 요청 COMPLETED 및 복귀 후 예약 해제를 확인했다. 18,742개 표본에서 Cinemachine 차량 추적 유지, 네 바퀴 동시 비접지 0회였다. 30~40km/h 카메라 높이 진동 RMS는 4.18mm(1초 이동 평균 제거)이며 승차감 합격 판정은 아니다. 근거: `artifacts/high_speed_drive/physical-passenger-library-hitech.csv`, 동일 이름 `-summary.json`, `passenger-status.json`. 기존 공통 회귀 155개 통과 후 운송 예약 상태 전달·수동 목적지 변경 충돌 방지를 추가하고 관련 28개 테스트를 통과했다. protocol 1~3 응답 형식은 유지하며 protocol 4에 `transport_phase`를 추가한다. 물리 자동 충전, V02/V03 물리 운송, 전체 거점 경로, Unity 승객 요청 폼은 미완료다. 아래 실행 경계 미연결 기록은 실행기 연결 이전 상태다.

2026-10-25 실제 요청 실행 경계(미커밋, PARTIAL): `create_request(external_execution=True)` 요청을 가상 배차/가상 충전 시점/가상 희소 차량 비용에서 제외한다. 기본 접수 경로는 변경하지 않았다. `preview_mission`은 회차 포함 경로를 임시 차량으로 계산하며 live ego·임무·시계를 보존한다. 공통 요청/배차/배터리/주행 151개 테스트 통과 후 충전·희소성 분리 보완에 대해 관련 78개 재검사도 통과했다. 실제 배차 실행기와 UI 요청 연결은 아직 미연결이며 `Docs/ADR/0010-observed-request-execution.md`에 후속 상태 전이 요구를 기록했다.

2026-10-25 회차 연결 및 실제 주행(미커밋, PARTIAL): `TurnController`와 `physical_turn.py`를 통해 전진 경로가 없을 때만 직선 도로 폭/앞뒤 여유 안에서 3점 회차를 계획하고, 이후 목적지 경로 생성까지 확인한다. 관측 기반 추종·3회 연속 정지 확인 후 방향 전환·도로 경계 이탈 시 정지를 구현했다. 관련 Python 검사 55개 및 회차 후 경로 재개 폐루프 검사를 통과했다. Unity 출발점→하이테크→실제 3점 회차→도서관 도착 1,491.29m/최고 50.61km/h를 계측했다. 회차는 약 18초/최고 2.16km/h였다. 전경 카메라 전환으로 보이는 높이 급변이 포함되어 카메라 진동 완료 판정은 하지 않았다. 전체 오프라인 경로 감사는 134/183개 통과이며 전체 거점·승객 물리 배차는 미완료다. 웹 회차 상태 표시를 추가했고 JS 구문 검사는 통과했으나 새 표시의 웹 렌더 검증은 미수행이다.

2026-10-25 Physics protocol 4(미커밋, PARTIAL): 후륜 축 오프셋과 후륜 중심 기준 보수적 차체 반길이를 Unity에서 측정해 전달한다. 음수 속도는 schema_version=4/`MANEUVER` 상태에서 -5km/h까지 허용하고 기존 protocol 1~3의 후진은 거부한다. Python 계약·스키마·회차 기하·기존 주행 51개 테스트 통과. Unity 컴파일 오류 없음 및 MCP 실연결을 확인했고, 저장된 실관측의 Python/JSON Schema 검증도 통과했다(`artifacts/high_speed_drive/protocol4-observation.json`: 반폭 0.4600m, 반길이 1.5500m, 후륜 오프셋 -0.6500m). 실제 후진 명령 생성/회차 상태기계/배차 연결은 아직 미구현이다. 계약 결정과 호환성은 `Docs/ADR/0009-physics-reverse-protocol.md` 참조.

2026-10-25 추가 검증(미커밋, PARTIAL): 출발점→하이테크 실제 주행은 848.80m/최고 50.58km/h/목적지 도착으로 확인했다. 전체 거점 후속 경로 생성 감사는 예상 도착 pose 기준 183개 중 111개만 통과했으며 일부 거점의 재출발이 막힌다(`Docs/LANE_MODEL.md`, `artifacts/high_speed_drive/all-stop-route-audit.json`). 전체 물리 배차는 미완료다. UI의 경로 접수 실패가 5초 후 접수 대기로 되돌아가던 오류를 수정했고 Unity MCP에서 `Unknown Stop candidate` 거부 응답 후 실패 안내 유지/`driving=False`를 검증했다(`rejection-check.json`). Unity 컴파일 오류 없음. 접수 실패 시 제동은 유지하며 다음 요청 때 안내를 초기화한다.

프로젝트 버전 **0.5.0.0** · 2026-10-23. 이 문서는 현재 checkout을 기준으로 하며, 과거 검증 기록과 현재 단계 판정을 구분한다. 근거가 없는 기능은 완료로 표시하지 않는다.

2026-10-25 정류소 시각화(미커밋): CampusTerrain의 STOP 후보 14개 옆에 지붕·벤치·한글 이름판이 있는 시각용 정류소를 추가했다. `Campus/Server Map/Build Stop Shelters`에서 재생성하며 기존 Stop 좌표와 서버 지도는 유지한다. 4.5m 측면 배치는 합성 시각화 오프셋으로 실제 시설 위치·접근성 검증이 아니다. 충돌체 0개 및 씬 저장을 MCP로 확인했고 도서관 정류소 화면에서 이름판 배치를 검토했다(`artifacts/high_speed_drive/shelter-preview.png`). 공유 프로젝트 재질과 고정 메시 Static Batching 설정을 사용하고 그림자는 끈다. PC/모바일 FPS·배치 수·메모리 실측과 전체 정류소의 주변 지형 시각 검토는 미수행이다.

2026-10-25 도로 평활화: 도로 영역만 사용하는 높이 피팅의 경계 연속성과 좁은 도로 수치 안정성을 보완하고 MCP로 도로 15개를 재베이크했다. 독립 수치 검사 8개와 최종 정적 형상 검사 통과, 70,155개 지점에서 지형 관통 0/메시·충돌면 불일치 0. 경사 10% 초과 면적은 최초 838.896m²에서 595.353m²로 감소했다. 전체 자동 운송/모바일/성능 검증과는 구분하며 상세와 한계는 [도로 요철 보정 기록](ROAD_SURFACE_SMOOTHING.md)을 따른다.

2026-10-23 최신 변경: 차량별 자동 운행 중지 UI·API를 제거하고, 운행에 영향을 주지 않는 추적 해제/재추적을 추가했다. 차로 운행의 직선 최대 속도는 18km/h이며 곡률·도로 제한·남은 거리·센서 제약을 함께 적용한다. 이전 중지 API의 제거로 프로젝트 B를 증가했다. 아래 항목은 각 단계의 당시 범위를 기록한다. 최종 검증은 artifacts/pc_auto_0.5.0.0 및 artifacts/pc_operator_0.5.0.0에 기록한다.

2026-10-23 차로: 기존 도로 에셋 합성 폭에서 우측 차로/교차로 연결을 계산하고 실측 차량 Collider 폭으로 차로 경계를 검사한다. 정적 지도 그래프와 원본 에셋은 변경하지 않는다. 물리 protocol 2에서 차로를 사용하며 protocol 1은 기존 중심선 Baseline을 유지한다. 근거와 제약은 Docs/LANE_MODEL.md 및 artifacts/lanes_0.4.10.0 참조.

2026-10-23 자동 경로 운행: Python 제어와 Unity WheelCollider를 별도 WebSocket 프로토콜로 연결한다. 자기 위치/속도/heading·차량에서 측정한 wheelbase·전방 Raycast 거리만 사용하고 타 객체 실제 Transform은 보내지 않는다. 동일 demo 세션 웹에 실제 물리 위치를 표시하며 수동 운전은 없다. 시간 기반 승객 배차와 분리된 목적 Stop 후보 자동 경로 운행이며 상세/검증은 Docs/UNITY_PHYSICS_AUTODRIVE.md를 따른다.

2026-10-23 PC 카메라: 기존 CampusTerrain의 V01 차량을 명시적 ID로 Cinemachine 3.1.7에 연결했다. 차량 버튼으로 추적하고 운영지도에서 드래그 이동·우클릭 회전·휠/버튼 확대 및 전체 보기/Esc 복귀를 제공한다. V02/V03은 웹 시연 데이터만 있으므로 Unity 미연결로 표시한다. 사용자의 추가 지시에 따라 수동 운전 UI와 Editor 방향키 입력을 제거했고 명령 없는 차량은 제동 상태로 시작한다. 기존 SetDriveCommand API와 물리 모델은 유지하며 서버 경로를 실제 차량에 적용하는 기능은 이번 변경에 포함하지 않는다. 검증 증거는 artifacts/pc_operator_0.4.8.0에 기록한다.

2026-10-23 PC 진입: PC_Bootstrap이 로컬 Python 서버를 실행/재사용하며 CampusWorld → CampusTerrain 및 PC_Operator Additive 구조를 유지한다. PC fixture는 껐으며 차량 조작은 보류한다. 실제 Python/Unity/웹 세션 검증과 배포·종료 규칙은 `Docs/PC_SERVER_STARTUP.md`를 따른다.

2026-10-23 노드 단계: 기존 CampusTerrain에 웹과 동일한 노드 187개·허브 3개·Stop 후보 14개를 배치했다. 읽기 전용 HTTP 지도·동일 웹 세션 상태와 오프라인 snapshot을 추가했다. 차량 조작은 보류하며 WebSocket/센서/물리 운행 연결 완료가 아니다. 상세는 [서버 지도 안내](CAMPUS_SERVER_MAP.md)를 따른다.

2026-10-23 통합: main의 WheelCollider 차량 제어·제동·Anti-Roll·프리팹/씬과 decision의 Python 지도·알고리즘·웹 시연을 보존했다. 지도 해시는 개행 독립 schema_version=2로 수정했다. 이는 Python–Unity 운행 연결 완료를 뜻하지 않는다. 현재 검증 결과·명령·제약은 [통합 보고서](INTEGRATION_MAIN_DECISION.md)에 기록한다. 아래 10월 19일 이전 결과는 과거 실행 기록이다.

2026-10-18: 독립 세션 `/demo/`에서 OSM 기반 웹 2D 관제와 차량 3대 경로 재생, 요청·FIFO·배터리·충전, 1~300배속/정지/초기화를 연결했다. 전체 pytest 109개와 브라우저 요청·배차·충전·정지 흐름을 검증했다. Unity/물리/센서 완료 판정과는 구분한다. [시연 안내](WEB_DEMO.md)를 따른다.

기본 운용은 V01 정문·V02 후문·V03 운동장 옆 주차장이다. 차량별 픽업/복귀/충전·42개 거점-Stop 왕복 가능성과 FIFO를 검증한다. 배치는 실제 충전 설비나 주차 베이 승인이 아닌 기존 OSM 도로 노드의 합성 운용이다.

2026-10-19 추가: 웹 기본 경로 탐색은 A*이며 FIFO는 유지한다. 187개 노드 보기/선택 구간 Dijkstra 비교, 별도 합성 RRT/TTC 시각화 탭을 추가했다. 전체 backend 147개 테스트 통과. 실제 센서/제어 연결과 구분한다.

2026-10-19 추가: FIFO 기준선 외 Greedy/Hungarian을 공통 제약/비용/우선순위와 batch 접수에 연결했다. 웹 정책 선택과 동일 초기 상태의 독립 반복 비교/JSON 저장을 제공한다. 전체 backend 187개 테스트 통과. [비교 범위](DISPATCH_COMPARISON.md)를 따른다.

2026-10-19 추가: 16:9 메인 관제를 요약/상세로 재배치하고 상단 메뉴를 두 개로 정리했다. RRT/TTC 보조 화면에 독립 20Hz 합성 운동 실험과 재생·시간 이동을 추가했다. 기본 정적 우회와 횡단 보행자 감속/대기/재출발, tick 사이 충돌/실패 기록을 검증했다. 전체 backend 191개 통과. 실제 관제 차량의 회피·차량 간 경합·Unity 센서는 여전히 미연결이다.

## 2026-10-23 · Unity MCP 직접 Play Mode 재검증

- 실제 PC_Bootstrap 및 Mobile_Bootstrap을 Unity MCP로 실행하고 동일 CampusWorld→CampusTerrain Additive 로딩과 각 역할 씬을 확인했다. 별도 씬 생성·차량 조작은 없다.
- 재현한 문제: 실행 중 390×844→360×640 변경 시 카메라 aspect가 1.074617에 머물러 기대값 1.308140과 달랐다. HUD의 화면/안전 영역 변화 감지와 재맞춤으로 수정했다. 허브 라벨의 기준도 실제 Safe Area 크기로 바꿨다.
- Editor 모바일 검증이 background=false 상태에서 2프레임에 정지한 문제를 수정했다. Editor만 역할과 무관하게 백그라운드 실행하며 실제 모바일 OS 정책은 유지한다.
- 웹의 실제 Freesentation 7Bold(가중치 700)를 재사용하고 합성 Bold를 없애 겹쳐 보이는 글자 윤곽을 정리했다. 기존 SIL OFL이 적용된다.
- 서버 패키징의 stdout/stderr를 동시에 읽도록 수정했다. 256개 번들 파일을 잠가 82,418자 오류를 발생시키는 MCP 회귀 검사에서 4.823초 만에 실패를 반환했고 모든 잠금을 해제했다. 이후 전체 빌드는 성공했다.
- 렌더링 완료 프레임에 실행되는 MCP 캡처 진입점을 개발 검증 프로브에 추가했다. PC 1280×720·1024×768 및 모바일 390×844·360×640에서 잘림 0건, 카메라 비율 일치와 동일 run_id를 확인했다. 지도 XZ 최대 오차 0.00006824m, 노드 187/허브 3/Stop 후보 14개다. Python 대상 검사 6개와 최종 PC 빌드가 통과했다. 결과와 화면은 artifacts/mcp_ui_review_0.4.7.1에 기록했다.
- 한계: 실제 모바일 단말·노치·OS 회전 및 성능 검증은 수행하지 않았다.

## 2026-10-23 · 웹 스타일 네이티브 UI / 그래픽 확인

- PC 실서버 모드는 웹과 같은 헤더·4개 지표·운영 지도·요청 카드·대기열·3개 차량 카드 배치다. 읽기 전용 demo 응답의 실제 run/time/배차/탐색/배터리를 표시한다.
- 모바일은 사용자 참고 이미지의 캠퍼스 3D 지도 + 하단 호출 시트 구성이다. 출발/목적 후보 선택은 UI 내부 동작이며 서버 요청을 만들지 않는다. 차량 호출·운행·탑승 ETA는 보류다.
- PC는 웹 창이 앞에 있어도 공통 월드와 HTTP 상태를 갱신하도록 데스크톱 백그라운드 실행을 유지한다.
- 모바일 하단 시트/버튼/메뉴는 SafeArea 내부의 분리된 행에 배치했다. 카메라는 기존 Campus overview camera를 사용하고, 역할별 각도와 지도 범위에 맞춰 투영한다. 신규 씬을 만들지 않았다.
- 공통 로고와 Freesentation 글꼴을 웹 자산에서 재사용했다. WOFF2를 fontTools로 TTF 컨테이너 변환했으며 SIL OFL 원문을 Resources에 포함했다. 운영체제 한글 글꼴 의존성을 줄였다.
- Annyoung Car의 실제 누락 원인은 씬 인스턴스의 삭제된 VehicleWebSocketClient GUID(4f84e7f4abde4aef8964fb1f5dcb993a)였다. 이 고아 컴포넌트만 정리하고 기존 WheelCollider VehicleController와 물리 설정을 유지했다. 프리팹 클래스 식별자도 완전한 이름으로 정리했다.
- 회귀: Python 212개, Unity EditMode 57개, 실제 Python 기동을 포함한 PlayMode 3개 통과. 첫 PlayMode 시도는 열려 있던 그래픽 Player가 포트를 소유해 자동 기동 조건에서 실패했고, 검증 창 정상 종료 후 재실행하여 3개 통과했다.
- 그래픽 Player/모바일 Windows 미리보기 검증은 artifacts/client_ui_0.4.7.0에 기록한다. Unity MCP로 최종 빌드를 실행했고 Player 자체 캡처에서 PC 2560×1440, 모바일 780×1688 모두 텍스트 잘림 0건, 웹 API와 두 클라이언트의 run_id 일치를 확인했다. 선택적 com.unity.pipeline의 RuntimePipelineConfig 부재 경고가 있지만 BuildReport는 성공했고 이번 클라이언트 UI/HTTP 경로에서 해당 패키지는 사용하지 않는다. 모바일 실기기·Android/iOS 빌드, FPS/메모리 성능, 실제 차량 동역학은 이번 검증 대상이 아니다. 자동 기동 서버는 loopback 바인딩이며 LAN 모바일 배포는 별도 서버 바인딩/주소 설정이 필요하다.

## 인지한 미구현 사항: 차량 간 경로 충돌

2026-10-19 사용자 지적을 기록한다. **이번 기록은 인지 및 후속 검토 항목이며 구현 착수/완료를 의미하지 않는다.**

- 현재 차량별 A*/Dijkstra와 가상 시간 운행은 독립 실행이다. 동일 구간 동시 점유, 동일 차로 앞차 추종, 좁은 통로의 대향 진입, 교차로/합류부 경합을 조정하지 않는다.
- 웹의 가까운 차량 아이콘 위치 보정은 표시용이다. 실제 차량 간 안전거리 유지·양보·충돌 회피가 아니다. RRT/TTC 보조 탭도 관제 차량 운행과 연결되지 않았다.
- 물리적으로 분리된 차로가 확인된 구간과 공유 단일 차로를 구분해야 한다. 그래프의 정방향/역방향 간선만으로 두 개의 독립 차선이라고 판단하지 않는다. 도로 폭·차선·대기 위치·출구 공간은 OSM/Unity 자료와 검증된 overlay로 확인해야 한다.
- 후속 검토: 좁은 통로의 양방향 간선을 공통 자원으로 묶는 진입 예약, 동일 차로의 센서 기반 안전거리/TTC 감속·정지, 교차로 충돌 영역 예약, 출구 공간 확인, 교착 감지와 대기/재탐색. 점유 중인 자원은 예약 시간 만료만으로 다른 차량에 넘기지 않는다.
- RRT 후보가 예약·차로 제약이나 정지 판단을 무시하도록 연결하지 않는다. 계획 단계의 Reservation/CBS와 실시간 센서 안전 판단을 구분한다. 설계 기준은 `AGENTS.md` §9~10을 따른다.
- 현재 FIFO/Greedy/Hungarian 비교 결과는 차량 간 양보·정체 지연을 제외한다. 경합 처리를 추가한 후 모든 정책에 같은 제약을 적용해 비교를 다시 검증해야 한다.

## 단계별 상태

| 단계 | 상태 | 확인된 구현 및 남은 종료 조건 |
|---|---|---|
| M0 클라이언트 기반 | VERIFIED (범위 한정) | Bootstrap/Additive 씬, Windows/Android 빌드 프로필, DTO, WorldStateStore, 좌표 변환, fixture replay 및 PC/Mobile uGUI 기본 화면. Unity 6000.3.21f1에서 과거 기록된 EditMode 54/54와 PlayMode 1/1, 화면 smoke 결과가 있다. Android Player/실기기 검증은 아니다. |
| M0 Python 서버 기반 | PARTIAL | FastAPI/Pydantic, 메모리 요청/취소 API, FIFO와 OSM/Unity 형상 기반 가상 시간 실행. 기본 V01~V03 세 대, 테스트 187개 및 Ruff/format check 검증. 독립 OSM 웹 2D 시연 세션을 제공한다. 영속 저장, 인증된 principal/명령 충돌 방어, WebSocket/Unity 연결은 미완료다. |
| 지도 조사·표현 | PARTIAL | OSM/DSM 조사 자료, 기존 Unity 캠퍼스 장면 및 근사 Terrain/건물 표현이 있다. 서비스용 그래프/Stop 또는 통행·접근성 승인은 아니다. 개별 상태는 [지도 계획](MapResearch/PLAN.md)을 따른다. |
| M1 단일 차량 수직 흐름 | PARTIAL (실제 형상·가상 시간 실행) | 자체 Dijkstra/A*, 방향/평행 edge와 geometry 복원, FIFO·용량·Stop 접근성·세 구간 경로 확인, 픽업→시간상 완료→복귀 후 재배차. 정적 100 합성 OD를 Floyd–Warshall 기준과 대조했다. 웹 A* 왕복/196 OD 비교와 187개 노드 토글을 추가했다. 승인된 실제 운송 지도, 승하차 시간/도착 판정, Unity 주행은 없어 T01/T03 전체 통과가 아니다. 합성 배터리 소모·예비량·충전과 3대 차량 FIFO 가상 시간 운행을 추가했다. |
| M2 실제 지도·Stop·커버리지 | PARTIAL (형상 그래프·후보) | OSM/Unity 도로 59 way, 187 node, 402 방향 edge를 AEQD로 변환했다. 인벤토리 14곳 모두 Stop 후보와 정문 왕복 경로를 제공한다. 하이테크/생활관 1·2·3/역을 포함한 v2이며 역은 Unity 중심선 기반 우회 복귀를 사용한다. 접근성·폭·통행·보행/차량 연결 승인과 비룡플라자 정책은 미완료다. 원본/생성 코드 해시와 누락/오류 실패 처리를 검증했다. |
| M3 혼잡·동적 재계획 | NOT_STARTED | 다섯 시간대, 센서/prior 구분 혼잡 추정, A* Full Replan/D* Lite 및 T04~T06/T24가 없다. |
| M4 센서·안전·지역 계획 | PARTIAL (Python 독립 기초) | 센서 관측 검증, TTC/정지거리 조언, 정적 RRT 기하 후보와 신규 24개 테스트를 추가했다. 전체 backend 147개 통과. 합성 RRT/TTC 웹 시각화를 제공한다. Unity SensorRig/통신/제어/재출발/동적 궤적 검증 및 T07~T09/T21 전체 인수는 미완료다. [범위와 연결 계획](LOCAL_AVOIDANCE.md)을 따른다. |
| M5 다중 차량·배차·경합 | PARTIAL (가상 시간 한정) | 3대 차량의 가용 순서 FIFO, 합성 배터리 소모·15% 예비량·차량별 거점 가상 충전과 재배차를 검증했다. Greedy/Hungarian 선택·독립 반복 비교와 장기 대기 보호를 추가했다. 실제 제원/충전소 검증, 다중 차량 Physics·Reservation/CBS·수요 기반 재배치 정책은 없다. 정적 거점 분산 운용만 제공한다. M5 전체 완료가 아니다. |
| M5a 실시간 통합 | NOT_STARTED | Python WebSocket, PC/Mobile 권위 상태, 실제 호출/ACK/재접속/세션 격리, 센서 입력 연결이 없다. |
| M6 실험·성능·OSS/IOSS | NOT_STARTED | E1~E4, 부하/성능 원자료, 재현 run, 동시 데모와 OSS/IOSS 증빙이 없다. |

## 지도 데이터 출처와 검증 상태

| 데이터 | 출처/보관 위치 | 현재 확인된 검증 | 서비스 사용 상태 |
|---|---|---|---|
| 기본 OSM | `Assets/InhaCampus/Source/campus.osm`, OSM 기여자·ODbL 1.0, 출처/범위는 `Assets/InhaCampus/Source/ATTRIBUTION.txt` | 원본 및 기존 장면 생성 입력. 기존 좌표 변환은 원점 37.4506N, 126.6535E의 로컬 평면 좌표이며 지도 계획상 AEQD 대비 검사점 최대 약 0.69m 차이가 남아 있다. | 시각 표현/조사 원본. 폭·통행 허용·경사·Stop은 승인되지 않음. |
| 확장 OSM | `Docs/MapResearch/2026-09-17/expanded_campus.osm`, 2026-09-17 다운로드 기록과 SHA-256은 같은 폴더 `downloads.json` | 조사 bbox 126.645~126.665E, 37.443~37.457N. 조사 범위이며 최종 서비스 경계가 아니다. | 원본 조사 자료. relation 조립·위상/연결 검증 후 그래프화해야 함. |
| Copernicus GLO-30 DSM | `Docs/MapResearch/2026-09-17/` 다운로드 기록·metadata·EULA 및 `maps/inha_relief_research/` 가공 결과 | manifest가 원본 SHA-256, AEQD 원점, 수직 기준 EGM2008, 257×257·8m 보간 격자, 처리 방법과 제한을 기록. RAW 양자화 왕복 오차 약 0.00077m는 파일 표현 오차이지 지형 정확도가 아니다. DSM은 건물/수목을 포함할 수 있다. | 근사 시각 Terrain 전용. 경사·단차·접근성·건물 높이 증명에 사용 금지. |
| OSM landmark 후보 | `maps/inha_relief_research/osm_features.geojson`, `landmark_candidates.json` | 983개 node/way feature 및 19개 landmark 후보 기록. 후보 수는 완료 시설 수가 아니다. relation 및 시설 대응·출입구 검증은 미완료다. | 검색/검토 후보. 런타임 graph/Stop으로 사용 금지. |
| 공식·보조 시설 참고 | `Docs/MapResearch/2026-09-17/inhatc_campus.html`, `inha_campus_reference.html`, 입학 자료 및 `Docs/MapResearch/BUILDING_REFERENCES.md` | 시설 명칭/위치 참고 자료. 시설 전체 목록과 각 출입구·보행/차량 동선의 현장 확인은 별도다. | 교차 확인 근거. 통행성·접근성 승인 아님. |
| Terrain·출입구·도로 작업 기록 | `Docs/MapResearch/Iterations/` 아래 개별 검사 결과 | 각 보고서는 기록된 구역·검사항목만 증명한다. 최신 계획은 인벤토리 14 랜드마크/27 출입구/27 접근 메시, 미해결 역할 1개로 기록하며 인하대역 이동지원 출입구가 미확정이다. | 전체 도로 그래프 연결, 교차로 접합, 경사/단차, 접근성 검증 전에는 운송 가능으로 표시하지 않음. |

필수 랜드마크 목록과 대표 시설 전체 대조, service_needs별 Stop 선택, 접근 가능한 보행 연결, 차량 통행 그래프, 비룡플라자 경계/대체 지점은 M2 종료 조건이다. 생활관 1·2·3은 현재 일부 형상·도로가 시각화 초안이며 차량 Stop이 아니다. 지도 데이터 세부 현황과 다음 검증은 [Docs/MapResearch/PLAN.md](MapResearch/PLAN.md)에 기록한다.

## 클라이언트·Python 기반의 한계

- Python `backend/`는 실제 OSM/Unity 형상에 기반한 가상 시간 실행이다. `osm_geometry_simulation`에서 30km/h(약 8.33m/s) 가정 속도로 계산하며 도로 폭·통행·Stop은 승인되지 않았다. 고정 ETA 180초는 제거했다. 시간상 완료는 Unity 물리 운송 성공을 의미하지 않는다.
- Unity fixture는 합성 snapshot이다. 0.1.5.0 시점 fixture 화면 검증과 0.1.6.0 서버 기반은 같은 네트워크 실행이 아니다.
- PC/Mobile 요청 생성 UI, Landmark 검색·선택, 지도 차량/경로 표시, 공통 JSON Schema, WebSocket, 이벤트 delta, Physics/센서 연동은 미완료다. 상세 화면 인수 기준은 [클라이언트 적용·검증 계획](ClientUI/IMPLEMENTATION.md)을 따른다.
- 2026-10-15 후속: 3대·배터리 적용 후 pytest 91개, Ruff/format check를 통과했다. 차량 가용 FIFO·예비량 경계·적재 에너지·충전 재배차·큰/작은 tick 결과 일치를 검사했다. 확장 목적지 5건과 배터리 18건 데모를 재생했고 `artifacts/battery_fleet_validation.json`에 입력/코드 hash와 결과를 기록했다. 아래 75개 검사는 이전 단일 차량 시점 기록이다.
- 2026-10-15: Python 3.14.4에서 `python -m pytest backend/tests -q -p no:cacheprovider` 75개 통과, Ruff/format check 통과 및 `python -m campus_sim.demo_fifo` 실행. 원본/산출물 해시, 지도 재생성, 세 좌표 기준점, 실패 사례와 HTTP/자동 복귀를 검증했다. Windows 로컬 소켓 권한과 쓰기 가능한 별도 basetemp가 필요했다. 의존성은 `backend/requirements-dev.lock`에 고정한다. Unity/Player/실단말/성능 및 Python 3.11 실행은 미검증이다.

## 차량 모델·프리팹 준비 상태

최근 커밋 `f97fa11`에서 차량 모델과 프리팹을 추가했다. 프로젝트 에셋은 `Assets/CampusSim/Models/AnnyongCar/`, `Models/Default Car/`, `Models/Induck Car/` 및 `Assets/CampusSim/Prefabs/Annyoung Car.prefab`, `DefaultCar.prefab`, `InduckCar.prefab`에 있다. 주행 구현에서는 이 프리팹을 차량의 시각 표현으로 재사용한다. 프리팹 추가만으로 차량 동역학·Collider/Physics 설정·차량 제원·차량 능력·안전 제동이 구현된 것은 아니다. M1/M4에서 축/전방 방향·미터 단위 크기·피벗·Collider/접지·렌더링 비용을 검토한 후, 시각 모델과 물리/제어 루트를 분리해 연결한다. 제원이나 성능은 측정·확인 전까지 미확정이다.

## 다음 작업 순서

2026-10-15 확장 적용: 사용자 요청으로 다섯 거점의 고정 OSM 선분을 v2 기본 지도에 반영했다. 총 14곳에서 FIFO·Dijkstra 픽업/운송/복귀를 제공하며 역은 미지원 지름길을 제외한 407.16m 복귀 경로를 사용한다. V01~V03 세 대·30km/h·합성 배터리/충전을 적용한다. 91개 테스트와 확장 목적지/배터리 데모를 검증했으며 실제 운송/접근성 승인은 미완료다.

1. 지도 계획의 출처·검증 표를 유지하고 필수 Landmark/대표 시설/Stop/차량·보행 연결의 실제 검증 근거를 추가한다.
2. 확장한 하이테크·생활관·역의 통행·폭·경사·Stop·보행/횡단 접근과 실제 씬 적용을 검증해 T14/T19를 확장한다.
3. Dijkstra→A* 비교와 Unity 차량 1대의 실제 운송 흐름을 구현한다. 가상 왕복 타이머는 물리 운송 완료 판정으로 사용하지 않는다.
4. M2 지도/접근성/비룡플라자 범위를 완료한 뒤, 센서·안전, 다중 차량, WebSocket 통합 순으로 진행한다.

버전의 단일 원본은 루트 `VERSION`이다. 현재 Python package/API 버전, Unity `bundleVersion`, README 및 CHANGELOG는 0.4.8.0로 정합화했다. Unity Editor 버전은 별도인 6000.3.21f1이다. schema_version 및 map_version은 프로젝트 버전과 독립적으로 유지한다.

### 2026-10-19 커밋 전 최종 확인

충전 중 차량의 에너지 가능성 재평가와 선택 시 충전 중단을 FIFO/Greedy/Hungarian에 공통 적용했다. 필요 에너지 도달 시각을 이벤트로 처리해 화면 폴링 간격에 의존하지 않는다. 전체 backend 196개 통과 후 UI 최종 정리에서 웹 회귀 8개를 재확인했다. Ruff 검사/포맷 및 JS 3개 구문 검사를 통과했다. 브라우저에서 1600×900·1280×720 관제, 1280×720 실험 화면, 390px 관제, 차량 상세/노드 토글/TTC 전환을 확인했다. Unity 빌드·실제 SensorRig 및 다중 차량 충돌 조정은 검증하지 않았다.

### 2026-10-25 Unity 관제 UI 정리 — PARTIAL (실행 화면 미검증)

- `WebClientHud` PC 레이아웃을 웹 `dashboard.css`/`polish.css`의 64px 헤더, 328px 우측 패널, 20px 작업영역 여백, 요약/지도/차량 카드 구조로 재구성했다. 지도 카메라 viewport도 실제 UI 영역에서 계산한다.
- 운행 설정은 선택 차량/현재 속도, 목적지 드롭다운, 설정 속도, 시작 버튼과 필수 상태만 표시한다. 설명·내부 세션·중복 지도 버튼을 제거하고 추적 해제/전체 보기는 지도 헤더에 모았다. 연결이 끊기면 서버 집계는 대시로 표시한다.
- 허브는 작은 위치 점/이름표, 차량은 실제 Unity Transform 기반 색상별 ID 표식으로 표시한다. 차량 카드와 표식 색상을 맞추고 표식 클릭 시 추적한다. 겹치는 이름표는 위치 점을 이동하지 않고 연결선과 함께 재배치한다. Unity에 연결되지 않은 차량의 위치는 만들지 않는다.
- 검증: Unity 프로젝트 csproj의 참조 DLL/소스 목록으로 만든 `tmp/ui-compile.rsp`에 대해 Roslyn C# 컴파일 실행(exit 0, 기존 JSON DTO CS0649 경고). `git diff --check -- Assets/CampusSim/Scripts/Presentation/WebClientHud.cs` 통과.
- 미검증: Unity batch 렌더 검증을 시도했으나 프로젝트 경로 확인 직후 Editor가 종료되어 이미지가 생성되지 않았다(`tmp/ui-unity.log`). 실제 클릭/스크롤, 해상도별 표시, 차량 추적 중 라벨, PC/모바일 FPS·메모리 측정은 미검증이다. 씬/공유 메시/재질은 변경하지 않았고 UI 스프라이트를 공유하며 표식 탐색은 등록 차량/허브에 한정한다. 성능 개선 완료나 통합 주행 검증으로 해석하지 않는다.
