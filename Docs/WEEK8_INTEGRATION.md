# 8주차 팀 작업 통합

프로젝트 0.5.3.0 · 2026-10-28 · 브랜치 `codex/week8-integration`

## 포함한 작업

| 담당 | 원본 | 반영 범위 |
|---|---|---|
| 이상엽 | `020f6317` | 최신 실제 지도, 물리 운송, 3대 배차, PC 관제와 서비스 포인트 편집을 기준선으로 유지 |
| 최장현 | `53bb8ae6`, `origin/vehicle-simulation` | 독립 LiDAR, Ray/점군 시각화, 시연 씬과 재질을 이력 보존 병합 |
| 이태호 | `05388eb0`, `30d2614f`, `78500582` | 통신 복구, 제동 게이트, 차량 bridge, 팀 프리팹 및 독립 시험을 선별 반영 |

`algorithms` 전체를 병합하면 이전 합성 Python 서버가 현재 물리 운행 구현과 충돌한다.
8주차 추가 코드와 필요한 DTO, WebSocket data source, runtime host 및 합성 이동
충돌 검사 의존성만 반영했다. 과거 합성 Python 서비스와 평가 전체를 병합한 것은 아니다.
원본 브랜치와 커밋은 변경하지 않으며 출처는 `week8_import_manifest.json`에 기록한다.

## 충돌과 의존성

- 최신 CampusTerrain, Python 서비스, 실제 차량 제어와 Bootstrap 구조를 유지하고 실제 PC 센서 제어 계약을 확장한다.
- 기존 VehicleController GUID를 보존하고 가져온 팀 프리팹의 참조를 맞춘다.
- Physics와 LiDAR는 InhaExpress.Simulation 어셈블리에 둔다. 차량 제동 relay와
  binding도 같은 어셈블리로 옮겨 Presentation과의 순환 참조를 방지한다.
- Simulation은 Domain, Presentation과 Input System을 참조한다. 차량의 씬/프리팹
  class identifier만 맞추며 공유 재질과 차량 형상은 변경하지 않는다.
- 제거된 키 입력은 독립 LidarSensorDemo 전용 어댑터로만 제공한다. PC 캠퍼스 자동 운행에는 수동 입력을 추가하지 않는다.
- 통신 DTO는 기존 생성자의 선택 인수로 확장한다. 독립 정지 전용 gateway와 기존
  물리 운행 계약을 같은 schema라고 가정하지 않는다.
- 프로젝트 버전과 지도 build 정보 및 Unity 지도 export를 갱신한다. 지도 schema와 독립 정지 gateway schema 및 Unity Editor 6000.3.21f1은 유지한다. PC 물리 운행은 protocol 5를 사용한다.

## 검증

```powershell
$env:PYTHONPATH="$PWD/backend/src;$PWD/.utmp/fifo-dijkstra-deps"
python -m pytest backend/tests -q -p no:cacheprovider
./AgentScripts/TestTransportSafetyGate.ps1
./AgentScripts/RunTransportBrakeTests.ps1
```

`.utmp`는 이 PC의 기존 의존성 경로다. 다른 환경에서는 lockfile에 맞춘 환경을 사용한다.
가져온 과거 XML은 당시 소스의 검증 근거이며 신규 통합 시험 결과로 간주하지 않는다.

최종 검증: Python **390/390**, Unity EditMode **57/57**, PlayMode **10/10**, 순수 C# 게이트 **37 assertions** 통과. PlayMode는 지원 transport 7개, LiDAR 생성/제거·연출/후방 관측 2개와 실제 PC Bootstrap 1개다. 실제 PC 시험은 동일 서버/run ID에서 차량 **3대 모두** protocol 5의 유효 스캔과 지속 tick 갱신을 확인하고 소유 Python 정상 종료를 확인했다. 이 Bootstrap 시험은 차량을 운송 임무로 움직이지 않았다. LiDAR→제어 WebSocket과 장애물 정지/clear 재출발, 후진 위험 우선 제동은 Python API/제어 시험에서 확인했다. 그래픽 화질이나 캠퍼스 실제 제동 거리 실측과 구분한다.

필수 오류 Ruff 검사 및 웹 JS 구문 검사도 통과했다. Python의 기존 Starlette deprecation 경고 1개가 남는다. [최종 원본 XML과 요약](../artifacts/validation/2026-10-28-week8-integration/summary.json)에 범위를 보존한다. protocol/정책 결정은 [ADR 0013](ADR/0013-continuous-pc-lidar-protocol5.md)을 따른다.

## PC LiDAR 시험 통합

