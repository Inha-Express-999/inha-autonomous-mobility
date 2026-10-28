# ADR 0004 버전 4 통신 초안의 정지 전용 독립 검증

- 날짜: 2026-10-18
- 상태: 실험용 제안, 팀 공통 계약 확정 아님
- 근거: 사용자 제공 `이태호.docx`의 10월 12~18일 통신 담당 계획

## 결정

2026-10-25 보완: 로컬 v4 초안 envelope에 session_id를 추가한다. hello는 null/생략,
이후 입력은 welcome의 session_id와 일치해야 한다. 기존의 연결 암묵 바인딩만으로는
새 socket에서 재전송된 이전 ego를 구분하지 못하므로 명시 검사를 추가했다.
이 변경은 팀 공통 계약 확정이 아니며 v3에는 영향 없다. 서버/peer를 함께 재시작한다.

현재 algorithms의 프로젝트 버전 0.4.2.2와 schema-v3 API/Unity 연결은 보존한다.
새 snake_case envelope, hello/welcome, ego.state, sensor.observation,
control.command/result 왕복은 별도 FastAPI 앱과 `/experimental/runtime/ws`에서 시험한다.
기본 서비스에 등록하거나 기존 `/v1/client/ws`의 schema를 변경하지 않는다.

FakeController는 항상 throttle=0, brake=1인 정지 명령만 반환한다.
실제 서비스의 mission/route 및 제어 판단을 호출하지 않고 차량 주행 권한을 부여하지 않는다.
FakeSimulationPeer의 APPLIED는 통신 시험용 자기 보고이며 물리 제동 증거가 아니다.

## 초안에서 임시로 정한 사항

- SIMULATION/PC/MOBILE 역할은 서버의 명시적 테스트 토큰 설정에 연결한다. hello는 이를 주장하는 것만으로 권한을 얻지 않는다.
- 시뮬레이션은 연결마다 단일 차량만 소유한다. 동일 차량의 중복 연결을 거부하며 먼저 기존 연결을 닫아야 재접속 가능하다.
- hello의 run_id는 null, welcome에서 서버 run_id와 신규 session_id를 받는다. 이후 메시지는 해당 연결과 run에 귀속된다.
- 위치/속도 벡터는 east/north/height, 센서 local_position은 right/forward/up 객체다.
- sensor_config_version과 scan_id는 문자열, pose_version은 정수다.
- 이 단계에서는 관측과 최신 ego의 tick/time/pose가 정확히 일치해야 한다. 지연 관측을 위한 pose history는 아직 없다.
- seq는 연결별 type/sensor_id 스트림마다 증가한다. 단일 차량 소유 연결이므로 차량별 분리는 연결 자체로 보장한다.
- 제어 결과는 VALIDATED 후 APPLIED 순서로 시험한다. REJECTED는 별도 사유를 요구한다.
- 제어 명령은 최신 한 개만 유지한다. 기존 명령을 새 명령이 대체하며 재접속에 재전송하지 않는다.
- 프로토타입 상한은 메시지 64 KiB, 관측 64 hits, 연결당 8 sensor streams, 동시 연결 8개다. 제품 성능 목표가 아니다.
- 관측 수신 wall-clock age 1초, 명령 유효시간 0.2초, 읽기/쓰기 제한 5초는 시험 설정이며 실차 안전 제원이 아니다.
- PC/MOBILE은 역할 협상만 시험한다. 역할별 서비스 상태 projection, HTTP 어댑터와 인증 시스템 통합은 미구현이다.
- 잘못된 입력은 일반 오류와 close 1008로 연결을 종료한다. 세부 오류 코드 계약은 후속 작업이다.

## 보존하는 안전 경계와 한계

타 객체의 실제 ID/위치/미래 궤적 필드는 받지 않는다. 미지원 필드와 NaN/Infinity,
null 관측 배열, 권한 없는 차량, 이전 run, 역순 seq와 불일치 pose를 거부한다.
버퍼와 결과 로그는 크기를 제한한다. 서버 측 연결 정리가 Unity의 자체 제동을 대신하지 않는다.
센서 geometry, 제동 거리, 센서 누락에 대한 실제 안전 판단은 검증하지 않았다.

실제 Unity 수신 큐/Pump, C# codec, actuator 및 wall-clock watchdog은 이번 변경 범위 밖이다.
따라서 이번 결과로 C#과 Python 계약 일치, 실제 물리 적용 또는 전체 첫 주 목표 완료를 주장하지 않는다.
팀과 명령의 steer/throttle/brake 계약 및 기존 targetSpeed/yawRate 어댑터를 합의한 후 실제 연결한다.

## 검증

전용 30개 및 기존 회귀 포함 497개 Python 테스트 통과, 기존 의존성 경고 1개.
변경 코드 Ruff 통과. 인프로세스 ASGI 왕복과 실제 127.0.0.1 TCP WebSocket 왕복 모두 성공했다.
Unity/Player/Android 실행, 부하, 실제 차량 단절 제동은 미실행이다.

## 2026-10-25 후속: 기존 v3 PC의 정지 경로

v4 계약을 기존 v3 snapshot으로 대체하지 않는다. 기존 PC WebSocket host의 연결 상태와
snapshot freshness는 WheelCollider 차량의 **정지 요청**에만 사용한다. Bootstrap은
해당 world/role 씬의 팀 차량에 relay/bridge를 연결하며 재접속만으로 운행 허가를 주지 않는다.
기존 kinematic actor와 WheelCollider controller는 같은 차량에 중복 부착할 수 없다.
schema/프로젝트 버전 변경 및 새 wire command는 없다. 격리 3/3 Physics 검증 이후 추가된
실제 v3 host 통합 시험은 컴파일 확인 상태이며 Unity 실행 결과는 별도로 기록한다.
