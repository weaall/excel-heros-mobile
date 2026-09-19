# Excel Heroes — UI design system

Here is the design system, mapped exactly to your 1080x2400 reference resolution and Unity USS constraints. 

### 1. TOKENS
Place these on the `.root` selector. 

**Colors**
*   `--ex-surface`: `rgb(255, 255, 255)`
*   `--ex-surface-raised`: `rgb(243, 243, 243)`
*   `--ex-ink`: `rgb(34, 34, 34)`
*   `--ex-ink-muted`: `rgb(107, 107, 107)`
*   `--ex-line`: `rgb(212, 212, 212)`
*   `--ex-accent`: `rgb(150, 112, 8)` *(Game primary)*
*   `--ex-accent-ink`: `rgb(253, 246, 227)`
*   `--ex-danger`: `rgb(196, 43, 28)`
*   `--ex-chrome-base`: `rgb(33, 115, 70)` *(Excel App Bar)*
*   `--ex-chrome-active`: `rgb(24, 92, 55)` *(Excel Active Tab)*

**Spacing Scale** (4px grid)
*   `--ex-space-1`: `16px`
*   `--ex-space-2`: `24px`
*   `--ex-space-3`: `32px`
*   `--ex-space-4`: `48px`
*   `--ex-space-5`: `64px`
*   `--ex-space-6`: `96px`

**Radius Scale**
*   `--ex-rad-sm`: `8px` *(Tags, inner images)*
*   `--ex-rad-md`: `16px` *(Cards, buttons, panels)*
*   `--ex-rad-lg`: `32px` *(Modals, giant reveal cards)*
*   `--ex-rad-full`: `999px` *(Badges, circular icon buttons)*

**Type Scale** (Noto Sans KR)
*   `--ex-text-xs`: `28px` / Normal *(Badges, stats, subtext)*
*   `--ex-text-sm`: `34px` / Normal *(Ribbon text, secondary labels)*
*   `--ex-text-base`: `42px` / Normal *(Body text, button labels)*
*   `--ex-text-lg`: `54px` / Bold *(Card names, section headers)*
*   `--ex-text-xl`: `72px` / Bold *(Hero reveal grades, huge stats)*

---

### 2. BUTTONS
This replaces `.btn`, `.btn--primary`, `.btn--ghost`, `.rb-btn`, and `.sheet-list-btn`. 

You need exactly three variants: Primary (game action), Secondary (standard interaction), and Ghost (tabs/appbar). All touch targets are exactly 136px tall. Elevation is faked with a thicker bottom border.

**Base Button (`.ex-btn`)**
*   **Height**: `136px`
*   **Padding**: `0 48px` (Left/Right)
*   **Radius**: `var(--ex-rad-md)`
*   **Font**: `var(--ex-text-base)`, Bold
*   **Disabled**: `opacity: 0.4`, no hover/active state changes.
*   **Active (Pressed)**: `border-bottom-width: 2px; margin-top: 6px;` (Consumes the bottom border to simulate physical depression).

**Primary Variant (`.ex-btn--primary`)**
*   **Fill**: `var(--ex-accent)`
*   **Border**: `2px solid rgb(120, 90, 6)` (Top/L/R), `8px solid rgb(120, 90, 6)` (Bottom)
*   **Text**: `var(--ex-accent-ink)`

**Secondary Variant (`.ex-btn--secondary`)**
*   **Fill**: `var(--ex-surface)`
*   **Border**: `2px solid var(--ex-line)` (Top/L/R), `8px solid rgb(170, 170, 170)` (Bottom)
*   **Text**: `var(--ex-ink)`

**Ghost Variant (`.ex-btn--ghost`)**
*   **Fill**: `rgba(0, 0, 0, 0)`
*   **Border**: `0px`
*   **Text**: `var(--ex-ink-muted)`

---

### 3. SURFACES
Replaces `.panel`, `.card`, `.slot`, `.reveal__card`, `.section-title`, and `.sheet-head`.

