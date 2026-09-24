# UI_DESIGN_SYSTEM.md

## 1. 목적

이 문서는 `INHA Campus Autonomous Shuttle` 프로젝트의 PC 관제 클라이언트와 모바일 승객 클라이언트가 하나의 제품처럼 보이도록 만드는 공통 시각 언어와 UI 구현 규칙을 정의한다.

프로젝트의 기능/권한/데이터 소유권은 `AGENTS.md`를 우선하며, 본 문서는 **시각 표현과 Unity UI 구현 원칙**을 담당한다.

---

## 2. 제품 인상

목표 인상은 다음과 같다.

- 캠퍼스 기반의 스마트 모빌리티 서비스
- 친근하지만 장난스럽지 않은 대학 서비스
- 깨끗하고 현대적인 관제 시스템
- 실시간 상태가 즉시 읽히는 데이터 중심 UI
- PC와 모바일이 동일한 서비스 패밀리로 느껴지는 통일감

핵심 키워드:

`Campus / Mobility / Realtime / Clean / Friendly / Safe / Data-driven`

---

## 3. 공통 브랜드 규칙

### 3.1 Primary Color

| Token | Value | 용도 |
|---|---:|---|
| `Color.Primary.500` | `#1677FF` | 주요 버튼, 활성 상태, 경로, 포인트 |
| `Color.Primary.600` | `#0B63E5` | 눌림/강조 |
| `Color.Primary.100` | `#EAF3FF` | 선택 배경, 약한 강조 |
| `Color.Navy.900` | `#10244A` | 제목/강한 텍스트 |
| `Color.Navy.700` | `#27446F` | 보조 제목 |

### 3.2 Neutral

| Token | Value |
|---|---:|
| `Color.Surface` | `#FFFFFF` |
| `Color.Background` | `#F4F7FB` |
| `Color.Border` | `#DDE5F0` |
| `Color.Text.Primary` | `#16233B` |
| `Color.Text.Secondary` | `#6C7A90` |
| `Color.Disabled` | `#C7D1DE` |

### 3.3 Semantic

| Token | Value | 의미 |
|---|---:|---|
| `Color.Success` | `#18B86A` | 정상/운행 가능 |
| `Color.Warning` | `#F4A41D` | 주의/저배터리 |
| `Color.Danger` | `#F04444` | 충돌 위험/실패 |
| `Color.Info` | `#3B82F6` | 시스템 정보 |

색만으로 상태를 구분하지 않는다. 반드시 텍스트, 아이콘, 배지 중 하나 이상을 같이 제공한다.

---

## 4. Typography

권장 폰트: **Pretendard**

| 역할 | 크기 | Weight |
|---|---:|---|
| Hero Title | 30~36 | Bold |
| Screen Title | 24~28 | Bold |
| Section Title | 18~22 | SemiBold |
| Body | 15~17 | Regular |
| Label | 13~15 | Medium |
| Caption | 11~13 | Regular |
| Button | 15~17 | SemiBold |
| Data Number | 22~32 | Bold |

숫자 중심의 PC 관제 화면은 단위를 숫자와 분리해 가독성을 높인다.

예:
- `300 명`
- `3.5 분`
- `78 %`
- `12 / 50`

---

## 5. Shape / Radius / Shadow

| Token | 값 |
|---|---:|
| `Radius.Small` | 10 |
| `Radius.Medium` | 16 |
| `Radius.Large` | 24 |
| `Radius.Pill` | 999 |
| `Shadow.Card` | Y 4 / Blur 16 / Alpha 0.10 |
| `Shadow.Float` | Y 8 / Blur 28 / Alpha 0.14 |

PC는 작은 카드가 많으므로 10~16px 중심, 모바일은 16~28px 중심으로 사용한다.

---

## 6. Spacing Grid

기본 간격 단위: **4px**

권장 스케일:

`4 / 8 / 12 / 16 / 20 / 24 / 32 / 40 / 48`

원칙:
- 카드 내부 패딩: 16~24
- 모바일 주요 섹션 간격: 24~32
- PC 대시보드 카드 간격: 12~16
- 리스트 행 높이: 44~56

---

## 7. 공통 컴포넌트

### Button.Primary

- 높이: 52~60
- 배경: `Primary.500`
- Radius: 16~20
- 텍스트: White / SemiBold
- Pressed: scale 0.98, 배경 `Primary.600`
- Disabled: `Disabled`

### Button.Secondary

- 흰 배경
- 1px Border
- Primary 텍스트
- 동일 Radius

### StatusBadge

형식:

`[Icon] Label`

