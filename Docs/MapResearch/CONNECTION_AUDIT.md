# 확장 OSM·Unity 미연결 거점 조사

2026-10-15 · 프로젝트 0.2.2.0 작업 트리 · 대상: 하이테크, 생활관 1·2·3, 인하대역

## 후속 적용 완료

사용자 요청에 따라 아래 후보를 기본 **inha-osm-unity-v2** 가상 지도에 반영했다. 현재는 14개 Stop 후보 모두 왕복 가능하고 역의 Unity 중심선 기반 우회 복귀를 사용한다. V01 한 대·30km/h·배터리 제외를 유지한다. 현재 구현/검증은 [서버 안내](../server_m0.md)와 `artifacts/osm_extension_runtime_validation.json`을 따른다. 아래 내용은 적용 전 조사 기록으로 보존하며 실제 통행/Stop/접근성 승인은 계속 미완료다.

## 초기 조사 결과(적용 전)

다섯 곳 모두 확장 OSM에는 방향성을 지킨 왕복 후보가 있다. 현재 기본 서버에 없는 이유는 OSM 도로의 부재로 단정할 수 없으며, 기존 캠퍼스 범위 잘림·도로 등급 정책·출입구 위치 가정·Unity 적용 범위를 구분해야 한다. 이번 조사는 후보를 찾는 작업으로, 기본 운행 지도는 기존 9개 왕복 후보를 유지한다. 원본/Unity 씬은 수정하지 않았다.

| 대상 | 도로 위 후보 / 출입구까지 평면 간격 | 현재 미연결 원인과 발견한 대안 |
|---|---|---|
| 하이테크 | way 481241714 / 32.47m | 정문→481241683→481241689→481241658→481241696→481241715→481241714로 왕복 가능. 기존 경계에서 잘린 약 64.65m 구간이 road_extensions.json에는 있다. service 도로만으로 가능하며 기존 core 범위 제한이 연결을 끊었다. |
| 생활관 1 | way 1390060991(residential) / 5.62m | service 전용 모드의 가까운 way 1098489687은 51.09m 떨어지고, 진출 단독 일방통행으로 이 모드에서는 복귀 불가다. 새 후보는 1390060991→281621314(소성로, secondary)를 경유해 정문과 왕복한다. 기존 출구의 방향을 뒤집어 만든 연결이 아니다. |
| 생활관 2 | way 471332807(service) / 12.05m | 가까운 service 도로는 있지만 캠퍼스와 연결하려면 284010313→731224474→281621314(secondary)를 거친다. service 전용 정책에서는 양방향 단절이다. scoped/restored 자료 외에 약 155.03m가 일반 extension export에만 있는 것으로 대조됐다. |
| 생활관 3 | way 471332807(service) / 22.22m | 생활관 2와 같은 외곽 축을 통해 왕복 가능하다. 현재 Unity 출입구는 공식 위치도 기반 근사 형상/합성 출입구이며 실제 문 위치가 확정되지 않았다. 일반 extension export에만 있는 구간은 약 131.56m다. |
| 인하대역 7번 출구 후보 | way 955217469(독배로, secondary) / 15.95m | 출발 경로는 481241683→281621314→955217469. 최단 복귀에는 way 1215102704(secondary_link) 약 72.12m가 포함되며 현재 네 종류 Unity 중심선 export에서 찾지 못했다. 이 way를 제외하면 955217469→281621314→481241683의 407.16m 복귀 대안이 있고 core/scoped 자료로 대조된다. |

간격은 합성/미검증 일반 출입구에서 도로로 투영한 직선 분리 거리다. 이 직선을 차량 도로나 보행 연결로 추가하지 않았다. 안전한 Stop, 횡단 위치, 접근성 또는 실제 정문↔시설 운송 가능 승인이 아니다.

## 30km/h 기준 후보 거리·시간

사용자 요청에 따라 기본 기준 순항속도를 30km/h(8.333333333333334m/s)로 변경했다. 원본의 더 낮은 숫자형 제한이 있으면 그 값을 우선한다. 아래는 픽업이 정문 거점인 후보 경로의 가상 주행 시간이며 정차·신호·혼잡·가감속·센서 판단은 반영하지 않았다.

