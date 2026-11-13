# 차로 밖 구조물 TTC 오탐과 LiDAR 표시 검증

작업일: 2026-10-30 · 작업 트리 프로젝트 버전: 0.5.6.0 · 미커밋 검증 기록

## 변경

기존 정지 판단은 현재 heading의 긴 직진 검사와 향후 경로 검사를 합쳐 적용했다. 곡선 경로가 벽을 피해도 직진 검사가 정지시킬 수 있었다. 또한 후륜 기준의 보수적 길이를 root 기준 차체 길이로 사용하면 앞뒤 여유가 불필요하게 커졌다.

- Unity에서 실제 Collider의 root 기준 앞/뒤 길이와 현재 전륜 조향각·최대 조향 변화율을 ego 관측으로 전달한다. 이전 rear-axle envelope도 유지하고 새 길이가 그 범위를 벗어나면 거부한다.
- Python은 센서 반환점이 실제 차체의 예상 이동 범위와 겹치는 거리로 TTC/정지거리를 계산한다. 일반 주행은 기존 pure pursuit 목표와 조향 변화율을 이용한 짧은 bicycle 예측, 회차는 현재 실행 중인 회차 arc를 사용한다. 경로 꼭짓점을 제자리 차체 회전으로 취급하지 않는다.
- 관측된 고정 지도 메시에는 `static` 센서 분류를 부여한다. 명시적 보행자/차량 분류가 우선하며 Rigidbody가 붙은 미분류 물체는 `unknown`을 유지한다. 감지 반환 자체를 제거하지 않는다. 동적 객체의 실제 Transform/속도를 Python에 전달하지 않는다.
- 현재 차체 근처, 반응시간 내 실제 이동 범위, 미분류 회차의 보수적 제동, 무효/불완전 관측 제동을 유지한다. 정지한 보행자가 전방 안전 폭을 벗어나기 전에는 속도가 줄었다는 이유로 출발하지 않는다. 5회 연속 clear scan을 요구하며 재출발 속도도 같은 TTC 기준 안으로 제한한다.
- 정지 위험이 사라진 뒤에도 별도의 1m 재출발 문턱 때문에 영구 대기하던 조건을 제거했다. 5회 연속 관측이 같은 TTC/정지거리 검사를 통과해야 출발할 수 있다. 실제 정체 관측·서버 경로를 고정한 재현 검사로 저속 재출발과 침입 시 재정지를 확인한다.
- 초기 표시 보완은 점군 표시 중 지도 viewport의 배경을 검게 하고 도로·건물의 렌더링을 잠시 숨겼다. 차량/점군/관제 UI와 Collider·센서·물리 상태는 유지했다. 이 전역 적용이 다른 차량 시점에도 영향을 주어, 이후 별도 사용자 요청의 차량 시점별 흑백 작업이 이 구현을 대체했다. `pedestrian-black-phase/`와 아래 초기 7개 검사는 검은 배경 단계의 기록이다. 최종 `pedestrian/stopped.png`는 후속 흑백 상태의 실제 보행자 시험 화면이다.

`schema_version=5`에 선택 필드를 추가했다. 새 서버는 이전 클라이언트 관측을 보수적으로 처리한다. 이전 서버는 새 필드를 거부할 수 있으므로 Unity와 Python을 함께 갱신해야 한다. 다른 `IAutonomousVehicle` 구현을 추가할 때에는 조향 변화율도 제공해야 한다. 프로젝트 버전/태그를 변경하거나 커밋하지 않았다.

## 회귀 및 실제 장애물

