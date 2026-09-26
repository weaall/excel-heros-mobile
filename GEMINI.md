# Excel Heroes (Unity) - Gemini 개발 및 디자인 가이드라인 (GEMINI.md)

이 문서는 **엑셀 히어로즈 (Unity - 가로형 모바일 RPG)** 프로젝트의 개발 방식, 디자인 규칙, 그리고 AI 에이전트(Gemini)의 자율 개발 자격과 도구 활용법을 규정하는 핵심 지침서입니다. 이 프로젝트를 담당하는 모든 Gemini 세션은 이 지침을 최우선적으로 엄격히 준수해야 합니다.

---

## 1. 개발 환경 및 자율성 (Autonomous Development License)

* **자율 실행 및 무조건 수행 권한:** 본 에이전트(Gemini)는 파일 생성, 기존 코드 수정, 컴파일, 빌드, 및 테스트 명령어 실행을 포함한 모든 권한을 사전 부여받았습니다. 사용자의 반복적인 확인을 요청하지 않고 가이드라인에 부합할 때까지 **스스로 계획하고 검증하며 자율적이고 완벽하게** 구현해야 합니다.
* **성공할 때까지 반복:** 특정 UI 수정이나 기능 개발 시, 배치 빌드 또는 셀프 테스트를 실행하여 무결성이 증명될 때까지 반복 수정을 거쳐 완성도 높은 결과물을 도출하십시오.

---

## 2. 디자인 및 레이아웃 절대 준수 사항 (UI/UX Fidelity)

* **`temp_images` 폴더의 레이아웃 완벽 동기화:**
  * `C:\Users\user\excel-heros-unity\temp_images` 폴더 내에 정의된 모든 버튼 스타일, 레이아웃 구조, 모달 프레임, 그리고 디자인 요소들을 완벽하게 준수해야 합니다.
  * *Blue Archive* 스타일의 청량하고 세련된 학원제/오피스 UI 감성(예: 대담한 타이포그래피, 사선 처리된 데코 요소, 그라데이션 및 밝은 키 라이트 대비)을 충실하게 모사하십시오.
* **모바일 기기 최적화 및 요소 잘림 방지 (Zero-Clipping Responsive Design):**
  * 화면 해상도 변화 및 다양한 노치/상태바 영역에 대응하여 UI 요소가 화면 밖으로 밀려나거나 잘리는 현상이 절대 발생해서는 안 됩니다.
  * 리스트나 도감 화면 등 가로/세로 화면 영역을 초과하는 리스트는 `Pages<T>` 페이징 클래스와 `.pager` UI 요소를 활용하여 페이지를 전환하도록 구현하십시오.
  * 고정된 절대 크기(`px` 단위)는 컨테이너 비율에 맞춰 유연하게 조절될 수 있도록 구성하고, 텍스트가 자신의 박스를 벗어나는 오버플로우가 생기지 않도록 `LayoutAudit` 검사를 항상 통과해야 합니다.
  * 상세 보기 및 모달 레이아웃은 `ScrollView`를 도입하여 어떠한 화면비에서도 스크롤을 통해 모든 텍스트와 스탯을 온전히 볼 수 있어야 합니다.

---

## 3. "나노바나나(Gemini AI)" 및 그래픽 파이프라인 적극 활용

* **AI 자산 생성 도구 연동:**
  * 프로젝트 내 `tools/gen-art.mjs`와 `tools/gemini.mjs` 파일은 AI 이미지 생성(Gemini/Imagen 모델 기반) 및 데이터 동기화를 처리하는 핵심 도구입니다.
  * 새로운 캐릭터 카드, 스킨, 아이콘, 템플릿 프레임, 배경(UI)을 보강하거나 재생성해야 할 경우, `node tools/gen-art.mjs` 명령어를 적극 호출하여 자동화된 이미지 에셋 생성을 수행하십시오.
  * **스타일 앵커 (`docs/design/style-anchor.png`) 유지:** 모든 카드 일러스트와 UI 생성 작업은 앵커 이미지를 기반으로 스타일 편차 없이 동일한 작가가 그린 것처럼 통일성을 유지해야 합니다.
* **명령어 종류 및 실행 방법:**
  * **카드 아트 생성:** `node tools/gen-art.mjs cards [--only <id>]`
  * **코스튬 스킨 생성:** `node tools/gen-art.mjs skins [--only <id>]`
  * **UI 요소/배경 생성:** `node tools/gen-art.mjs ui`
  * ** rarity 프레임 생성:** `node tools/gen-art.mjs frames`

---

## 4. UI 시스템 규격 (System Tokens & Polish Rules)

* **컬러 및 타이포그래피 표준 (`docs/design/system.md` 참조):**
  * CSS 및 USS 내에 설정된 표준 변수(`--ex-surface`, `--ex-accent`, `--ex-chrome-base` 등)와 폰트 크기 규칙(xs: 28px, sm: 34px, base: 42px, lg: 54px, xl: 72px)을 반드시 사용하십시오. 스타일시트에 임의의 폰트 크기를 개별 선언하는 것을 전면 금지합니다.
* **입체감 구현 (Flat-Tactile Border):**
  * Unity UI Toolkit에서 지원하지 않는 `box-shadow` 효과를 대신하여, 버튼 및 카드 하단에 두꺼운 입체 경계선(`border-bottom-width: 12px` 또는 `16px`)과 어두운 톤의 섀도 컬러를 적용하여 물리적인 깊이감을 나타내십시오.
* **기하학적 재미 부여 (Geometric Accent):**
  * 메인 배너나 배너 타이틀 박스 등 정형화된 그리드를 탈피하기 위해 사선 형태(`transform: skewX(-15deg)`)의 강조용 태그(예: 마젠타 컬러 등)를 적극 도입하십시오.

---

## 5. 변경 사항 검증 및 테스트 자동화 (Verification Pipeline)

모든 코드 수정 또는 UI 개편 작업 후에는 반드시 아래의 도구와 테스트 명령어를 실행하여 자가 검증을 수행하고, 단 하나의 타임아웃이나 깨짐도 허용하지 않아야 합니다.

1. **레이아웃 클리핑 및 텍스트 넘침 감사:**
   * 빌드를 실행하고 배치 스크린샷 캡처를 돌려 `LayoutAudit` 로그에 누수나 오버플로우가 나타나지 않는지 확인합니다.
2. **프로젝트 셀프 테스트 실행:**
   ```bash
   Unity.exe -batchmode -quit -nographics -projectPath . -executeMethod ExcelHeroes.EditorTools.SelfTest.Run
   ```
3. **전투 시뮬레이션 및 밸런스 벤치마크 실행:**
   * 밸런스 수정이나 전투 메커니즘 변경 시 아래 명령어로 1000회 이상의 모의전투를 시뮬레이션하여 타임아웃 오류나 기형적인 승률이 나오지 않는지 점검합니다.
   ```bash
   Unity.exe -batchmode -quit -nographics -projectPath . -executeMethod ExcelHeroes.EditorTools.SelfTest.Bench
   ```

---

*본 `GEMINI.md` 규정은 작업 진행 중 발견되는 새로운 오류, 화면 레이아웃 이슈, 혹은 시스템 요구사항 변경에 따라 지속적으로 갱신될 수 있습니다.*
