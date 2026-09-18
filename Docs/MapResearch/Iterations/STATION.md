# 인하대역 진입부 초안 — 접근성 출입구 미완료

원본 `expanded_campus.osm`에서 railway=subway_entrance인 1~7번 출구를 확인했다. 기존 후보 목록의 railway=station/stop 노드는 출입구 좌표로 사용하지 않았다.

7번 출구는 node/4741793213, 경위도 126.6502630 / 37.4478074이며 wheelchair=no가 명시되어 있다. 이 좌표에 간략한 일반 진입부 파빌리온을 생성했다. 외형·방향·치수는 합성 초안이고 실제 역사 도면을 재현한 것이 아니다. 지하 계단·승강장·교외 도로 연결은 아직 없다.

장애인전용 출입구는 위치 근거가 없어 null로 유지했다. 역 전체 노드의 wheelchair=yes를 특정 출구에 적용하지 않는다. 해당 랜드마크는 routeValidated=false, 상태는 osm_exit_7_wheelchair_no_accessible_entrance_unresolved다. 출입구 검사도 누락된 쌍을 INCOMPLETE로 기록한다.

씬에 표시한 랜드마크는 총 7곳이지만, 분리 출입구 쌍이 있는 곳은 기존 6곳뿐이다. 비룡플라자·제1/2/3생활관의 형상, 인하대역 접근성 출입구와 모든 보행/차량 연결은 후속 작업이다. 최종 렌더는 station.png다.
