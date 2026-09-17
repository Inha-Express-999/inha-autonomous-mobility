# 인하 캠퍼스 지도 개선 계획 및 확보 자료

프로젝트 기준: 0.1.0.0 · 조사일: 2026-09-17 · 상태: 계획/데이터 준비, Unity 씬 미변경

## 확정한 방향

- 정밀 디지털 트윈보다 실제 캠퍼스의 배치·실루엣·대략적인 기복을 알아볼 수 있는 스타일화된 전경을 만든다.
- 사용자 정정: 기숙사는 제1·2·3생활관만 포함한다. 제4생활관은 없다. 역은 인하대역이다.
- Flat Kit로 지형/건물 색감을 통일하고 Stylized Water 3로 인경호/수로, Idyllic Fantasy Nature로 녹지를 구성한다.
- Terrain 편집 후 다시 베이크하는 Editor 워크플로를 목표로 한다. 런타임 자유 지형변형은 이번 범위가 아니다.
- 외형과 고저차는 근사 허용. 통행 금지·계단·출입구 연결·이동지원 접근성은 별도로 검증한다.

## 확보한 데이터

| 자료 | 위치 | 현재 상태/용도 |
|---|---|---|
| 기존 OSM | Assets/InhaCampus/Source/campus.osm | 원본 보존. 범위 126.648~126.659 / 37.445~37.454 |
| 확장 OSM | 2026-09-17/expanded_campus.osm | 신규 다운로드. 범위 126.645~126.665 / 37.443~37.457. 서비스 영역 확정값이 아닌 조사 영역 |
| Copernicus GLO-30 | 2026-09-17/copernicus_n37_e126.tif | N37/E126 원본 29,862,574 bytes. 약 30m DSM, 건물·수목 포함 |
| 고도 메타데이터/약관 | 2026-09-17/copernicus_metadata.xml, copernicus_eula.pdf | 수직 기준 EGM2008, source WGS84. 약관 원본 보관 |
| 다운로드 이력 | 2026-09-17/downloads.json | URL·UTC 취득 시각·크기·SHA-256·성공 여부 |
| 인하공전 공식 안내 | 2026-09-17/inhatc_campus.html | 본관, 1~11호관, 다온관, 실습장, 정문/후문 등 명칭 확인 |
| 인하대 캠퍼스 참고 | 2026-09-17/inha_campus_reference.html | 인하대 연구실의 공식 도메인 캠퍼스 지도 링크 확보 |
| 가공 고도·지도 후보 | ../../maps/inha_relief_research/ | 아래 적용 결과 참조 |

HTML과 전체 고도 타일은 로컬 연구 캐시로 Git 제외한다. 공개 데이터 원본과 manifest는 별도 보존하며, 배포에 앞서 약관과 출처를 함께 제공해야 한다. 외부 지도 사진은 런타임 텍스처로 넣지 않았다.

## 고도 자료와 오픈소스 도구 선택

- **채택:** Rasterio 1.5.1(GDAL 기반 래스터 읽기/재투영), pyproj 3.8.0(좌표 변환), NumPy 2.5.3(가공), Pillow 12.3.0(비교 이미지).
- **열람 대안:** QGIS는 오픈소스 데스크톱 GIS다. 가공 GeoTIFF를 열어 확인할 수 있으며 이번 작업에서 QGIS 자체는 설치하지 않았다.
- **데이터:** Copernicus는 오픈소스 소프트웨어가 아니라 별도 라이선스로 제공되는 공개 DSM이다. AWS 공개 타일을 API 키 없이 확보했다.
- **대안 조사:** OpenTopography는 같은 종류의 고도 자료/API를 제공하지만 키가 필요한 경로가 있어 이번에는 사용하지 않았다.
- **정밀 대안:** 국토지리정보원 자료는 검토 후보. 인하대 고해상도 원본의 접근 가능성/사용 조건은 미확정이며 다운로드 완료로 표시하지 않는다. 사용자가 대략적인 형태를 요청했으므로 이를 기다리며 구현을 지연하지 않는다.