예:
- `● 주행 중`
- `● 대기 중`
- `● 배차 완료`

### DataCard

구조:

- Label
- Value
- Optional delta
- Optional icon

PC 상단 KPI 카드와 모바일 요약 카드에 공통 사용한다.

### SegmentedControl

PC 차량 탭, 센서 뷰 탭, 모바일 이동 유형 선택에 공통 사용한다.

### BottomSheet

모바일 전용.
- 상단 Radius 24~28
- 최소 높이 20%
- 확장 높이 50~70%
- Drag handle 선택
- 핵심 CTA는 SafeArea 위에 고정

---

## 8. 아이콘 스타일

- 선 굵기: 2px 내외
- 둥근 모서리
- 같은 계열의 outline icon 사용
- PC 관제용 기능 아이콘은 정보 밀도가 높아도 되지만 24px 이하에서도 식별 가능해야 한다.

권장 범주:
- 차량
- 보행자
- 요청
- 배터리
- ETA
- 경고
- 위치
- 경로
- 센서
- 설정
- 로그

---

## 9. 지도/3D 표현 규칙

PC와 모바일 모두 같은 `CampusWorld`를 사용하되 시각 정보량을 다르게 한다.

### PC

표시 가능:
- 모든 차량
- 보행자
- 요청
- 전역 경로
- 지역 경로
- heatmap
- 센서 FOV
- LiDAR/Radar debug
- zone 상태
- Ground Truth 디버그

### Mobile

표시:
- 내 차량
- 내 출발/목적지
- 내 경로
- 주요 랜드마크
- 단순화된 Stop
- 승객에게 필요한 상태 메시지

표시 금지:
- 다른 승객 요청
- 전체 heatmap
- RRT 샘플
- LiDAR/Radar hit
- Ground Truth debug

---

## 10. 모션

기본 원칙은 짧고 기능적이어야 한다.

| 상황 | 권장 |
|---|---|
| 카드 등장 | 180~220ms fade + slide |
| 상태 변경 | 150~200ms |
| Bottom Sheet | 220~300ms ease-out |
| 지도 마커 이동 | snapshot interpolation |
| 차량 선택 강조 | 150ms scale/fill |
| 경고 | 1회 pulse, 반복 금지 |

실시간 정보 화면에서 장식 애니메이션은 최소화한다.

---

## 11. 접근성

- 색상만으로 상태를 표현하지 않는다.
- 터치 타깃 최소 44x44 pt 상당.
- 모바일 본문은 14pt 이하를 지양.
- 접근성 승객 모드에서는 아이콘+텍스트 병행.
- 위험/중요 안내는 배너, 텍스트, 아이콘을 함께 사용.
- 차량 상태 메시지는 기술 용어 대신 승객 관점 문장으로 변환한다.

예:
- `CROWD_AVOIDANCE` → `혼잡 구간을 피해 경로를 변경했어요`
- `YIELDING` + 보행자 원인 → `보행자 통행을 기다리고 있어요`

---

## 12. Unity 구현 원칙

기본 UI 프레임워크: **UGUI**

권장:
- `CanvasScaler: Scale With Screen Size`
- PC / Mobile Canvas 분리
- 공통 Prefab 라이브러리 사용
- `TextMeshPro`
- Sprite Atlas
- LayoutGroup 남용 금지
- 고빈도 갱신 값은 매 프레임 전체 레이아웃 재계산을 피한다.

공통 폴더 예시:

```text
Assets/CampusSim/UI/
├─ Common/
│  ├─ Prefabs/
│  ├─ Icons/
│  ├─ Fonts/
│  ├─ Sprites/
│  └─ Themes/
├─ PC/
└─ Mobile/
```

---

## 13. 네이밍

Prefab:

```text
UI_PC_KpiCard
UI_PC_VehiclePanel
UI_PC_RequestTable
UI_PC_SensorDebugPanel

UI_Mobile_TopBar
UI_Mobile_RequestSheet
UI_Mobile_RideStatus
UI_Mobile_BottomNav
```

C#:

```text
PcOperatorView
PcVehicleDetailView
PcSensorDebugView

MobileHomeView
MobileRequestView
MobileRideView
```

---

## 14. 금지 사항

- 임시 색상을 화면마다 따로 정의
- RGB 값을 스크립트에 하드코딩
- PC와 모바일에서 같은 상태명을 서로 다르게 표현
- 실제 서버 상태가 아닌 클라이언트 추측값 표시
- debug 정보가 모바일 승객 화면에 노출
- 스크롤 가능한 화면에서 중요한 CTA를 찾기 어렵게 배치
- 모든 패널에 과도한 그림자 사용
