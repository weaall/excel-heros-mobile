# Blue Archive — what the target actually does

Researched 2026-09-19 from the Blue Archive Wiki (bluearchive.wiki/wiki/Combat_basics) rather than
from memory, because the last round of guesses produced a battle screen that looked like a debug
view. Only the parts that change what this game should do are written down.

## Combat is semi-auto, and the one thing the player does is spend Cost

> "Cost points are required to activate EX skills of characters, which is players' primary way of
> directly affecting the battle."

This is the whole interaction loop, and it is the thing this build is missing. Right now EX skills
sit on independent cooldowns, so there is no decision: you press each button when it lights up.
With a shared Cost pool there is a real question every few seconds — spend now on the cheap skill,
or hold for the expensive one.

- Cost accumulates continuously during the fight and is shared by the whole team.
- **Cost Recovery is per-student and additive**: "a full team of 6 characters will have a much
  faster cost recovery than a team of fewer students". So a bigger party charges faster — which
  gives the roster a second reason to exist beyond raw stats.
- Some students boost recovery, others discount their own skill's cost. That maps cleanly onto the
  traits this game already has.

## Speed and auto are first-class controls

> "This combat is realtime, speedup options up to 3x and Auto skill use become available shortly
> after starting the game."

Both live in the HUD, not in a settings menu. Auto exists but the wiki notes it "leads to
worse-than-expected team performance" — which is the right shape: auto is the convenience option,
manual is the skilled one. This build has an auto toggle and no speed control at all.

## Team shape

A typical team is one tank, one healer, and the rest attackers/supports. This game's four roles
(탱커 / 근접 / 원거리 / 힐러) already match, and the party of five sits in the same range.

## Mission objectives are a three-star checklist

Each mission carries three extra objectives shown as gold stars, cleared independently across
multiple runs, and clearing all three unlocks Sweep (skip the fight, take the rewards). This build
has none of that; it is a good fit for the daily-quest sheet rather than a new system.

## What this means here, in order

1. **Cost pool + EX cards showing their cost.** The missing interaction. Disguise: the pool reads
   as a recalculation budget, the cards as pending operations.
2. **Speed ×1 / ×2 / ×3 in the HUD**, next to auto.
3. **Three objectives per stage**, feeding the existing 일일_업무 sheet, with sweep once cleared.

## Not adopted

Attack/armour typing (six attack types against six armours) and terrain affinity. Both are real
depth, but they are a second and third stat axis on top of grade, role, star and division, and this
game's fights are ten seconds long — the player would never see the difference. Revisit only if
fights get long enough to plan.

## What its screens actually look like (2026-09-20, from screenshots)

Read off the lobby, the 부대 편성 sheet, the 학생 sheet, the 청휘석 shop modal, a ten-pull result
and a combat frame — not from memory. This build now copies the following deliberately, and they
should not be "improved" back into something else without a reason:

**Chrome is light.** Every screen puts its bar on white or a pale translucent panel. The only dark
things in the chrome are the player's own plate and the name plates on cards. A navy slab under a
white app was this build's invention.

**Navigation is a row along the bottom, and every screen is on it.** 카페 스케줄 학생 편성 서클
제조 상점 모집 — eight, all visible, icon over label. There is no overflow menu. One loud call to
action sits in the bottom-right corner (업무 there, 모집 here).

**The top-left corner is either the player or the way back.** On the lobby it is a dark plate with
the level on a coloured tab. On every screen inside, that plate is replaced by a round dark back
arrow and the screen's name. The settings and home buttons sit at the far right.

**Currencies are white pills**: a coloured icon, a bold dark number, a hairline, a `+`.

**Sheets split into pill tabs rather than scrolling.** 학생 is 기본 정보 / 레벨 업 / 신비 해방; the
formation modal is STRIKER / SPECIAL. The lit tab is white with a blue rule under it, not a blue
fill. Held sideways there is no scroll anywhere, so a sheet that does not fit becomes tabs or pages.

**Buttons** are wide, heavily rounded, with a darker lip along the bottom edge. Confirm is the light
blue; cancel is the pale one beside it, same shape.

**Combat** puts the run's counters in a small dark pill top-right, the skill hand bottom-right, and
gives the rest of the frame to the scene. The floor recedes — that is what makes it read as three
dimensions rather than as figures pinned to a wall, and it is worth more than any amount of sprite
detail. This build gets the same effect from a drawn perspective road plus three discrete depth
rows with contact shadows; the sprites stay 2D.
