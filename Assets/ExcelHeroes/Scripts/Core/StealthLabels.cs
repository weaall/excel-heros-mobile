using System;
using ExcelHeroes.UI;

namespace ExcelHeroes.Core
{
    /// <summary>
    /// 위장 모드 어휘집 — ported from the web build's stealthLabels.js.
    ///
    /// The disguise is not a skin. Repainting the chrome green and leaving "보스 도전" on a button
    /// gives the whole thing away in one glance, and one line is enough: the desktop build shipped
    /// with the battle screen disguised and the sheet tabs still saying 메인_전투.
    ///
    /// So the rule the web build settled on is kept here: every replacement has to be something a
    /// real spreadsheet would say, and it has to be about the same length, or the layout shifts and
    /// draws the eye that the disguise is meant to slide past.
    /// </summary>
    public static class StealthLabels
    {
        public static string Tab(AppRoot.Sheet sheet) => sheet switch
        {
            AppRoot.Sheet.Home => "Sheet1",
            AppRoot.Sheet.Roster => "직원_목록",
            AppRoot.Sheet.Gacha => "외부_데이터",
            AppRoot.Sheet.Quests => "일일_점검",
            AppRoot.Sheet.Story => "수신_메일",
            AppRoot.Sheet.Album => "사원_사진",
            AppRoot.Sheet.Codex => "오류_로그",
            AppRoot.Sheet.Chart => "요약_차트",
            _ => "Sheet1",
        };

        /// <summary>
        /// One battle-log line. Keyed by row rather than by time so that a log redrawn twice a
        /// second does not visibly churn — a wall of text that rewrites itself is its own tell.
        /// </summary>
        public static string LogLine(int row)
        {
            var r = Math.Abs(row);
            return (r % 8) switch
            {
                0 => $"Sheet2!A{r % 200 + 1}:M{r % 200 + 8} 재계산 완료",
                1 => $"SUMIFS 배열 {r * 7 % 900 + 100}행 평가",
                2 => $"외부 연결 새로 고침 — 레코드 {r * 13 % 4000 + 500}건",
                3 => $"피벗 캐시 갱신 (필드 {r % 9 + 3}개)",
                4 => $"조건부 서식 규칙 {r % 12 + 1}개 적용",
                5 => "이름 정의 범위 검사 — 순환 참조 없음",
                6 => $"VLOOKUP 조회 {r * 3 % 700 + 60}건 일치",
                _ => $"자동 필터 재적용 (표시 {r * 11 % 300 + 20}행)",
            };
        }

        public static string StageMode(bool challenging) => challenging ? "재계산 중" : "자동 계산 대기";

        public static string StageKills(int done, int total, bool challenging) =>
            challenging ? $"{done} / {total}행 처리" : $"유휴 — 누적 {done}행 처리";

        public static string StageHint(int stage) =>
            $"반복 계산 사용 · 최대 반복 {100 + stage % 40}회 · 허용 오차 0.001";

        public static string Challenge(bool challenging) => challenging ? "재계산 중단" : "선택 영역 재계산";

        public static string Bench(int heroes) => $"참조되지 않는 범위 {heroes}개";

        /// <summary>The status bar, carrying the same information in recalculation vocabulary.</summary>
        public static string Status(string kind, int done, int total, int seconds)
        {
            const string range = "Sheet2!A1:M8";
            return kind switch
            {
                "travel" => $"{range} 참조 갱신 중…",
                "boss" => $"{range} 대용량 수식 재계산 — 제한 {seconds}초",
                "challenge" => $"{range} 재계산 중 — 항목 {done} / {total}",
                "waiting" => $"{range} 자동 계산 대기 — 조건 충족 시 다음 범위",
                "paused" => "연결 대기 중…",
                _ => $"{range} 자동 계산 중 — 반복 참조 해결",
            };
        }

        /// <summary>The onboarding panel becomes a document inspection report of the same length.</summary>
        public static readonly (string Title, string Body)[] Inspection =
        {
            ("호환성 검사", "이전 버전에서 지원되지 않는 기능 없음"),
            ("접근성 검사", "대체 텍스트 누락 0건"),
            ("개인 정보 검사", "문서 속성에 개인 정보 없음"),
            ("수식 오류 검사", "순환 참조 없음 · #REF! 0건"),
            ("이름 정의 검사", "사용되지 않는 이름 2건"),
            ("연결 검사", "외부 통합 문서 연결 1건 (최신)"),
            ("시트 보호 검사", "잠금 해제된 셀 범위 확인 완료"),
        };

        static readonly (string Game, string Sheet)[] Words =
        {
            ("스테이지", "범위"), ("보스", "대용량 수식"), ("몬스터", "오류"), ("처치", "처리"),
            ("파티", "선택 영역"), ("영웅", "사원"), ("보석", "토큰"), ("골드", "포인트"),
            ("사냥", "계산"), ("전투", "계산"), ("도전", "검증"), ("레벨", "단계"),
        };

        /// <summary>
        /// Last resort for text assembled at runtime — logs, tooltips — where the vocabulary leaks
        /// a word at a time rather than a label at a time.
        /// </summary>
        public static string Disguise(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            foreach (var (game, sheet) in Words) text = text.Replace(game, sheet);
            return text;
        }
    }
}