- Python 전체: **440 통과, 3 실패**. 실패는 `test_map_portability`의 개행 조건 2건 및 `test_osm_map`의 deterministic rebuild 1건이다. 기존 `policy.json`의 service_locations 좌표 마지막 소수 자릿수와 재생성 결과가 달라 manifest hash가 달라진다. TTC 통과를 위해 지도를 다시 쓰지 않았다. 원본 출력: `../artifacts/validation/2026-10-29-ttc-corridor/python-all-final.txt`. 다른 동시 실행에서는 Unity 서버의 시작 시 재구성과 겹쳐 manifest가 잠시 없어 연결 시험도 실패했다. 시작과 분리한 최종 실행에서는 연결 시험이 통과했다.
- TTC/LiDAR 선택 검사: **68 통과, WebSocket 1건 제외**. 실제 관측 재생 5종, 곡선 내/밖 장애물, 새 조향 예측, 앞/뒤 차체 침입, 반응 범위, 보행자 정지 유지, 재출발 TTC 제한 및 잘못된 관측 필드를 검사했다. 고정 facade 분류 재생 fixture는 실제 관측을 바탕으로 분류만 바꾼 통제 입력이며 실제 Unity 통합 결과와 구분한다.
- Unity PlayMode **7/7 통과**: 지면 평면, 높은 물체, 고정/동적/명시적 센서 분류, 다중 점군 소유자의 검은 배경 및 원상 복구를 검사했다. `playmode.xml`에 2026-10-29 15:52:32~35 UTC 기록.
- 실제 PC 캠퍼스 보행자 시험 **PASS**: 최신 흑백 화면 상태에서 진입 속도 17.62km/h, 최저 속도 약 0.00035km/h, 재출발 약 18.60km/h. 보행자 점유 중 조기 출발 0회, Collider 겹침 없음. 실제 WheelCollider/Raycast/WebSocket/Python을 연결했으며 센서 데이터를 주입하지 않았다. `pedestrian/summary.json`, `trace.csv`, `stopped.png`에 보관했다. 18km/h는 명령 상한이며 실제 WheelCollider 속력에는 위와 같은 제어 overshoot가 있었다. 속도는 시뮬레이션 관측이며 실차 제동거리/성능 인증이 아니다. 후속 흑백 표시의 독립 LiDAR/보행자 8개 및 카메라 4개 회귀도 원본 XML에서 통과를 확인했다(`artifacts/validation/2026-10-30-lidar-monochrome/`).

## 캠퍼스 확대 검증

**최종 판정: 모든 목적지 요청 조합을 재시도했으나 전체 물리 경로 검증은 실패/미완료다.** 차량 3대 × 14개 Stop 후보의 42개 조합을 모두 요청했다. 첫 실행은 26건 후 다른 화면 작업의 Editor 재생 중지로 중단됐고, V01/V02의 남은 16건을 원래 씬 출발점에서 다시 시도했다. 일시정지 위치에서의 정확한 연속 재개와 다르다. 추가로 V03을 시험 중에만 관측된 V02 후문 출발점에 옮겨 14건을 비교했다. 따라서 총 요청 시도는 56건이며, 이 중 실제 도착은 24건이다.

| 차량/조건 | 목적지 시도 | 실제 도착 | 정체 | 경로 거부 | LiDAR 대기 전환 합계 |
|---|---:|---:|---:|---:|---:|
| V01 초기+남은 목적지 | 14 | 8 | 0 | 6 | 0 |
| V02 초기+남은 목적지 | 14 | 8 | 5 | 1 | 19 |
| V03 서쪽 원래 출발 | 14 | 0 | 13 | 1 | 20 |
| V03 후문 출발 비교 | 14 | 8 | 0 | 6 | 0 |

V01은 실제 이동 약 1.92km, 후문 출발 V03은 약 2.54km에서 LiDAR 대기 전환이 없었다. 14개 Stop 중 어느 차량으로든 실제 도착한 곳은 11개이며, **기숙사 1·2·3은 이번 실행에서 도착하지 못했다.** 대기 전환 합계는 파생 시도를 포함한 명령 상태 전환 수이며 서로 다른 위험 지점 수가 아니다. 기록된 관측 무효 제동은 0샘플이었다. 로컬 운행 보류/명령 상태 불일치 카운터는 V01 1, V02 3샘플로, 이 카운터만으로 명령 만료 원인을 단정하지 않는다.

주요 잔여 문제는 다음과 같다.

