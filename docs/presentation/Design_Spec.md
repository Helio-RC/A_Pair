# SeatFlow Design Spec

## Principles

- **Minimal**: Reduce visual noise. Each screen shows only core information.
- **Flat**: No unnecessary shadows, gradients, or textures. Solid colors with clear boundaries.
- **Intuitive**: Workflows match user expectations. Icons + text for dual semantics.
- **Fluent UI style**: Lightweight, transparent, responsive, following Microsoft Fluent Design System.

---

## Color System

### Accent

FluentTheme default blue:

| Token | Value | Context |
|-------|-------|---------|
| Accent | `#2563EB` | Primary button, selected state, links |
| AccentHover | `#1D4ED8` | Hover |
| AccentPressed | `#1E40AF` | Pressed |

### Neutrals

| Token | Light | Dark | Context |
|-------|-------|------|---------|
| Page background | `#F5F5F5` | `#1E1E1E` | Window background |
| Card/area | `#FFFFFF` | `#2D2D2D` | Content panels |
| Sidebar | `#F0F0F0` | `#252525` | Navigation |
| Divider | `#E0E0E0` | `#3D3D3D` | Separator |
| Primary text | `#1A1A1A` | `#E8E8E8` | Titles, body |
| Secondary text | `#666666` | `#9E9E9E` | Descriptions, labels |
| Disabled text | `#9E9E9E` | `#666666` | Disabled |

### Semantic Colors

| Token | Value | Context |
|-------|-------|---------|
| Success | `#16A34A` | Assigned seats, success |
| Warning | `#F59E0B` | Conflicts, warnings |
| Error | `#DC2626` | Validation failure, exceptions |
| Info | `#2563EB` | Tips |

---

## Typography

| Level | Size | Weight | Context |
|-------|------|--------|---------|
| PageTitle | 20px | Bold | Page heading |
| SectionTitle | 16px | SemiBold | Section heading |
| Body | 14px | Regular | Content, lists |
| Caption | 12px | Regular | Auxiliary info, hints |
| Small | 11px | Regular | Tags, badges |

Font: Inter (Avalonia FluentTheme built-in)

---

## Icons

See [Fluent_Icons.md](Fluent_Icons.md) for the complete icon reference.

Library: `FluentIcons.Avalonia` v2.1.325

---

## Spacing

| Token | Value | Context |
|-------|-------|---------|
| XS | 4px | Tight elements, icon padding |
| SM | 8px | Related element gap |
| MD | 12px | Standard element gap |
| LG | 16px | Section gap, page margin |
| XL | 24px | Major section separation |

Sidebar: 140px expanded, 64px collapsed.

---

## Corner Radius

| Token | Value | Context |
|-------|-------|---------|
| SM | 4px | Buttons, inputs |
| MD | 8px | Cards, panels |

---

## Layout Pattern

```
┌──────────────────────────────────────────┐
│ Sidebar (140/64) │  Content (fill rest)  │
│                  │                       │
│ Nav buttons × 10 │  PageTitle            │
│                  │  ────────────          │
│                  │  Content              │
└──────────────────────────────────────────┘
```

- Pages use `DockPanel`: toolbar top + status bar bottom + content center
- Each page Margin = 16px
- Toolbar buttons in `WrapPanel`

---

## Interaction

- **Hover**: Button color shift (FluentTheme default)
- **Selected**: Navigation button highlighted (Accent background + white text)
- **Transition**: Sidebar collapse via width binding
- **Focus**: Input focus shows Accent border

---

## Dependencies

- **UI**: Avalonia 12 (FluentTheme)
- **Icons**: `FluentIcons.Avalonia` v2.1.325
- **MVVM**: CommunityToolkit.Mvvm
- **Font**: Inter (built-in)
