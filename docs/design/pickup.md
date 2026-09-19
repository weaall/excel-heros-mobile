# pickup — design reference

### 1. Critique

1.  **Respecting the grid too much.** You are trapping the game UI inside simulated cell paddings. The tension works best when the Excel chrome (headers, row numbers) is rigid, but the game content boldly merges cells, bleeds to the edges, and breaks the spreadsheet illusion. Right now, it looks like a web form.
2.  **Timid typography.** Banner titles and pull counts using 32px or 40px fonts are invisible at 1080x2400. *Blue Archive* uses massive, aggressive typography (80px+) to establish hierarchy.
3.  **Floating elements.** Summon buttons and pity counters centered in the middle of their containers lack weight. They need to be hard-anchored to the bottom and corners of their parent flex containers. 
4.  **Lack of rarity signifiers.** Without gradients or particles, flat UI relies heavily on border colors and pill tags to communicate value. Your cards lack distinct colored accents.

### 2. Replacement Layout (1010w x 1500h)

Use this exact DOM structure. Because there is no `z-index` in USS, source order dictates stacking. Background images are applied directly to flex containers.

```text
[SheetContentArea] (Flex Column, W: 1010px, H: 1500px, bg: #F3F5F7)

  ├── [BannerSection] (W: 1010px, H: 800px, justify: flex-end)
  │    * Image: Character splash art as background-image, scale-to-fit
  │    * Padding: 0 0 40px 40px
  │    └── [BannerTitleCard] (W: 600px, H: 200px, bg: #FFFFFF, padding: 30px, flex-col, justify: center)
  │         * Border-left: 12px solid #1288FF
  │         ├── [BannerSubtitle] (Text: "PICK UP", size: 36px, color: #1288FF, bold)
  │         └── [BannerTitle] (Text: "데이터_가져오기", size: 84px, color: #1A2530, bold, margin-top: -10px)
  │
  ├── [PickupSection] (W: 1010px, H: 420px, Flex Row, padding: 20px 40px 0 40px)
  │    * Gap: 40px
  │    ├── [PickupCard_1] (W: 445px, H: 380px, bg: #FFFFFF, flex-col)
  │    │    * Border-bottom: 16px solid #FFD100
  │    │    * Border-radius: 12px
  │    │    ├── [CardImage] (W: 445px, H: 280px, bg-image: character portrait)
  │    │    └── [CardFooter] (W: 445px, H: 84px, padding: 0 24px, row, align: center, justify: space-between)
  │    │         ├── [CharName] (Text: "Aru", size: 48px, color: #1A2530, bold)
  │    │         └── [Rarity] (Text: "★★★", size: 40px, color: #FFD100)
  │    │
  │    └── [PickupCard_2] (W: 445px, H: 380px, bg: #FFFFFF, flex-col)
  │         * Border-bottom: 16px solid #FFD100
  │         * Border-radius: 12px
  │         * (Same internal structure as Card 1)
  │
  └── [ActionSection] (W: 1010px, H: 280px, Flex Row, align: flex-end, padding: 0 40px 60px 40px)
       * Gap: 30px
       ├── [PityCounter] (W: 190px, H: 140px, flex-col, justify: flex-end)
       │    ├── [PityLabel] (Text: "MILEAGE", size: 28px, color: #828C96, bold)
       │    └── [PityValue] (Text: "120/200", size: 56px, color: #1A2530, bold)
       │
       ├── [Btn_1_Pull] (W: 280px, H: 140px, bg: #FFFFFF, justify: center, align: center)
       │    * Border: 4px solid #1A2530
       │    * Border-radius: 12px
       │    * Border-bottom: 12px solid #1A2530
       │    └── [BtnText] (Text: "1 Pull", size: 56px, color: #1A2530, bold)
       │
       └── [Btn_10_Pull] (W: 480px, H: 140px, bg: #1288FF, justify: center, align: center)
            * Border-radius: 12px
            * Border-bottom: 12px solid #0D66C0
            └── [BtnText] (Text: "10 Pull", size: 56px, color: #FFFFFF, bold)
```

### 3. Type Scale & Color Roles

| Element | Size / Weight | Color (Hex) | Role / Notes |
| :--- | :--- | :--- | :--- |
| **Banner Title** | 84px Bold | `#1A2530` (Navy) | Highest hierarchy, extremely tight line-height. |
| **Button / Value**| 56px Bold | Varied | Core actions and pity numbers. |
| **Card Headers** | 48px Bold | `#1A2530` (Navy) | Character names on pickup cards. |
| **Subtitles** | 36px Bold | `#1288FF` (Blue) | Banner classification. |
| **Labels** | 28px Bold | `#828C96` (Grey) | Pity labels, purely functional. |

| Role | Color (Hex) | USS Application |
| :--- | :--- | :--- |
| **Action Primary** | `#1288FF` | Main 10-pull button fill, Banner Title left-border. |
| **Action Shadow** | `#0D66C0` | 10-pull button bottom border. |
| **Navy Ink** | `#1A2530` | 1-pull button border, primary typography. |
| **Rarity 3-Star** | `#FFD100` | Pickup card bottom borders, star text. |
| **Surface** | `#FFFFFF` | Card backgrounds, title box background. |
| **Background** | `#F3F5F7` | Sheet area background (contrasts with white cards). |

### 4. Three Specific Visual Polish Points (USS-friendly)

1.  **Thick Bottom Borders as Pseudo-Shadows/Rarity:** Since you cannot use `box-shadow`, use `border-bottom-width: 12px` (or 16px) on buttons and pickup cards. For buttons, use a darker shade of the fill color (e.g., `#0D66C0` under `#1288FF`). For cards, use the character's rarity color (e.g., `#FFD100`). This perfectly mimics the grounded, tactile BA UI style using only flat borders.
2.  **Skewed Accent Tags:** Use `transform: skewX(-15deg)` on a small absolute-positioned container at the top-left of the Banner Image. Fill it with Magenta (`#FF3366`) and add text (apply `transform: skewX(15deg)` to the text so it reads flat). This single geometric break provides the kinetic anime energy contrasting against the rigid Excel grid.
3.  **Typographic Watermarks:** Inside the `[BannerSection]`, place an absolute-positioned text element behind the `[BannerTitleCard]`. Set the text to something like "RECRUITMENT", size to `240px`, bold, color to `#1A2530`, and `opacity: 0.05`. It fills dead space in the 800px tall header and gives a high-editorial, magazine layout feel without needing complex assets.
