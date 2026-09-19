# 엑셀 히어로즈 : 모바일 (Unity)

[excel-heros](https://github.com/weaall/excel-heros) 웹 버전의 **세계관·로스터·밸런스·스토리를 그대로 가져와** 만든
유니티 모바일 수집형 RPG입니다. 웹 버전은 건드리지 않습니다 — 이 저장소는 별도입니다.

- 장르: 세로형 모바일 가챠 RPG (블루아카이브 계열)
- 엔진: Unity **6000.0.82f1** (Universal 2D / URP)
- UI: UI Toolkit (UXML + USS), 기준 해상도 1080×1920 세로 고정
- 현재 범위: **수직 슬라이스** — 모집 → 도감 → 편성 → 전투 한 바퀴가 실제로 돕니다

## 열기

1. Unity Hub에서 이 폴더를 엽니다 (6000.0.82f1).
2. 첫 로드 때 `ProjectBootstrap`이 PanelSettings·Main 씬·빌드 설정·세로 고정을 자동으로 만듭니다.
3. `Assets/ExcelHeroes/Scenes/Main.unity` 를 열고 Play.

Game 뷰는 세로 비율(예: 1080×1920)로 맞춰 두세요. 다시 세팅이 필요하면 메뉴 **Excel Heroes ▸ Rebuild Project Setup**.

## 무엇이 들어 있나

| 화면 | 내용 |
| --- | --- |
| 모집 | 1회/10회 뽑기, 공개 확률표, 천장 2종(A 50 · S 120), 카드 한 장씩 공개 → 10연 결과 그리드 |
| 인사 명단 | 55장 전체 도감. 미보유는 실루엣으로 남아 다음 뽑기를 만듭니다 |
| 카드 상세 | 일러스트가 **모션 프레임과 크로스페이드**되어 숨쉬고 눈을 깜빡입니다. 스탯·스킬·특성·인사기록 |
| 편성 | 5인 슬롯, 부문 시너지 계산, 총 전투력, 대기 인원 |
| 출근(전투) | 라인 오토배틀 + **EX 스킬 수동 발동**. 3웨이브, 마지막은 보스 |
| 사내 메신저 | 채팅 로그 형식 에피소드 20화, 첫 열람 시 보석 지급 |

## 구조

```
Assets/ExcelHeroes/
  Resources/Data/*.json     웹 빌드에서 뽑아낸 게임 DB (직접 수정하지 말 것)
  Resources/Art/Cards/*.png 캐릭터 카드 일러 55종 (Gemini 생성)
  Resources/Art/UI/*.png    배경 3종 · 등급 프레임 5종 · 로고
  Scripts/Data/             JSON 미러 정의 + GameData 로더
  Scripts/Core/             PlayerState · SaveService · GachaService · StatMath · BattleSim · Game
  Scripts/UI/               AppRoot + 화면 5종 + HeroDetail + UiKit
  Scripts/Editor/           ProjectBootstrap · ArtImportSettings
  UI/                       AppShell.uxml · App.uss · ExcelHeroesTheme.tss · PanelSettings.asset
tools/                      데이터 익스포터 · Gemini 아트 파이프라인
```

**밸런스의 원본은 웹 저장소입니다.** 수치를 바꾸려면 `excel-heros/src/config/balance.js` 를 고치고
`node tools/export-data.mjs` 를 다시 돌리세요. 이 프로젝트의 JSON은 산출물입니다.

## 도구

```bash
node tools/export-data.mjs                      # 웹 빌드 → Resources/Data/*.json
node tools/gen-art.mjs cards                    # 카드 일러 (없는 것만)
node tools/gen-art.mjs poses --only ceo,ai_lead # 모션 프레임 (숨쉬기/깜빡임/말하기/미소)
node tools/gen-art.mjs frames                   # 등급별 카드 프레임
node tools/gen-art.mjs ui                       # 배경 · 로고
```

`--force` 로 덮어쓰기, `--only a,b` 로 대상 지정, `--limit N` 으로 개수 제한, `--concurrency N` 으로 동시 실행.

### API 키

`tools/.env.local` 에 `GEMINI_API_KEY=...` 형태로 둡니다. 이 파일은 `.gitignore` 에 있으며
**절대 커밋하지 마세요.** 키가 노출됐다면 Google AI Studio에서 로테이트하면 됩니다.

### 아트 디렉션 (고정)

1. **캐릭터가 주인공** — 배경은 납작한 단색 + 흐린 셀 그리드. 썸네일에서 실루엣이 먼저 읽혀야 합니다.
2. **헤일로가 시그니처** — 7개 부문마다 다른 링(경영지원=클립, 기술=회로, 재무=금화, 임원=왕관 …).
   등급은 배경이 아니라 **헤일로의 크기와 빛**으로 표현합니다.
3. **디자인은 하나** — 움직임은 같은 포즈의 변형 프레임으로만 만듭니다. 리디자인 금지(크로스페이드가 깨집니다).

프롬프트는 `tools/art-prompts.mjs` 한 곳에 모여 있습니다.

## 아직 없는 것 (다음 마일스톤)

- 서버 없음 — 저장은 `persistentDataPath` 로컬 JSON 한 개
- 강화/비품/호감도/출장/일일 업무/업적 — 웹에는 있으나 이 슬라이스에는 미포함
- 픽업 배너, 상점, 광고, 인앱결제
- 안드로이드 빌드 모듈 미설치 (에디터 플레이만 검증됨)
- 아트는 `Resources/` 에 있어 전량 빌드에 포함됩니다. 로스터가 커지면 Addressables로 옮겨야 합니다