| 대상 | 정문→후보 거리 | 후보→정문 거리 | 왕복 가상 시간 |
|---|---:|---:|---:|
| 하이테크 | 654.25m | 654.25m | 157.02초 |
| 생활관 1 | 260.61m | 260.61m | 62.55초 |
| 생활관 2 | 985.72m | 985.72m | 236.57초 |
| 생활관 3 | 914.57m | 914.57m | 219.50초 |
| 인하대역 — Unity export 기반 대안 | 407.16m | 407.16m | 97.72초 |

역의 제한 없는 최단 복귀는 349.72m이며 왕복 90.83초다. 위 표는 Unity export에 없는 구간을 피하는 대안을 사용했다. 도로 전체를 제외하는 보수적 탐색이므로 모든 메시 제약을 반영한 전역 최적해라고 주장하지 않는다.

기본 서버의 기존 정문→도서관 경로는 왕복 692.41m로 동일하며, 시간은 기존 2m/s의 346.20초에서 30km/h의 약 83.09초로 변경됐다. 30km/h는 강제 하한이 아니며 실제 감속/정지 제어는 아직 별도 작업이다.

## 조사 입력과 파일 구조

```text
Docs/MapResearch/2026-09-17/expanded_campus.osm
Assets/InhaCampus/Source/campus.json
Assets/CampusSim/Data/{road_connections_scoped,road_connections_restored,road_extensions}.json
Docs/MapResearch/Iterations/EntranceInventory/entrances.csv
                 ↓
backend/src/campus_sim/connection_audit.py
                 ↓
artifacts/osm_connection_audit.json
```

- core/scoped/restored/일반 extension export를 구분한다. export에 있다는 사실만으로 활성 씬에 적용됐다고 해석하지 않는다.
- 저장 씬에는 `Campus scoped connections`와 `Restored dormitory road connections` 오브젝트의 m_IsActive=1 기록이 있다. 실시간 Editor/상위 계층·Collider·높이·렌더 검사는 수행하지 않았다.
- 세 모드로 탐색한다: service만, local 도로 추가, secondary/secondary_link 추가. 고속 trunk·보행로를 차량 후보로 사용하지 않는다. 조건/방향 제한이나 회전 상태를 처리할 수 없는 길은 보수적으로 제외했다.
- 같은 OSM node ID로만 접속하고 home/Stop 투영점은 원래 OSM 선분을 분할한다. 가까운 도로끼리 또는 교차하는 선끼리 임의로 연결하지 않는다.
- Unity 대조는 같은 OSM way의 중심선만 사용한다. 최대 2m 간격 midpoint와 0.05m 오차 기준의 추정이며 메시 표면/도로 높이 검사가 아니다. 역 대안은 누락 way 전체를 제외해 다시 탐색했다.
- 생활관 3의 과거 행사 안내 도착 좌표와 현재 Unity 근사 출입구는 같은 검증점이 아니다. 이 보고서는 현재 인벤토리의 일반 출입구만 기준으로 삼는다. 실제 위치·접근성 확인이 필요하다.
- JSON에 입력/코드/현재 지도 manifest 해시, 후보별 도로 ID·양방향 경로·제외 사유·Unity 대조·미지원 제한을 기록한다. OSM 자료의 ODbL 출처는 기존 ATTRIBUTION을 유지한다.

## 실행·검증과 남은 작업

```powershell
python -m campus_sim.connection_audit
python -m pytest backend/tests -q -p no:cacheprovider
python -m ruff check backend/src backend/tests
```

Python 3.14.4에서 61개 테스트와 Ruff/format check를 확인했다. 일방통행 복귀 불가, 같은 선분 분할, 교차선 오접속 방지, 후보별 실제 경로·역 우회, 30km/h와 낮은 원본 제한 우선 적용을 검증한다. HTTP 테스트는 이 환경에서 로컬 소켓 권한이 필요했다. Python 3.11·Unity 실주행·성능 측정은 미검증이다.

다음 적용 작업은 하이테크 잘림 구간의 범위 반영, 생활관 외곽의 사용 도로 등급/승인 범위 결정, 일반 extension에만 있는 구간의 씬 대조, 역 복귀 대안 선택, 다섯 곳의 Stop·보행 연결·횡단·폭·경사 확인이다. 미확인 조건을 완화해 운송 가능으로 표시하지 않는다.