**Card / Panel (`.ex-card`)**
*   **Fill**: `var(--ex-surface)`
*   **Border**: `2px solid var(--ex-line)` (Top/L/R), `8px solid var(--ex-line)` (Bottom)
*   **Radius**: `var(--ex-rad-md)`
*   **Overflow**: `hidden`

**Section Header (`.ex-section-head`)**
*   **Font**: `var(--ex-text-lg)`, Bold
*   **Color**: `var(--ex-ink)`
*   **Padding**: `0 0 var(--ex-space-2) 0` (Bottom padding only)
*   **Border**: `border-bottom-width: 2px; border-bottom-color: var(--ex-line);`
*   **Margin**: `margin-bottom: var(--ex-space-4);`

---

### 4. LAYOUT RHYTHM
USS lacks `gap`, so rhythm is enforced strictly through standardized margins and padding.

*   **Page Padding**: Every screen container inside the Excel grid (`.screen-body`) gets exactly `padding: var(--ex-space-4);` (48px) on all four sides. 
*   **Section Gap**: The space below any distinct block (a panel, a roster grid, a header) is exactly `margin-bottom: var(--ex-space-6);` (96px).
*   **Card Internal Gap**: Every card or panel (`.ex-card`) applies exactly `padding: var(--ex-space-3);` (32px) to its internal content body.
*   **Screen Header Rhythm**: A layout header uses `flex-direction: row; align-items: center; height: 128px; margin-bottom: var(--ex-space-5);`.

---

### 5. THE TEN WORST SPECIFIC THINGS

1.  **`.appbar__btn` Touch Target:** It is 84px tall, which translates to ~31dp, failing standard mobile touch guidelines. **Fix:** Increase minimum height and width to 136px and rely on the internal padding to size the visual icon.
2.  **`.sheet-tab__badge` Absolute Positioning:** It is pinned `top: 10px; right: 6px;` which blindly overlaps the dynamic Korean tab text. **Fix:** Move the badge into the standard flex flow of the tab with a left margin of 16px.
3.  **`.card__art` Background Cropping:** `background-position-y: top 0` with `scale-and-crop` arbitrarily decapitates characters if the source aspect ratios vary at all. **Fix:** Enforce a strict asset aspect ratio pipeline, or use `stretch-to-fill` inside a fixed-ratio container.
4.  **`.home__scrim` Hardcoded Height:** The 992px fixed height will detach from the bottom of the art or cover the wrong amount of screen on taller/shorter phones. **Fix:** Anchor it using `top: 50%; bottom: 0; height: auto;` to scale relative to the screen.
5.  **`.detail` Modal Sizing:** `max-height: 88%` with hidden overflow means users physically cannot scroll to see stats if the content exceeds the height. **Fix:** Make `.detail__body` a `ScrollView` with `flex-shrink: 1`.
6.  **Arbitrary Typography:** There are 24 distinct font sizes in this stylesheet. **Fix:** Delete them all and map every text element strictly to the five `--ex-text-*` variables.
7.  **`.tell__ring` Scaling:** Scaling a raster image from `0.3` to `3.4` looks incredibly pixelated in Unity UI Toolkit. **Fix:** Import the ring asset at its maximum intended visual size and scale *down* (e.g., `scale: 1 1` to `scale: 0.1 0.1`).
8.  **`.card--locked .card__name` Contrast:** `rgb(150,150,150)` text on an `rgb(248,248,248)` background fails WCAG contrast requirements. **Fix:** Change the locked text color to `var(--ex-ink-muted)` (`rgb(107,107,107)`).
9.  **`.detail__close` Placement:** Pinned absolute at `top: 32px; right: 32px;` guarantees it will collide with the system status bar or notch on modern Android devices. **Fix:** Place it inside the `.detail__head` flex layout, aligned to the right.
10. **`.btn` Transition Duration:** `0.12s` is too fast for Unity's UI Toolkit layout engine to smoothly interpolate background colors on low-end Androids, causing dropped frames. **Fix:** Increase button hover/active transitions to `0.2s`.
