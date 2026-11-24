# Interaction concept (draft, 2026-11-22)

*Deutsche Fassung: [UI-KONZEPT.de.md](UI-KONZEPT.de.md)*

Goal: every page behaves the same, so nothing has to be learned twice. The rules are derived from the existing code and apply to new pages and whenever an old page is touched. The look (paper white, ink, terracotta, serif headings) lives in `site.css` and `CLAUDE.md` and does not change here.

Status: draft, not implemented yet. Section 8 lists the deviations.

## 1. Navigation

Four groups by task, each with a submenu (the page of the group is the first entry):

| Group | Entries |
| --- | --- |
| Now | This month · Calendar |
| Plan | Plans · Needs |
| Collection | Varieties · Crops · Partner plants |
| Garden | My garden · Beds · Pots · Saucers |

- The active group is underlined (as `active` today); the active entry is marked in the submenu.
- The menu works without a mouse (Tab, Enter, arrow keys) and on a phone as one list that opens and closes.
- "Seed Planner" leads to the start page. The start page shows "This month" as the main entry and the groups as tiles.
- Names in the menu and in the page title are the same (today they differ in places, e.g. "Garden" and "My garden (beds)").

## 2. Page structure

1. Exactly one `<h1>` with the name of the page (the garden pages get one too).
2. Below it, if needed, one sentence of explanation (`text-muted`).
3. Toolbar: filter and search on the left, the main action of the page on the right ("New …").
4. Content.

List pages, edit pages (forms) and detail pages each have the same structure, see 4 and 5.

## 3. Actions (buttons)

| Role | Look | Rule |
| --- | --- | --- |
| Main action | `btn btn-primary` (terracotta) | at most one per page or form: Save, New, Calculate |
| Secondary action | `btn btn-outline-secondary` | everything of equal rank: Edit, Export, Back to the list |
| Dangerous | `btn btn-outline-danger` | Delete, Reset; always with a confirmation (section 6) |
| Navigation in text | plain link | only to go to another page, never to change data |

- The word on a button is the verb for what happens ("Save", "Calculate plan", "Sown"), not "OK".
- Order from left to right: main action, Cancel, then dangerous actions set apart on the right.
- `btn-success` and `btn-link text-danger` are no longer used. In `site.css` `btn-success` and `btn-primary` already look the same, so only the class name changes.
- Actions on a table row are secondary actions, small (`btn-sm`). Delete only on the edit page, not in the list.

## 4. Lists

- Filter and search on top (`_FilterBar`), sorting by the column headers, number of results visible ("12 of 203").
- The name in the first column is the link to the detail or edit page; the row itself is not clickable. There is no extra "Edit" button in the row.
- Empty list: one sentence plus a link to the main action ("No pots yet. Add a pot").
- Lists with sub-items are always foldable (`<details>`), the number of sub-items is in the heading. By default everything is closed, even if a group contains work; the heading shows the number instead (e.g. "Harvest (3)"). There is one shared "Open/close all" switch (`fold-groups` today). Exceptions: areas that only appear after an action (properties of the selected area in the garden editor) and areas with active content (a set filter, the row just edited, chosen pots) are open.
- Numbers right-aligned, text left-aligned, units in the column heading.

## 5. Forms

- Label above the field, required fields marked with `*`, help text below the field (`form-text`), errors at the field and additionally as a summary on top.
- Fields on a 12-column grid; the order follows the way of thinking: what is it (crop, name) → where from (source, stock) → how does it grow (sowing, growing place, location, role) → measures → times → notes.
- Rarely used fields in a foldable section, not in the main form.
- At the bottom, always in this order: **Save** · Cancel · (gap) Delete.
- After saving: back to the list (new record) or to the detail page (change) with a green "Saved" message. On errors the input is kept.
- Date fields are real date fields. If one field determines another (start → end), the other is set automatically but can be overwritten.

## 6. Feedback and confirmation

| Case | Pattern |
| --- | --- |
| Success | green `alert-success`, does not disappear by itself, gone after a reload |
| Warning / hint | `alert-warning` with the next step as a link ("Set the garden area") |
| Error | `alert-danger` on top + message at the field |
| Delete | confirmation dialog with the name ("Delete pot "Blue 1"?") and, if something depends on it, what is deleted with it or left orphaned |
| Reset / switch that discards input | the same confirmation dialog |
| Undo possible (garden map, "done" mark) | no confirmation, an undo button instead |

- Hints about missing data (e.g. no garden area, no spacing on the variety) always say where to fix it.

## 7. Texts and language

- All texts come from resources (DE/EN/RU), never from code. Names in the database are English, the display is translated.
- The same things are called the same everywhere: "variety" (Plant), "crop" (PlantType), "plan", "bed", "pot". Verbs: "add" for something new, "edit", "delete", "save".
- Dates and numbers follow the culture of the browser, measures always with a unit (m², cm, l).

## 8. Deviations in the code (as of today)

| Area | Today | Target |
| --- | --- | --- |
| Navigation | 11 entries in one row | 4 groups (section 1) |
| Main button | 25× `btn-success`, 2× `btn-primary` (pots, beds) | `btn-primary` everywhere |
| Delete | red outline (6 pages) and `btn-link text-danger` (6 places) mixed | always `btn-outline-danger` |
| Confirmation | 11 `confirm` calls, text differs per page | shared helper, text with the name |
| Cancel | missing in the plan list, Needs, the "done" marks of This month | in all forms as in section 5 |
| Page title | garden pages have no `<h1>` in the page body, the title comes from the editor | `<h1>` as everywhere |
| Foldable groups | Distribution 9, Garden 18, Plants/Edit 3, Calendar 1, This month 2 – default open/closed inconsistent | everything closed by default (section 4) |
| Messages | `alert` in 5 places in Distribution, elsewhere partly `TempData` | shared partial `_Message` |
| Variety form | order just rearranged | pattern for the other forms |
| Menu texts | "Garden" ↔ "My garden (beds)" ↔ "Plan and plot" | one name per page |

## 9. Implementation steps

1. Partials and helpers: `_Message`, `_Confirm` (confirmation dialog), confirmation rule, form footer (`_FormActions`: Save/Cancel/Delete).
2. Unify buttons and delete (purely mechanical, `btn-success` → `btn-primary`, delete → outline).
3. Navigation in groups with a submenu (layout, resources, CSS, phone view).
4. Bring the pages one after the other to the structure and form pattern: pots, saucers, beds, crops, varieties, plans, garden.
5. Align the texts (menu and page names DE/EN/RU).
6. At the end mark this document as "implemented".

After every step: build and tests, then commit.
