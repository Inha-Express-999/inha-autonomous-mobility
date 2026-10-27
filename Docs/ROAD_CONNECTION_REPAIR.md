# 도로 연결부·외곽 누락 구간 보정

2026-10-26 · 프로젝트 0.5.0.0 이후 미커밋 작업

## 적용 범위

Unity MCP로 기존 `CampusTerrain`의 활성 아스팔트 16개를 추출해 검사하고 같은 씬에 보정했다. 수정 전 씬 사본과 월드 좌표의 원본/베이크 메시를 `artifacts/road_connections/before.unity`, `before-meshes.json`에 보존했다. 생성 메시 참조는 `Assets/CampusSim/Generated/RoadSurfaceRepair/`로 분리하며 공유 원본 메시를 교체하지 않는다.

- 접합부의 점 접촉과 좁은 쐐기 틈을 메우고 모서리를 정리한다. 개발용 closing 반경 2m, 모서리 반경 1m, 기하 간소화 허용오차 2cm를 사용한다. 실측 도로 설계 반경이 아니다.
- 도로별로 달랐던 바닥 기준을 합성 offset 0.231m로 통일한다.
- 노란 경계선을 수정된 도로 외곽으로 다시 생성하고 기존 보도·연석을 새 외곽에 맞춘다. 새 외곽 도로의 보도 폭은 합성 표시 값이다.
- 지하철역·생활관 주변은 저장된 `road_extensions.json`의 OSM 선형을 복원한다. 주변 후보라도 기존 도로망과 떨어진 별도 도로는 제외한다.
- 서버 그래프에는 있지만 기존 씬 중심선에 아스팔트가 빠진 구간은 `1390060991:3:0:0:f`, `281621314:14:0:0:f`, `481241658:0:0:0:f`로 조사됐다. 초기 누락 중심선 길이는 각각 약 45.19m, 113.51m, 15.22m다. 원본 선형을 사용하며 임의의 시설 간 직선 연결을 추가하지 않는다.
- OSM 접근 승인·실측 폭·보행 접근성을 새로 확정하지 않으며 Python 그래프·Stop·허브의 X/Z 또는 통신 계약을 변경하지 않는다. 복원된 주변 표시 도로 전체가 서버 운행 경로에 추가된다는 뜻은 아니다.

## 평탄화 방식

원본 DSM에서 별도 도로 높이 프로파일을 생성한다. 격자 두 축에 각각 4%의 높이 변화 제한을 적용한 상·하 cone envelope의 평균을 사용한다. 이는 실측 경사 보정이 아닌 시뮬레이션 개발용 완화다. 2m 격자의 각 삼각 평면으로 도로 폴리곤을 잘라, 가느다란 접합 삼각형에서 높이 보간 오차가 급경사로 증폭되는 것을 막는다. Unity Terrain의 높이 양자화 때문에 실제 메시 경사는 원본 float 프로파일의 이론적 상한과 다를 수 있으며 실제 베이크 값을 별도 검사한다.

`CampusTerrainBinding.roadPlanarGridProfile`은 이 프로파일을 선택하고, `roadHeightSource`는 별도 높이 프로파일, `roadSupportSource`는 원본 DSM을 가리킨다. 물리 Terrain은 원본 DSM에서 시작해 도로·보도·연석 아래 격자만 낮춘다. 도로 높이 프로파일로 캠퍼스 전체 지형을 대체하지 않는다. 이전 하이테크 복원 도로는 기존 endpoint grade 구조를 유지하고 연결 높이만 다시 맞춘다.

## 검증과 상태

MCP로 적용한 뒤 생성된 `collision-audit.txt`와 저장 씬을 확인했다.

| 정적 검사 | 결과 |
|---|---:|
| 활성 도로 | 16개 |
| 면 경사 감사 삼각형 | 51,543개 |
| 지형 이격 표본 | 783,465개 |
| 지형 관통 | 0개 |
| MeshFilter/MeshCollider 참조 불일치 | 0개 |
| 최소 지형 이격 | 약 0.0487m |
| 최대 면 경사 | 약 5.891% |
| 10% 초과 면적 | 0m² |

프로파일 float 수치 검증, Python 스크립트 구문 검사, 기존 `RoadSmoothingChecks` 8개를 통과했다. 위 결과는 정적 형상 검사이며 실제 차량 주행·전체 승객 상태 전이·접근성 검증 결과가 아니다.

씬 저장 시각은 2026-10-26 22:36 KST다. MCP 실행은 내부 로그의 완료와 씬/에셋 저장까지 진행했으나 스크립트 재로드 뒤 플러그인 연결이 복구되지 않아 호출 응답은 시간 초과됐다. 사용자가 캡처 없이 현재 결과 정리를 요청하여 최종 캡처를 생략했다. 최종 재연결 이후의 독립 연결 검사도 미실행이다. 중간 후보의 캡처와 이전 `after-meshes.json`을 최종 결과로 사용하지 않는다. 추후 최종 캡처는 `final-*.png`, 최종 형상 export는 `final-meshes.json`, 독립 감사는 `final-independent-audit.json`을 사용하도록 준비했다. 이 파일들이 생성됐거나 검사가 통과했다고 보고하지 않는다.

도로의 기존 Renderer 재질과 Static Batching 설정을 유지하고 새 메시 bounds를 계산한다. 높이 격자에 맞춰 면이 늘어 원래 도로보다 삼각형 수가 증가했다. 중간 후보의 과도한 재귀 분할은 제거했으나 PC/모바일 FPS·배치 수·메모리 실측은 하지 않았다. 설정 유지 또는 면 경사 감소를 렌더링 성능 개선으로 해석하지 않는다. 실행 중에는 이 Editor 계산을 수행하지 않는다.

## 재현

원본 MCP export와 지형 float export가 보존된 상태에서 다음 순서로 준비한다. Python에는 Shapely 2.1+·NumPy와 backend 좌표 변환 의존성이 필요하다. 현재 스크립트는 이 작업의 임시 패키지 경로와 기존 개발 의존성 경로를 사용한다.

```powershell
python AgentScripts/audit_missing_road_meshes.py
python AgentScripts/repair_road_connections.py
dotnet run --project AgentScripts/RoadSmoothingChecks/RoadSmoothingChecks.csproj --configuration Release
```

Unity edit mode의 `CampusTerrain`에서 `Campus > Map > Repair All Road Connections`를 실행한다. 준비 데이터는 원본 MCP export에 대응하므로 다른 씬/새 도로 구성에 그대로 적용하지 않는다. C# 변경 후에는 Unity의 스크립트 import가 끝난 상태에서 MCP를 연결한다. 장시간 수정 도중 스크립트 import와 MCP 명령을 겹치지 않는다.

최종 MCP export 뒤 `python AgentScripts/validate_road_repairs.py`로 서버 중심선의 아스팔트 누락, 도로망 분리, 도로 간 중첩부 높이 차이를 독립 검사한다. 6cm 접합부 기준은 개발 회귀 기준이며 실제 운송 안전 기준이 아니다.

정류장·허브 위치 변경은 [별도 제안](STOP_HUB_EDITING.md)을 따른다. 위치 변경 UI 자체는 이번 도로 보정에 포함하지 않는다.