기존 OSM에는 `ele`가 0개다. 건물 `height`와 `building:levels`는 지면 표고로 사용하지 않는다. 기존 생성기는 모든 점의 y=0, 도로 폭과 일부 건물 높이는 가정값이다.

### 실제 적용한 데이터 가공

1. Copernicus GeoTIFF에서 원점 경도 126.6535, 위도 37.4506 주변을 AEQD 미터 좌표로 재투영.
2. x/z=-900부터 2048m 정사각 영역을 257×257 격자로 추출. 8m 간격은 보간 간격이며 원본 정확도가 좋아진 것이 아니다.
3. 원본 DSM 재투영본을 별도로 저장.
4. 32m 간격에서 3×3 이웃의 20백분위값을 취하고 3×3 평균 2회 후 보간하여 대략적 지형을 생성.
5. 원점 인근 값을 기준 높이로 빼고 Unity용 16비트 little-endian RAW 출력. 남→북 행, 서→동 열. 위치/높이 범위는 manifest 참조.

이 방법은 건물 영향을 완전히 제거하지 않으며 실제 지형도 낮출 수 있다. **미술용 합성 보정**으로 취급한다. 건물 높이·계단·경사 허용 여부의 측정값으로 쓰지 않는다. 높이 배율을 임의로 과장하지 않았다.

결과 파일:

- `maps/inha_relief_research/source_dsm_projected.tif`: 원본 DSM의 투영/보간본.
- `maps/inha_relief_research/approximate_relief.tif`: 근사 지형, QGIS 열람 가능.
- `maps/inha_relief_research/terrain_south_first_u16.raw`: Unity Terrain 입력용.
- `maps/inha_relief_research/manifest.json`: 좌표계·수직 기준·변환·배치·오차·제한.
- `maps/inha_relief_research/elevation_comparison.png`: 같은 색 범위의 원본/근사 비교 및 건물 윤곽.
- `maps/inha_relief_research/osm_features.geojson`: OSM node/way 후보. relation 조립/위상 검증 전이므로 런타임 그래프가 아니다.
- `maps/inha_relief_research/landmark_candidates.json`: OSM ID·원본 태그·geometry 보존. Stop 검증 완료 데이터가 아니다.

## 랜드마크 등록 계획

| 요구 명칭 | 대응/근거 | 현재 결손 |
|---|---|---|
| 정문 | OSM node 4741793208 | 안전한 정차 위치/보행 연결 |
| 인하대역 | 사용자 확정, OSM 역/출구 후보 | 출구 번호·엘리베이터 동선·차량 Stop |
| 후문 | OSM node 4741793209 | 후문쪽문과 구분, Stop 검증 |
| 5호관 | OSM way 218017288 / relation 3149246 | 중정 보존, 건물 파트/출입구 정리 |
| 2호관 | OSM way 218036946 | 인하공전 2호관과 campus_id로 구분 |
| 하이테크 | OSM way 218081857, 인하대 연구실 주소 | 대표 실루엣·출입구 |
| 60주년 | OSM way 218189298 | 저층부/고층부 형태·출입구 |
| 비룡플라자 | OSM 입구 node 9767122969 / 9767122970 | 광장 앞 polygon과 차량/보행 구간은 사람 확인 필요 |
| 기숙사 1 | 웅비재, OSM way 217948855 | 도로 횡단·차량 접근 검증 |
| 기숙사 2 | 비룡재, OSM way 218397211 | 인하공전 인접 연결 검증 |
| 기숙사 3 | 게스트하우스, 대학 모집 자료 | 정확한 건물 geometry와 출입구 미확정 |

역명에 출구가 들어간 버스정류장을 지하철 출입구로 오인하지 않도록 태그를 검사한다. 입구 node도 차량 Stop과 동일하지 않다. 제3생활관은 근거 없이 주변 건물에 이름을 붙이지 않는다.

인하대 대표 시설 추가 대조 대상: 본관, 4/6/9호관, 학생회관, 정석학술정보관, 서호관, 학생동아리관, 로스쿨관, 학군단, 평생교육원, 김현태인하드림센터, 체육관, 인하드림센터, 운동장, C호관 등. 개별 Stop 또는 접근 가능한 인근 Stop 연결 여부를 후속 커버리지 표에서 검증한다.

