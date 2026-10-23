# Unity 물리 자동 운행 검증 · 0.4.9.0

실제 Unity MCP로 기존 PC Bootstrap/Additive CampusTerrain의 V01을 선택하고 본관 앞 Stop 후보를 골라 자동 운행 시작 버튼을 실행했다. Unity 위치를 옮기거나 키보드/수동 조향을 사용하지 않았다.

- 자동 이동 24.7836m, 추적 카메라 이동 24.7790m. 목적 후보에서 0.5984m 이내 정지/도착(개발 도착 허용 반경 0.65m). 수동 운전 UI 없음.
- 실제 차량 앞에 검증용 임시 BoxCollider를 생성하고 전방 Raycast 거리만 Python에 전송했다. 장애물 앞 속도 0.0000177m/s, 제동 사유 확인 후 검증 객체를 제거하고 자동 재출발/도착했다. 장애물 실제 Transform은 서버에 전송하지 않았다.
- 첫 시도는 장애물 앞 정지 후 저속 명령이 재발해 엄격한 sensor_stop 판정이 실패했다. first-attempt/에 그 결과를 보존한다. 0.4m 해제 여유의 제동 hysteresis 적용 후 재검증은 모든 조건을 통과했다.
- automatic-driving.png 및 automatic-arrived.png/JSON에 실제 렌더링 화면과 잘림 0건을 기록했다. 정적 지도 187노드/3허브/14Stop 후보 유지.
- server-info.json/web-final.json/final-observation.json은 같은 run_id이며 웹 V01의 motion_source는 unity_physics다. post-first-run-observation.json은 최초 실행 종료 후 별도 조회한 관측이다.
- 전체 Python 222개, 자동 운행 신규 10개, JavaScript 문법 검사를 통과했다. 이 단계의 Windows Player 빌드는 미실행이며 후속 차선 통합에서 최종 빌드한다.

자세한 계약·제약: ../../Docs/UNITY_PHYSICS_AUTODRIVE.md. 사용자의 후속 차선 요청은 다음 버전에서 진행한다. 전체 승객 서비스 상태 전이·동적 회피/RRT·모바일/성능·실차 안전 검증 완료를 뜻하지 않는다.
