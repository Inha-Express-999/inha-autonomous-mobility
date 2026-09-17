# 인하대학교 시뮬레이션 맵

## 사용

`Assets/InhaCampus/InhaCampus.unity`를 열면 전체 맵을 볼 수 있습니다. 현재 에디터에 이 씬을 열어 두었습니다.

다른 시뮬레이션 씬에는 `Assets/InhaCampus/InhaCampusMap.prefab`을 배치하세요. 프리팹에는 환경 지오메트리만 있습니다. 카메라와 햇빛은 미리보기 씬에만 존재합니다. 플레이어, 사람, 차량, UI, 게임 로직은 없습니다.

- 26개 건물 파트, 도로 및 보행로 71구간
- 인경호와 본관 앞 긴 수로
- 612그루의 절차적 수목, 잔디, 운동장, 포장면
- 건물 외벽·지붕, 도로·보도·지면, 나무 줄기의 정적 충돌체
- 교차로를 합친 연속 도로 메시와 보행로 접속부의 낮춘 경계
- 232개 렌더러, 67개 충돌체. 수목 잎은 투명도 컷아웃 재질을 사용합니다.

## 좌표 및 정확도

원점: WGS84 위도 37.4506, 경도 126.6535. Unity 1단위 = 약 1m. +X는 동쪽, +Z는 북쪽, +Y는 위쪽입니다. 작은 캠퍼스 범위의 국소 평면 투영입니다.

건물 외곽선, 5호관 중정, 도로 중심선, 캠퍼스 경계와 수면 외곽은 실제 OpenStreetMap 좌표에서 생성했습니다. 인접 인하공업전문대학의 별도 캠퍼스 건물은 포함하지 않습니다. 지도에 없는 건물은 자동으로 보완되지 않습니다.

**실측 디지털 트윈이나 사진 수준의 복제본은 아닙니다.** 공개 데이터에 없는 높이, 층수, 창문 배열, 재질, 보도 폭, 수목 위치는 추정입니다. 본관 전면 기둥과 유리 입구는 공식 사진을 참고한 근사 모델입니다. 60주년기념관·하이테크센터 등의 세부 단차와 내부 공간은 정밀 재현하지 않았습니다. 지면은 평면이며 측량된 고도 데이터는 없습니다. 수면은 시각적 면으로, 수심·부력 시뮬레이션은 없습니다. 첨부 이미지의 UI와 차량 등은 구현 대상에서 제외했습니다.

건물 단위 정밀 시각 재현에는 추가 입면 사진, 높이/층별 도면, 지형 측량 자료가 필요합니다.

## 검증

- 모든 authoring C# 스크립트를 Unity Roslyn으로 컴파일하고 실행했습니다.
- 씬 저장 후 재로드, 플레이 모드 진입/종료를 확인했습니다.
- 최종 검사 당시 Unity Console 오류 0, 경고 0. 초기 지면 MeshCollider 경고는 BoxCollider로 변경해 해결했습니다.
- 도로/보행로 선분 중점 196개를 레이캐스트해 196개 모두 해당 도로 충돌면을 확인했습니다.
- 누락 스크립트 0, 저장되지 않은 메시 참조 0, 지원되지 않는 재질 0.
- 맵 프리팹 안 MonoBehaviour 0, Rigidbody 0, Canvas 0.
- 검증 세부 정보: `CampusValidation.json`. 성능 벤치마크와 전체 차량 주행 테스트는 수행하지 않았습니다.

## 재생성

완성된 씬/프리팹은 이미 저장되어 있으므로 스크립트 실행 없이 사용 가능합니다.

보존된 원본 OSM에서 데이터를 다시 변환하려면 Python 3와 Pillow, Shapely 2.1 이상이 필요합니다. 작업 환경에서는 Shapely를 `Temp/campus-python`에만 설치했습니다. 이 임시 디렉터리는 Unity의 배포 자산이 아닙니다.

1. `AgentScripts/prepare_campus.py`
2. `AgentScripts/prepare_road_surfaces.py`
3. 기존 결과를 별도 백업한 깨끗한 프로젝트 복사본에서 Pipeline `run_script`로 `BuildInhaCampus.cs`, `PolishInhaCampus.cs`, `FinalizeInhaCampus.cs` 순서로 실행합니다. 각 파일의 정적 Main이 진입점입니다. 생성기는 기존 완성 씬 덮어쓰기를 거부합니다.
4. `ValidateInhaCampus.cs`로 검증합니다.

## 출처와 라이선스

- 지도 데이터 © OpenStreetMap contributors, ODbL 1.0: https://www.openstreetmap.org/copyright
- 원본 API: https://www.openstreetmap.org/api/0.6/map?bbox=126.648,37.445,126.659,37.454
- 캠퍼스 경계: https://www.openstreetmap.org/way/472447787
- 본관 사진: 인하대학교 70년 디지털 역사관 https://heritage.inha.ac.kr/digital-history (참고 이미지 https://heritage.inha.ac.kr/images/data/2.jpg)
- 검색으로 확인한 인하대학교 정석학술정보관·60주년기념관 외관 이미지와 사용자 첨부 이미지를 시각 참고로 사용했습니다. 외부 사진을 텍스처로 재배포하지 않습니다.
- 맵 데이터 파생물 배포 시 OpenStreetMap 기여자 표시와 해당 ODbL 조건을 유지하세요. UI를 만들지 말라는 요청에 따라 출처는 이 문서와 소스 안내문에 보존했습니다.
- 생성일: 2026-09-15. OSM은 커뮤니티 지도이며 현장 현황의 완전성/최신성을 보장하지 않습니다.
