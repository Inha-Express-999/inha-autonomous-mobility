# MVP 3인 역할 분담

2026-10-08 기준 제안. 클라이언트, 서버, 총괄이 서로 다른 파일을 소유하고 계약 변경과 통합 검증을 함께 검토한다. MVP 범위는 AGENTS.md를 따르며 혼잡 정책과 비룡플라자 우회는 기본 비활성 상태를 유지한다.

## 담당과 인계 결과

| 담당 | 주 책임 | 주 작업 경로 | 인계 결과 |
|---|---|---|---|
| 클라이언트 | CampusTerrain과 Bootstrap, PC 관제와 모바일 승객 UI, 차량 Physics와 센서, 역할별 카메라·품질 | Assets/CampusSim, Unity ProjectSettings와 빌드 프로필 | PC·모바일 캡처, Unity 테스트, Player·Android 실행 기록, 센서 및 제어 왕복 로그 |
| 서버 | 요청·배차·상태 전이, RoadGraph 로딩, 경로·안전·제어, WebSocket 역할별 projection | backend, 서비스 configs | 회귀 테스트, 동일 run/map/tick의 서버 로그, API·snapshot·ACK 계약 및 오류 사례 |
| 총괄 | MVP 우선순위, 지도·Stop·접근성 근거, 공통 계약 검토, 통합·릴리스·증거 관리 | Docs, AgentScripts/MapData, 검증 artifacts, VERSION과 CHANGELOG | 승인 지도 패키지, 통합 인수표, 재현 명령과 제한, 버전 정합성 |

총괄은 현장 조사자를 배정하고 도로 폭·경사·통행·접근성의 근거를 수집한다. 서버 담당이 조사 자료를 검증해 지도 패키지로 변환하고, 클라이언트 담당이 그래프 좌표와 Unity 지형·Stop 위치를 대조한다. 근거가 없는 구간은 승인하지 않는다.

## 변경 충돌을 줄이는 작업 방식

- 각 담당은 별도 브랜치와 작업공간에서 작업한다. 동일 Unity 씬·prefab의 YAML을 동시에 편집하지 않는다.
- Bootstrap과 역할 씬은 클라이언트 담당이 소유한다. CampusTerrain의 환경 수정도 같은 담당이 조율한다.
- schema_version, map_version, 좌표·단위, 요청 상태와 reason 변경은 구현 전에 세 담당이 예제 payload와 호환성 영향을 검토한다.
- 서버 snapshot과 Unity DTO를 같은 통합 단위로 검증한다. 모바일의 다른 승객 정보 제한은 서버 projection에서 보장한다.
- VERSION, 앱·서버 버전, CHANGELOG와 태그는 총괄이 커밋 직전에 일치시킨다. 커밋마다 네 자리 버전을 올리며 push는 사용자가 진행한다.
- 담당의 구현 완료와 통합 완료를 구분한다. 인계에는 변경 파일, 재현 명령, 실제 결과, 미검증 항목을 포함한다.

## 다음 구현 순서

1. 로컬 변경 보존 및 원격 보고서 커밋 통합. 변경 유실과 충돌 표식이 없는지 검증한다.
2. PC·Mobile Bootstrap에서 CampusTerrain과 각 역할 씬을 직접 Additive 로드한다. 2026-10-08 사용자 지시에 따라 CampusWorld 중간 씬과 meta를 삭제했다.
3. 공통 UI 색상·선택·비활성 상태를 UI_DESIGN_SYSTEM.md에 맞추고 PC 가로·모바일 세로와 Safe Area를 검사한다. 서버 값과 데모 값은 명확히 구분한다.
4. 현장 지도와 Stop·접근성 승인, Unity 좌표 계약을 확정한다. 합성 graph를 실제 캠퍼스에 임의 배치하지 않는다.
5. 승인 지도에서 요청→배차→승차→주행→하차→완료를 연결한다. 센서 지연·무효, 전방 장애물, 연결 중단, 취소·거부를 함께 검증한다.
6. PC Player와 Android에서 같은 서버로 동시 실행하고 기준 기기·해상도·품질·FPS·프레임 시간·메모리를 기록한다. 50개 연결 부하와 보행자 부하는 별도로 측정한다.

## 통합 완료 조건

실제 지도·Stop 승인, Unity Physics와 센서 왕복, 역할별 요청·상태 표시, 단절과 안전 정지·복구, PC·Android 실행 증거가 필요하다. 합성 테스트나 UI 스타일 변경만으로 실제 캠퍼스 MVP 완료를 선언하지 않는다. 최신 상태와 실행별 한계는 MVP_EXECUTION.md 및 implementation_status.md에 기록한다.
