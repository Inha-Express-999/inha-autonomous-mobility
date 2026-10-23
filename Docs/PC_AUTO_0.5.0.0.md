# PC 자동 운행·차로·카메라 검증 · 0.5.0.0

2026-10-23, Windows 11, Unity 6000.3.21f1, Python 3.14.4, Game View 1280×720. 통합 브랜치 `codex/integrate-decision-main`에서 작업했다. 기존 CampusTerrain과 PC_Bootstrap → CampusWorld/PC_Operator/CampusTerrain Additive 구조 및 원본 차량 프리팹을 사용한다.

## 변경

- 차량별 자동 운행 중지 버튼, Unity StopMission 호출, Python 중지 메서드와 POST `/v1/demo/unity/vehicles/{id}/stop`을 제거했다. 제거한 API는 404를 반환한다. 계약 제거에 따라 프로젝트 버전 B를 올려 0.5.0.0으로 정합화했다.
- 차량 선택은 실제 V01을 Cinemachine으로 추적한다. 추적 해제/Esc는 차량 부근의 자유 지도 카메라로 전환하고 운행을 유지한다. 드래그 이동·우클릭 회전·휠 확대 후 차량 버튼으로 재추적할 수 있다. 운영지도 보기는 전체 캠퍼스 구도로 돌아간다.
- 새 차로 protocol 2는 직선 목표 최대 18km/h, 도로 제한속도, 곡률 미리보기, 목적지 거리와 센서 정지거리로 속도를 제한한다. 주행 중 새 시작 입력을 막는다. protocol 1은 기존 중심선·0.8m/s를 유지한다.
- 400/402개 방향 edge에 합성 제작 폭 기반 차로 프로필을 고정했다. 2개는 폭 출처가 없어 제외한다. 우측 offset, 차량 회전 반경을 고려한 교차로 곡선, 차체 반폭을 고려한 차로 이탈 제동을 사용한다.
- 익명 Raycast hit의 ego 좌표로 계획 차로의 통과 영역을 검사한다. 제동 후 해제 거리를 고정하고 최소 정지 여유를 적용해 장애물 앞 저속 접근과 반복 출발을 방지한다. 센서·연결·명령 watchdog 및 기존 시뮬레이션 일시정지에 따른 제동은 유지한다.

## 실행 결과

Unity MCP `Unity_RunCommand`에서 실제 UI 버튼을 호출하고 기존 차량이 움직이는 동안 위치/속도/차로 오차를 기록했다. 테스트 장애물만 임시 생성 후 제거했다. 차량 Transform은 이동시키지 않았다. 차량 조작에 computer-use나 LLM 판단을 사용하지 않았다.

| 항목 | 결과 |
|---|---|
| 실제 V01 자동 경로 | Library 후보까지 약 45.3초, 시작/종료 직선거리 118.97m |
| 교차로 방향 전환 | 약 92.85도, 최대 차로 횡오차 0.4293m |
| 속도 | 직선 목표 18km/h, 실제 최고 18.66km/h, 곡선 감속 확인 |
| 장애물 | 완전 정지 판정 시 약 0.0317m/s, 장애물 제거 후 재출발 |
| 목적지 | 목표 오차 0.5907m, 정지 후 ARRIVED |
| 추적 해제 | 카메라 고정·타깃 해제, 차량 9.08m 추가 운행, 지도 이동·재추적 통과 |
| UI/지도 | 잘림 0건, 노드 187·허브 3·Stop 후보 14, 지도 XZ 배치 최대 오차 약 0.00006824m |
| 같은 웹 세션 | run_id·차로 hash 일치, motion_source=unity_physics, 시간차 있는 도착 후 snapshot 위치 차이 약 0.0105m |
| Windows PC | MCP 빌드 성공, 포함된 서버 자동 시작, 앱/서버 0.5.0.0 및 동일 세션 연결, PARKED 상태, 정상 종료 |

실제 속도는 목표 상한보다 약 0.66km/h overshoot했다. 목표 명령 상한과 측정 속도를 구분한다. 정지·횡가속도·차로 폭·여유는 시뮬레이션 튜닝이며 검증된 실차 한계가 아니다.

회귀 검사:

```powershell
$env:PYTHONPATH='backend/src;.utmp/fifo-dijkstra-deps'
python -m pytest backend/tests -q -p no:cacheprovider --basetemp=.utmp/final-0500-verified-tests --junitxml=artifacts/pc_auto_0.5.0.0/python.xml
# 232 passed
python -m pytest backend/tests/test_drive_speed.py backend/tests/test_unity_drive.py backend/tests/test_lanes.py -q -p no:cacheprovider --basetemp=.utmp/fast-drive-final-tests --junitxml=artifacts/pc_auto_0.5.0.0/drive-python.xml
# 마지막 장애물 정지 여유/곡선 미리보기 보완 후 20 passed
node --check backend/src/campus_sim/web/app.js
# 통과
```

Unity MCP로 `PcCameraRegressionRunner.Run()` PlayMode 회귀 2개 통과. 카메라 고정·자유 이동·재추적·대상 삭제 복귀·지도 확대/회전/리사이즈를 검사했다. `DesktopServerBuild.BuildForVerification()`는 Windows Player와 Python 서버 묶음을 생성했다. Python 실행기는 외부 설치를 사용하며 로컬 의존성을 묶는다.

빌드 당시 checkout에 사용자의 VehicleController Rigidbody 보간 설정이 있었다. 이 변경은 보존하며 이번 커밋에서 제외한다. 사용자 소유의 다른 지도 조사 산출물 변경도 수정하거나 포함하지 않는다.

증거: `artifacts/pc_auto_0.5.0.0/verification.json`, `trace.csv`, `fast-driving.png`, `released-map.png`, `final.png`, `web-analysis.json`, `native-startup.json`, `python.xml`, `drive-python.xml`, `artifacts/pc_operator_0.5.0.0/playmode.xml`.

PC 단독 실행은 숨김 창으로 서버 시작과 계산된 UI 배치를 확인했다. 숨김 창의 PNG 캡처는 실패했으므로 네이티브 PNG 시각 검증을 통과로 기록하지 않는다. 화면 시각 검증 근거는 MCP Game View 캡처다. 네이티브 Player의 실제 자동 이동은 별도로 검사하지 않았다.

## 실패 후 보완과 한계

최초 전체 Python 검사에서 manifest 개행으로 바이트 재현 비교 3개가 실패했다. LF로 수정 후 전체 232개가 통과했다. 첫 고속 실제 주행은 도착/추적 해제에 성공했지만 정해진 장애물 검사 시간 안에 정지하지 않아 실패했다. 최소 정지 여유와 곡선 미리보기를 보완하고 실제 정지까지 관측해 재검증을 통과했다. `first-attempt`에 첫 결과를 보존했다.

`artifacts/lanes_0.4.10.0`은 커밋 전 저속 개발 중 기록이며 별도 릴리스가 아니다. 당시 중지 기능 검사는 이번 기능 제거로 대체됐다. 원래 표시 버전을 소급 변경하지 않았다.

실제 물리 차량은 V01 한 대다. V02/V03은 기존 가상 시연 상태이며 다중 차량 물리 주행을 검증하지 않았다. 전체 승객 요청→배차→픽업→하차의 물리 도착 통합, 센서 RRT 회피·예약/CBS, 모바일 실기기·FPS 성능·현장 도로 접근성/폭 인증은 완료 범위가 아니다. 원본 에셋·지형·모바일 UI를 이번 변경에서 수정하지 않았다.