## 인하공전 범위

인하대 경계만 남기는 기존 필터를 바꿔야 한다. 인하공전 OSM 경계 way 568420965는 원본에 있지만 기존 생성 결과는 인하대 안으로 제한된다.

- 배경: 인하공전 건물 매스·도로·녹지를 포함해 전경을 연결.
- 상세: 제2생활관 등 필수 거점으로 이어지는 구간, 눈에 띄는 건물/실습장부터 표현.
- 서비스: 차량 통행·보행 연결이 확인된 edge/Stop만 개방. 배경 구현 완료를 운송 서비스 가능으로 처리하지 않음.
- 인하공전 다온관은 인하대 제4생활관이 아니며 필수 기숙사 목록에 추가하지 않음.
- 확장 OSM 조사 bbox와 Terrain 정사각 범위는 서로 다르다. 최종 서비스 polygon과 외곽 배경 범위는 후속 단계에서 후보 시설 coverage를 보고 확정한다.

## 에셋 배치 계획

| 에셋 | 로컬 확인 | 적용 방향 |
|---|---|---|
| Flat Kit | Terrain.shader, URP 렌더 기능, 매뉴얼 존재 | TerrainLayers + Terrain 전용 shader. 건물은 별도 Surface 재질. 원본 재질 복사 후 프로젝트용 변형 |
| Stylized Water 3 | package 3.3.1, Unity 6000.0/URP17 계열 메타데이터 | 인경호·수로만 사용. 다른 두 에셋의 물 shader와 중복 배치하지 않음 |
| Idyllic Fantasy Nature | Green Broadleaf/Willow, Bush, Blossom 등 prefab 존재 | 녹색 활엽수·버드나무·관목 중심. 판타지 색상·절벽·대형 바위는 기본 제외 |

캠퍼스 실루엣을 식생으로 가리지 않고, 수목 밀도/그림자/물 반사를 PC/모바일 품질로 분리한다. 데모의 PlayerMovement·CameraMovement·물리 상호작용은 캠퍼스 시뮬레이션에 그대로 넣지 않는다. 기존 612그루를 일괄 고비용 prefab으로 치환하기 전에 대표 구역을 먼저 측정한다.

에셋의 Unity Editor 실제 렌더 호환성은 아직 미검증. Stylized Water 제작사 문서는 원본 패키지의 공개 저장소 배포를 허용하지 않는다고 명시하므로 공개 릴리스의 에셋 배포 방식은 별도 확인한다. 이번 작업은 업로드하지 않았다.

## Terrain 편집 대응 설계

`원본 고도 → 프로젝트용 근사 높이 → Terrain 수동 편집 → Bake → 도로/건물/Stop 갱신 → 검증 → 지도 버전 발행`

- Terrain 높이 수정과 데이터 원본을 분리하고 재생성 시 사용자 조형을 무단 덮어쓰지 않는다.
- 도로: 중심선 표본과 교차로를 함께 갱신, 완만한 종단/횡단 형태로 만들고 주변 Terrain 정리.
- 건물: 수평 유지, foundation offset과 주변 부지 평탄화. 건물 전체를 지면 법선 방향으로 기울이지 않는다.
- 수면: 고정 수위. Terrain 샘플에 맞춰 물 표면을 울퉁불퉁하게 만들지 않는다.
- 수목: 도로/건물/물/Stop 제외 마스크 적용, 지표면에 재배치.
- Stop/출입구: 보도 접속·단차·접근성 재검증, 오류면 사용 불가 표시.
- 경로: 베이크한 높이·경사·map hash를 Python과 Unity가 공유. 현재는 엔진 미구현이므로 이 연동을 완료했다고 주장하지 않는다.
- 기존 근사 평면 투영과 새 AEQD 차이를 먼저 검증하고 한 좌표계로 통일한다. 기존 geometry에 RAW만 덮어씌우지 않는다.
- 실패 시 이전 지도 버전을 유지. 안전 조건을 완화해서 갱신 성공으로 표시하지 않는다.

