# Unity MCP UI 검증 · 0.4.7.1

실제 Unity Editor 6000.3.21f1에서 PC_Bootstrap/Mobile_Bootstrap을 각각 Play Mode로 실행했다. CampusWorld → CampusTerrain과 역할 씬의 Additive 구성을 유지했다. 각 PNG는 렌더링 완료 프레임에서 캡처했으며 같은 이름의 JSON에 화면·카메라·씬·지도 좌표 검증을 기록했다.

- PC: 1280×720, 1024×768. 모바일: 390×844, 360×640. 모든 화면에서 텍스트 경계 검사 잘림 0건이며 하단 탭/버전 표시를 시각 확인했다.
- mobile-resize-before.json은 화면 변경 후 aspect가 1.074617에 고정된 재현 기록이다. 수정 후 360×640에서 기대값 1.308140과 일치한다.
- 노드 187개, 허브 3개, Stop 후보 14개. 서버 지도와 Unity XZ 최대 오차 약 0.00006824m. 후보 배치는 실제 승하차장 승인/접근성 검증을 뜻하지 않는다.
- native-pc는 최종 Windows Development Player이며 자동 실행 서버와 Editor의 네 화면 및 웹 상태 GET 응답이 같은 run_id를 사용한다. server-info.json/web-state.json 참조. 서버는 paused=true이며 차량 조작/호출을 실행하지 않았다.
- Python 버전/지도/번들 대상 검사 6개 통과. MCP 전체 PC 빌드 성공. 선택 Unity Pipeline의 RuntimePipelineConfig 미설정 경고는 남아 있다.
- 패키징 오류 회귀: 256개 파일 잠금, 오류 82,418자에서 4.823초 만에 예상 실패를 반환하고 잠금을 모두 해제했다. stdout/stderr 동시 읽기로 Editor 교착을 방지한다.
- 모바일 실기기·노치·OS 회전·FPS 및 센서/물리/차량 제어는 미검증이다. Editor Play Mode를 종료하고 임시 해상도 프리셋을 제거했다. 최종 PC 앱과 자동 시작 서버는 실행 상태로 남겼다.
