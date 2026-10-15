# Design system

## Creative direction

Three directions were explored and compared on brand fit, distinctiveness, accessibility and
implementation practicality.

### A — Quiet Security
Restrained neutrals, elegant typography, deliberate whitespace, a single calm accent. Calm and
premium, but at risk of reading as "another minimal dashboard".

### B — Technical Precision
Crisp layouts, strong hierarchy, dense structure, precise alignment. Reads well for data, but
cold and austere for a product people use daily.

### C — Warm Minimalism
Approachable surfaces, softer neutrals, refined type, human-centred interaction. Friendly, but it
undercuts the gravity a security product needs.

### Chosen: Quiet Security disciplined by Technical Precision

A cool near-black graphite base with generous type and exactly one warm accent. Two deliberate
constraints:

- **Never pure black.** `#000000` looks cheap on OLED and harsh in a dark room. The base is
  `#0D1013`.
- **Exactly one accent.** Everything else is neutral. Hierarchy comes from type and spacing, not
  from colour.

### Why brass

Every major competitor uses blue or indigo as the "security" colour. Brass carries the association
of a vault door without resorting to the shield and padlock imagery the brief rules out, and
against a near-black base it gives a warm focal point that stays calm rather than glowing. On the
light theme the same hue is darkened so it still passes contrast.

---

## Palette

Contrast ratios were checked against WCAG 2.1 AA (4.5:1 body text, 3:1 large text and borders).
Status is never communicated by colour alone: every state also carries a label, an icon, or a
shape change.

### Dark (default)

| Token | Hex | Use |
|---|---|---|
| `Background` | `#0D1013` | Window background |
| `Sidebar` | `#101419` | Navigation rail, header bars |
| `Surface` | `#151A21` | Cards, list rows |
| `Elevated` | `#1C232D` | Menus, dialogs, hover |
| `Input` | `#10151B` | Text fields |
| `Selection` | `#212B38` | Selected row / nav item |
| `Hover` | `#1A212A` | Row hover |
| `Border` | `#232B36` | Default hairline |
| `BorderStrong` | `#323D4C` | Emphasised border |
| `TextPrimary` | `#E9EDF4` | Body and headings (~15:1) |
| `TextSecondary` | `#9BA7B8` | Supporting text (~8:1) |
| `TextMuted` | `#707D8F` | Metadata (~4.7:1) |
| `Accent` | `#F2B33D` | Brass — primary actions, focus |
| `AccentHover` | `#FFC55C` | |
| `AccentPressed` | `#D99A22` | |
| `AccentSoft` | `#2A2314` | Tinted accent surface |
| `TextOnAccent` | `#1A1200` | Text on brass (~9:1) |
| `Success` | `#45D483` | |
| `Warning` | `#F5A524` | |
| `Danger` | `#F2555A` | |
| `Info` | `#56A8F5` | |

### Light

| Token | Hex |
|---|---|
| `Background` | `#F5F6F8` |
| `Sidebar` / `Surface` / `Elevated` | `#FFFFFF` |
| `Input` | `#FFFFFF` |
| `Selection` | `#F0E3C4` |
| `Hover` | `#F1F3F6` |
| `Border` | `#E2E6EC` |
| `BorderStrong` | `#C9D1DC` |
| `TextPrimary` | `#10151C` (~17:1) |
| `TextSecondary` | `#4A5566` (~7.5:1) |
| `TextMuted` | `#6B7687` (~4.8:1) |
| `Accent` | `#A9660A` (~5.3:1 on white) |
| `AccentSoft` | `#F6EBD5` |
| `TextOnAccent` | `#FFFFFF` |

The dark accent is darkened in the light theme because `#F2B33D` on white fails contrast.

---

## Typography

**Segoe UI** and **Consolas**, both shipped with Windows.

Inter, Geist and IBM Plex Sans were all evaluated and rejected on one practical ground: this
machine has no package feed, and downloading a webfont at 2–50 KB/s is not viable. Segoe UI is
licensed for use with the operating system, renders natively with sub-pixel hinting and ClearType,
and is present on every supported machine. Consolas is the Windows system monospace.

| Role | Face | Size |
|---|---|---|
| Display | Segoe UI Light | 26 |
| Title | Segoe UI Light | 21 |
| Section heading | Segoe UI Semibold | 15.5 |
| Item name | Segoe UI Semibold | 14.5 |
| Body | Segoe UI | 13.5 |
| Form label | Segoe UI Semibold, uppercase | 12 |
| Metadata | Segoe UI | 11.5 |
| Secret / technical | Consolas | 13.5 |

Monospace is used only where it carries meaning: secrets, file paths, and generated passwords.

---

## Metrics

Everything is a multiple of 4.

- **Spacing**: 4, 8, 12, 16, 20, 24, 32, 40, 48
- **Radii**: 6 small · 8 control · 10 card · 14 panel · 999 pill
- **Sidebar**: 244 px · **List**: 348 px · **Narrow breakpoint**: 1080 px
- **List row**: 56 px

Below the breakpoint the detail pane replaces the list rather than squeezing three columns into a
small window, because three columns that narrow are all unusable.

---

## Iconography

All icons are **original vector geometry defined in code** on a 24×24 grid, 1.8-unit stroke, round
caps and joins, so every glyph shares the same optical weight. Drawing them rather than shipping an
icon font keeps the application dependency-free, avoids any third-party licensing question, and
lets icons recolour with the theme at runtime.

Only lines and cubic segments are used — no elliptical arc commands — so the geometry parses
identically on every Windows build.

---

## Motion and interaction

Motion is purposeful and restrained:

- Hover and press transitions are instant state changes, not animations. A password manager should
  feel immediate, not cinematic.
- Toast notifications fade in and auto-dismiss after 3.5 s, or 6 s for errors, because errors need
  reading time.
- Locking and unlocking are never animated in a way that delays the security action.
- The auto-lock countdown updates once per second in the title bar, so the user is never surprised
  by a lock.

Animations never obstruct a security-critical message or gate an action behind a transition.

---

## Components

Built in `src/Ui/Design/Factory.cs` and used by every screen:

`Button` (Primary / Secondary / Ghost / Danger) · `TextInput` · `TextInputWithPlaceholder` ·
`SecretInput` (masked with reveal) · `Check` · `Toggle` · `Icon` · `IconFilled` · `Card` ·
`Divider` · `Pill` · `Label` · `Text` / `Body` · `Rect`

Every interactive control has a visible keyboard-focus state drawn by its own template, and
carries an accessible name via `AutomationProperties`.