## 실행 순서와 완료 기준

1. **자료 준비(이번 완료):** 원본 캐시·출처·해시·고도 가공·랜드마크 후보·계획.
2. **작업 씬과 좌표 통합:** 원본 프리팹 보존, 새 씬/지도 데이터에서 좌표 일치 3점 확인.
3. **Terrain 시범 적용:** 근사 높이맵, Flat Kit, 본관~인경호 대표 구역. 수동 지형 수정 후 재베이크 확인.
4. **도로/건물 고정:** 교차로·출입구·부지·수면 높이 정리. 떠 있거나 묻히는 메시 검사.
5. **범위 확장:** 11개 필수 랜드마크, 인하공전 배경과 연결부, 대표 시설 커버리지.
6. **미술 마무리:** 주요 건물 실루엣·수목·수면·조명. 소품보다 전체 배치 우선.
7. **검증:** 레이캐스트·도로 연결·Stop 접근·Terrain 재편집·재로드·콘솔·PC/모바일 성능.

초기 성능 목표는 기존 설계의 30FPS이며 아직 측정하지 않았다. 정확한 일정/작업 시간은 새 에셋의 실제 Editor 검증 후 산정한다.

## 재현

Windows PowerShell, 저장소 루트에서:

```powershell
python -m venv Temp/map-research-venv
Temp/map-research-venv/Scripts/python.exe -m pip install -r AgentScripts/map-research-requirements.txt
Temp/map-research-venv/Scripts/python.exe AgentScripts/prepare_elevation_research.py
```

검증 환경은 Python 3.14 Windows다. 이 도구 환경은 향후 Python 3.11+ 서버 환경과 별개다. 원본 다운로드 URL은 downloads.json에 있으며, 전체 타일과 expanded_campus.osm이 필요하다.

## 근거 링크

- [인하대 연구실 캠퍼스 지도](https://cvl.inha.ac.kr/contact.html), 지도 이미지 경로 `media/Campus_map.png`: 배치 참고용, 측량 좌표 원본 아님.
- [대학 캠퍼스 안내 PDF](https://ipsi1.uwayapply.com/foreign/inha/etc_file/etc_fileN18.pdf): 대표 시설/생활관 이름 대조. 최신 현황 전체를 보장하지 않음.
- [2026 인하대 모집요강](https://inha.uway.com/file/2026_inha_js.pdf): 제1 웅비재, 제2 비룡재, 제3 게스트하우스 명칭 근거.
- [인하공전 캠퍼스 지도](https://www.inhatc.ac.kr/kr/103/subview.do): 시설 목록 원본 로컬 확보.
- [인하대 디지털 역사관](https://heritage.inha.ac.kr/digital-history): 본관/전경 시각 참고 후보. 이번 조사에서 최신 전경 사진 세트 확보까지 완료하지 않음.
- [Copernicus 공개 데이터](https://registry.opendata.aws/copernicus-dem/), [타일 형식](https://copernicus-dem-30m.s3.amazonaws.com/readme.html), [라이선스](https://docs.sentinel-hub.com/api/latest/static/files/data/dem/resources/license/License-COPDEM-30.pdf).
- [Rasterio 설치/환경](https://rasterio.readthedocs.io/en/stable/installation.html), [라이선스](https://github.com/rasterio/rasterio/blob/main/LICENSE.txt), [QGIS](https://www.qgis.org/download/), [OpenTopography API](https://opentopography.org/developers).
- [Flat Kit Terrain](https://flatkit.dustyroom.com/terrain/), [Stylized Water 문서](https://staggart.xyz/unity/stylized-water-3/sw3-docs/?section=stylized-water-3).

## 남은 데이터 결손

제3생활관 정확한 geometry, 역 출구/엘리베이터, 비룡플라자 앞 경계, 실제 차량 진입 조건, 보도 단차·경사, 최신 주요 건물 전경 자료가 미완료다. 미술용 지형 초안 작업은 가능하지만 실제 지도 운송 검증 완료로 판단할 수는 없다.
