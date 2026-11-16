# 새 통합본 V03 주행 및 시험 배치 간섭 기록

기준: 69ef457c / v0.5.11.1, Unity 6000.3.21f1. 2026-11-14 실행 기록을 2026-11-15에 확인했다.

## 확인한 결과

V03는 원래 거점에서 `anniversary_60_road_candidate`까지 실제 WheelCollider 주행으로 도착했다. 관측기 기준 103.4초, 속력 0.039m/s, `Reached Stop candidate`를 기록했다. `00-v03-arrived.png`와 동일 이름의 state JSON이 근거다. 센서를 끄거나 차량 위치를 순간 이동시키지 않았다. 이전 맵의 V03 접근 실패와 달리 이번 실행은 도착했지만, 맵과 LiDAR 판단이 함께 변경됐으므로 맵 수정만의 효과라고 단정하지 않는다.

이후 관측기가 V03를 정류장에 그대로 둔 채 승객 요청을 접수했다. FIFO가 V02를 배정했고, V02가 승차 지점 접근 중 YIELDING으로 정지했다. 최종 기록의 위치는 V02 (21.774, -22.728), V03 (23.553, -19.539)로 약 3.65m 떨어져 있었다. V03가 정류장을 점유한 시험 배치가 정지를 유발했을 가능성이 있다. 이 위치 비교만으로 LiDAR가 검출한 객체의 정체를 확정하지 않는다.

이 실행에서는 승객 상태가 ASSIGNED에 머물렀고 탑승 중 단절 버튼을 실행하지 못했다. 종료 전 약 488초까지 기록됐으며 자동 완료 결과는 없다. **탑승 중 재연결 검증 통과가 아니다.**

후속 시험은 새 Play 세션의 초기 거점 배치에서 승객 요청만 접수한다. 사전 주행 차량을 정류장에 남겨두지 않는다. 반복할 경우 V03 접근 시험과 승객 복구 시험을 별도 세션으로 분리한다.

## 환경 준비 및 회귀 시험

- 새 프로젝트 `.venv`에 backend[dev] 설치, pip check 및 서버 import 성공.
- LiDAR 시험에서 사용하는 jsonschema가 dev 의존성에서 빠져 있어 backend/pyproject.toml에 jsonschema>=4,<5 추가.
- 선택 범위: test_release_version, test_osm_map, test_unity_map, test_lidar_ground, test_lidar_corridor, test_pc_lidar, test_physical_dispatch.
- 의존성 수정 후 152 passed, 5 xfailed, 임시 폴더 권한으로 setup error 9건.
- 오류 9건만 쓰기 가능한 별도 --basetemp로 재실행: 9 passed. 합산 161개 통과, 기존 예상 실패 5개. 단일 전체 실행 통과로 표현하지 않는다.
- pytest.xml / pytest-before-temp-fix.xml은 임시 폴더 오류를 포함한 결과이고, pytest-retry.xml이 재검사 결과다. pytest-before-dependency-fix.xml은 jsonschema 누락 당시 결과다.
- packages.txt는 설치 버전 기록이다. Starlette TestClient의 httpx 사용 중단 예정 경고 1건은 남아 있다.

driver.cs.txt는 실행 당시 임시 관측기 소스다. 서비스 동작 코드가 아니라 HUD 입력과 상태 기록을 수행하는 시험 도구다.
