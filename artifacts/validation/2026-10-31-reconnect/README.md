# Unity 재연결 검증 (2026-10-31)

Unity 6000.3.21f1 Editor를 Hub에서 열어 PlayMode 시험 실행: Passed 1/1, Failed 0.
근거: playmode-results.xml. 기존 v0.5.3.1 + 미커밋 시험 보완/지도 manifest 재생성 상태.

범위: 실제 UnityAutonomousClient와 실제 Python loopback WebSocket, kinematic Rigidbody 시험 객체 1대. 서버가 첫 응답 후 연결을 닫고, 단절 중 시험이 위치를 변경한 뒤 두 번째 연결의 첫 패킷에 새 위치가 포함되는지 확인. 위치가 되돌아가지 않으며 정지 명령 호출이 이어지는지도 검사.
한계: 실제 차량 프리팹의 WheelCollider 제동 거리·CampusTerrain 주행·3대 운행·서버 재시작 복구 증거는 아니다.

수정: 시험 프로젝트 Vehicles 모듈 추가, Hub 실행용 Python 경로 파일, 오류 stderr 기록, 정지 명령 호출 횟수 확인. 이전 버전 증가 후 누락된 지도 manifest를 정식 map_builder로 재생성했다. 도로/경로 데이터 변경은 없다.
