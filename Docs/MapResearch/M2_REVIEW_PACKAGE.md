# M2 실제 지도 검토 패키지 초안 계약

2026-09-28 작업 중. 이 계약은 검토할 파일의 동일성과 기본 연결 관계를 확인한다. 유효한 패키지도 차량 주행 승인, 지도 정확도, 접근성 또는 MVP 완료를 의미하지 않는다. 현재 저장소에는 이 계약을 채운 실제 캠퍼스 패키지가 없다. 사용자 지시에 따라 혼잡구역·비룡플라자 우회는 MVP 이후로 연기했으며 `zones.items=[]`를 허용한다.

`campus-sim validate-map --map <패키지 디렉터리>`는 `manifest.json`을 읽고 원본과 다섯 산출물의 SHA-256, 같은 `map_version`, 패키지 내부 상대 경로를 검사한다. 그래프·랜드마크·Stop·zone의 구조와 참조, 선언한 좌표계·원점과 기준점의 수치적 일치도 검사한다. 통과한 경우에도 `package_integrity_valid=true`, `review_content_valid=true`, `coordinate_contract_valid=true`, `road_graph_contract_valid=false`, `mvp_map_ready=false`를 반환하고 실패 종료한다. 합성 fixture의 구조 검사에는 별도로 `--allow-synthetic`을 쓴다.

```text
<map_dir>/
  manifest.json
  <원본 파일들>
  vehicle_graph.json
  pedestrian_graph.json
  landmarks.json
  stops.json
  zones.json
```

manifest는 `schema_version=1`, `map_id`, `map_version`, `data_status=REVIEW_PACKAGE`, `coordinate_frame=LOCAL_METERS`, 명시된 `crs` (`EPSG:<번호>`), WGS84 `origin_wgs84.lon/lat`, `coordinate_controls`, 원본 목록 `source_files`, 다섯 `artifacts`, `review_evidence_refs`를 가진다. 각 원본에는 패키지 상대 경로·SHA-256·취득일·출처 설명을, 각 산출물에는 상대 경로·SHA-256을 적는다. 산출물 JSON 각각에는 같은 `map_version`을 기록한다. 경로는 패키지 밖으로 벗어나거나 서로 중복될 수 없다.

## 현재 내용 검사

- 차량·보행 그래프는 `nodes[{id, position_m}]`와 방향 `edges[{id, from_node, to_node, key, geometry_m, length_m, width_m, evidence_refs}]`를 가진다. edge ID·노드 참조·polyline 끝점과 기하 길이를 확인한다. 차량 edge에는 `allowed_speed_mps`, `allowed_vehicle_classes`, `access_status=REVIEWED_ALLOWED`가 필요하고 보행 edge에는 `grade_percent`, `step_free`가 필요하다. 이는 기록 형식 검사이며 실제 통행 허가를 입증하지 않는다.
- `landmarks.items`는 ID·이름·Stop ID·근거 참조와 선택적 `required_facility_key`를, `stops.items`는 랜드마크와 차량 그래프 노드, 보행 Stop/출입구 노드·출입구 ID·step-free 상태·근거 참조를 가진다. 참조 일치와 Stop↔출입구의 양방향 보행 경로를 확인한다. `step_free_access=true`에는 모든 경유 edge가 step-free인 경로가 필요하다.
- 필수 시설 key는 `main_gate`, `inha_station`, `rear_gate`, `building_5`, `building_2`, `hitech`, `anniversary_60`, `biryong_plaza`, `dorm_1`, `dorm_2`, `dorm_3`이다. 같은 key의 중복 지정을 거부하고, 보고서에 누락 목록과 `required_facility_coverage_complete`를 표시한다. 이는 작성자가 붙인 key와 Stop 연결의 구조 확인이며 공식 시설명·실제 위치·출입구 일치를 증명하지 않는다. 비룡플라자 시설은 MVP 대상이고 혼잡/우회 zone 정책만 MVP 이후다.
- `zones.items`는 MVP에서 빈 목록을 허용한다. 선택적으로 입력한 zone은 ID·미터 좌표 polygon·차량 edge ID·경계 밖 대기 Stop ID·근거 참조를 가진다. 퇴화·자기 교차·중복 정점 polygon, 모르는 edge/Stop, polygon 안 또는 경계 위 대기 Stop을 거부한다. 마지막에 첫 정점을 반복하지 않고 검사기가 경계를 닫는다. 차량 polyline이 polygon 내부·경계에 닿거나 가로지르면 해당 방향 edge를 폐쇄 목록에 넣어야 하며, polygon에 닿지 않는 edge를 넣어도 거부한다.
- 모든 차량 Stop 쌍은 방향 edge를 따라 서로 도달 가능해야 한다. 비룡플라자 폐쇄 후 우회 가능성은 MVP 승인 조건이 아니며 후속 범위에서 검증한다. 기본 연결성 검사는 실제 통행 가능성·폭·거리·시간 적합성을 보장하지 않는다.

`manifest.coordinate_controls`에는 최소 세 개의 서로 다른 차량 그래프 노드 기준점을 넣는다. 각 항목은 `id`, `vehicle_node_id`, `wgs84: {lon, lat}`, `local_m: {x, y}`로 기록한다. 적어도 세 점이 비공선이어야 한다. 검사기는 투영 미터 CRS의 x/y 축을 요구하고, WGS84 경도·위도에서 투영한 뒤 원점 좌표를 뺀 값이 `local_m` 및 해당 그래프 노드와 각각 1 cm 이내로 일치하는지 확인한다. 역변환 왕복도 1 cm 이내인지 확인한다. 이는 입력값 사이의 수치적 일관성 검사이며 측량 정확도나 축 방향의 현장 확인을 대신하지 않는다.

다음 검증에는 근거의 진위, 원본 자료로부터의 좌표 변환 재현, 현장 통행 조건·실측 폭/경사, 실제 출입구 접근성, 시설 커버리지, zone 경계와 edge 교차의 현장 검토가 필요하다. 현재 숫자와 `REVIEWED_ALLOWED`는 패키지 작성자의 선언이며 승인 근거가 아니다. 패키지는 runtime RoadGraph 로더에 연결되지 않는다.