- V03 도서관 방향 서쪽 코너 `x≈-193.40, z≈38.78`: 실제 Raycast 반환은 `Facades`다. 정지 시 현재 차체와 반환점의 수평 여유는 약 0.57m이나, 기존 제어의 예측 이동 중 최소 여유가 약 0.086m로 줄어든다. 유지한 static 안전 여유 0.2m보다 작아 정지를 유지한다. `v03-narrow-corner-diagnostic.json` 참고.
- V02 기숙사 2 방향 `x≈304.12, z≈-132.36`: `Facades` 반환과 현재 차체의 수평 여유는 약 0.35m, 예측 중 최소 약 0.165m로 역시 0.2m 미만이다. `remaining-routes/v02-narrow-corner-diagnostic.json` 참고.
- 위 수치는 수평 센서 점과 bicycle 예측 차체 사이의 계산값이다. 실제 Collider penetration 측정이나 벽 전체의 최소 여유 실측이 아니다. 이 근거만으로 벽을 무시하거나 제동 여유를 줄이지 않았다. 기존 경로/차로 형상과 추종 궤적의 안전 여유를 보정·검증해야 한다.
- 경로 거부의 대표 응답은 `No aligned road within 2m, or no directed route; vehicle retained in place`다. 인하대역 도착 뒤의 후속 요청 등에서 재현됐다. 거부된 경로를 실제 주행 검증 완료로 세지 않는다.

후문 비교 주행은 30개 시도 전부를 마치고 `FINISHED`로 종료됐다. 이는 실행기 종료 상태이며 전체 통과라는 뜻이 아니다. 관측/제어/씬 등 18개 파일의 실행 중 SHA-256 변경은 없었다. 초기+남은 42개 조합 및 별도 V03 비교를 `final-live-report.json`에 구분해 보관했다.

clear/dry, 1배속, 보행자 없음 조건이다. 정적인 도로/건물 오탐을 독립적으로 보기 위해 시험 중에만 차량 간 감지와 충돌을 분리했다. 따라서 이 시험은 다중 차량 경합/충돌 조정의 통과 근거가 아니다. 검증 종료 시 재생 중지, pending/armed=false, 차량 간 시험 충돌 무시=false, PC_Bootstrap 씬 dirty=false를 확인했다. 시험용 이동은 씬에 저장하지 않았다.

정체/거부 뒤 다음 목적지를 같은 위치에서 요청할 수 있으므로, 실패 건수는 서로 다른 지점 수가 아니다. 목적지 요청 시도 완료와 전체 경로 실제 도달을 구분한다. 이 검증은 모든 402개 방향성 차로를 빠짐없이 주행하는 시험도 아니다. 이전 2D 건물 footprint 후보 검사는 `CAMPUS_NO_OBSTACLE_VALIDATION.md`를 참고한다.

## 재현

Editor 메뉴에서 `InhaExpress/Validation/Run LiDAR Ground Tests`, `Run TTC Obstacle Stop And Resume`, `Run Campus No Obstacle Survey`를 순서대로 실행한다. `Run Remaining Campus Routes`는 `remaining-routes/config.json`의 이미 완료한 목적지와 시험용 V03 후문 출발 조건을 사용한다. 주행 중에는 같은 Editor의 씬 전환·스크립트 재컴파일을 피해야 한다. 원본 결과는 `artifacts/validation/2026-10-29-ttc-corridor/`에 보관하며, 메뉴가 이전 주행의 summary와 commands를 history에 보존한다.

```powershell
$env:PYTHONPATH="$PWD/backend/src;$PWD/.utmp/fifo-dijkstra-deps"
python -m pytest backend/tests/test_lidar_corridor.py backend/tests/test_pc_lidar.py -q -p no:cacheprovider -k 'not websocket'
python scripts/analyze_campus_no_obstacle.py --folder artifacts/validation/2026-10-29-ttc-corridor
```

Player/모바일 빌드·FPS 및 동적 다중 차량 경합 검증은 이번 기록에 포함하지 않는다. 검은 배경은 렌더링 설정과 화면을 확인한 결과이며 성능 개선 실측으로 해석하지 않는다.
