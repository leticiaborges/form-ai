# Phase 6 — Brand Color Theme (Replace Indigo/Purple with #00a7d5)

## Goal

Replace all `brand-*` and `brand-*` Tailwind classes with a new brand cyan color (`#00a7d5`), and set it up so that **future color changes only require editing one file** (`index.css`).

---

## Why this approach

Tailwind CSS 4 supports `@theme` blocks in CSS that register custom color palettes as CSS custom properties. Once defined, Tailwind generates `bg-brand-*`, `text-brand-*`, `border-brand-*`, `ring-brand-*`, etc. as utility classes. Components use `brand-N` instead of `brand-N`, and a future recolor is just updating the hex values in `index.css` — no component files need touching.

---

## 1. Define the brand palette — `frontend/src/index.css`

Add an `@theme` block immediately after `@import "tailwindcss"`:

```css
@import "tailwindcss";

@theme {
  --color-brand-50:  #e6f7fc;
  --color-brand-100: #b3e8f7;
  --color-brand-200: #80d9f2;
  --color-brand-300: #4dcaed;
  --color-brand-400: #26bae3;
  --color-brand-500: #00a7d5;  /* base brand color */
  --color-brand-600: #0091b8;
  --color-brand-700: #007a9a;
  --color-brand-800: #00627c;
}
```

Also update the existing `:root` accent variables (currently purple `#aa3bff`) to the brand color:

```css
:root {
  /* replace the 3 accent lines with: */
  --accent: #00a7d5;
  --accent-bg: rgba(0, 167, 213, 0.1);
  --accent-border: rgba(0, 167, 213, 0.5);
}

@media (prefers-color-scheme: dark) {
  :root {
    /* replace the 3 accent lines with: */
    --accent: #26bae3;
    --accent-bg: rgba(38, 186, 227, 0.15);
    --accent-border: rgba(38, 186, 227, 0.5);
  }
}
```

---

## 2. Replace color class tokens across all components

Do a global find-and-replace of Tailwind color tokens:

| Find | Replace | Context |
|---|---|---|
| `indigo-50` | `brand-50` | Page background gradients |
| `indigo-100` | `brand-100` | Badge / icon backgrounds |
| `indigo-400` | `brand-400` | Border & hover states |
| `indigo-500` | `brand-500` | Focus rings |
| `indigo-600` | `brand-600` | Primary button bg, links, text |
| `indigo-700` | `brand-700` | Button hover, text hover |
| `indigo-800` | `brand-800` | Button active state |
| `purple-100` | `brand-100` | Multiple choice badge background |
| `purple-700` | `brand-700` | Multiple choice badge text |

**Files that contain `brand-` or `brand-` classes (16 total):**

- `src/components/Button.tsx`
- `src/components/Input.tsx`
- `src/components/StatusCard.tsx`
- `src/pages/LandingPage.tsx`
- `src/pages/LoginPage.tsx`
- `src/pages/RegisterPage.tsx`
- `src/pages/RegisterSuccessPage.tsx`
- `src/pages/VerifyEmailPage.tsx`
- `src/pages/FormEditorPage.tsx`
- `src/components/editors/QuestionCard.tsx`
- `src/components/editors/OptionRow.tsx`
- `src/components/editors/CheckBoxList.tsx`
- `src/components/editors/RadioButtonList.tsx`
- `src/components/editors/AddQuestionModal.tsx`
- `src/components/editors/TextInput.tsx`
- `src/components/editors/NumberInput.tsx`

---

## Verification

1. Run `npm run dev` in `frontend/` — open every page (landing, login, register, dashboard, form editor).
2. Confirm all buttons, input focus rings, badges, links, and background gradients show the new cyan `#00a7d5` tone.
3. Grep for `indigo` and `purple` in `src/` — expect zero results.
4. Optionally test dark mode (browser devtools → Emulate `prefers-color-scheme: dark`) to confirm accent variables also updated.

---

## Future recoloring

To change the brand color again: update only the hex values in the `@theme` block and the `:root` accent variables in `frontend/src/index.css`. No component files need to change.