PC_Bootstrap → 기존 Additive 씬 → WebClientHud → 차량별 UnityAutonomousClient의
실제 운행 경로에 연결했다. Presentation의 인터페이스와 Simulation의 등록 factory로
어셈블리 순환 참조 없이 차량마다 센서를 자동 부착한다. 사용자가 센서를 수동 추가할 필요가 없다.

- 지속 관측: 차량 3대 각각 10 Hz, 수평 360개 × 수직 7개 = 2,520 Raycast, 거리 15m.
  PC 수직 범위는 -30~+15도로 위쪽에서 내려오는 물체도 관측 범위에 포함한다.
  독립 시연 씬의 기존 채널 설정은 유지한다. 22채널 성능 검증을 주장하지 않는다.
- 자기 차체를 제외하고, 관측된 위쪽 법선과 차체 하단 높이로 평평한 바닥 반환을
  장애물에서 분리한다. 바닥 반환은 점군 표시에는 남는다. 다른 차량/물체의 Transform,
  Rigidbody 속도, 미래 궤적이나 객체 ID를 Python에 전송하지 않는다.
- protocol 5는 동일 tick의 전체 hit 관측, 스캔 유효성과 감지 거리를 전송한다.
  관측을 조용히 잘라 보내지 않는다. Raycast 버퍼 포화, 관측 누락/지연/부정합과
  통신 단절은 운행을 허용하지 않는다. protocol 1~4의 기존 시험 계약은 보존한다.
- `campus_lidar` 프로필로 직접 운행과 실제 배차를 연결한다. 시험 상한은 18km/h이며
  2m/s² 감속, 반응 .3초와 .5m 여유는 **개발 가정**이다. 실측 제동 성능이 아니다.
  이전 고속 프로필 이름을 보내도 protocol 5의 LiDAR 안전 검사를 우회할 수 없다.
- TTC/정지거리와 차체 폭/길이 및 원래 차로 경로의 관측 위험을 회차 제어보다 먼저
  확인한다. 정지 후 5개 연속 clear scan을 확인해야 재출발한다. 동적/미분류 반환은
  여유 폭을 늘려 보수적으로 정지하며 실제 상대속도를 추정했다고 주장하지 않는다.
- 근접 조우나 서버 안전 정지 시 기존 셰이더 기반 포인트 클라우드를 자동 표시한다.
  PC 전체 장면, 카메라와 관제 UI를 숨기지 않는다. 표시만 샘플링/수명 제한하고,
  센서 및 안전 계산은 계속 모든 관측을 사용한다. 독립 데모의 수동 고립 뷰는 유지한다.
- 향후 트리거는 물리 Collider를 가진 객체를 생성하면 된다. 선택적으로 `LidarTarget`의
  관측 의미 분류를 지정한다. Trigger-only Collider는 물리 센서에서 제외되므로 별도
  비트리거 Collider가 필요하다. 보행자 이동/낙하 생성 버튼 자체는 이번 범위에 없다.

### RRT와 검증 경계

장애물이 남아 있으면 차량을 먼저 제동한다. 정적 의미 분류와 직선 차로 폭을 사용할 수
있는 경우 기존 RRT에 센서 관측을 전달해 기하 후보를 계산한다. 미분류/동적 대상은
`WAIT_DYNAMIC_VALIDATION`, 차로 경계가 충분하지 않으면 `WAIT_VALIDATED_CORRIDOR`다.
모든 후보는 `executable=false`를 유지한다. `lidar_rrt_status`로 상태를 확인할 수 있다.

**현재 동작은 정지/대기/관측 해소 후 재출발이며, RRT 후보를 실제 회피 주행 명령으로
적용하지 않는다.** 후보의 조향/차체 경계/시간 궤적 및 동적 충돌 검증이 추가되어야 한다.
동적 객체 추적/상대속도 추정, 센서 사이의 미관측 공간, 차량 간 통행 우선권과 교착 해소,
전체 거점 운송, 모바일 실기기 및 렌더링 성능은 완료로 간주하지 않는다.

### 별도 통신 코드의 경계

가져온 WebSocketClientDataSource의 v3 snapshot 경로와 이전 `/v1/client/ws` 서비스는
현재 PC protocol 5 제어 채널과 별개다. 정지 전용 gateway에 운행 권한을 부여하거나
제동 bridge를 기존 운행 차량에 자동 추가해 명령 소유권을 중복 생성하지 않는다.
과거 v3 backend를 요구하는 시험 2개는 별도 실행에서 실패했으며 삭제하지 않았다.
기본 transport 검사는 현 브랜치가 지원하는 7개를 명시적으로 선택한다.
이전 `-IncludeClientIntegration` 모드는 명시적으로 거부한다.

PC/모바일 Player 빌드, 시각적 화질, FPS/프레임 시간/메모리는 이번 headless 시험의 검증 범위가 아니다.
