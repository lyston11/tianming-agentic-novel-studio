# Component Guidelines

> This document records conventions already implemented in this repository. It is not a planning draft.

## Scope

Apply these rules to React components in `tianming-web/frontend/src`. The code
uses React 19, Tailwind CSS 4, Radix primitives, shadcn-style wrappers, and
feature-local composition. There is no separate styled-components layer.

## Component Shape

- Use function components and explicit props. Small shared components may use an
  inline prop type; feature components use a nearby `interface`, as shown by
  `PageHeaderProps` and `ChapterWorkspaceProps`.
- Keep route/page composition in a feature and keep primitive behavior in
  `components/ui`. A component should render from props and callbacks rather
  than reaching into an unrelated feature's private state.
- Put the component's main return path after early loading/empty checks. The
  pattern in `ChapterWorkspace` makes loading, no-selection, and ready states
  explicit instead of rendering a partially valid object.
- Use `ReactNode` slots for composition when a caller supplies a complete action
  or child region. `PageHeader` accepts `actions?: ReactNode` rather than
  knowing every possible toolbar action.
- Keep a component's state local when it is draft input, selection, disclosure,
  or another transient UI concern. Move server state and mutations into a
  feature hook or query owner.

## Props and Events

- Name callback props by intent, such as `onAccept`, `saveManualEdit`, and
  `onOpenWorkflow`. Pass the smallest input needed by the callback.
- Keep required/optional/null semantics explicit in the props interface. Do not
  use an untyped object or a broad index signature to avoid defining a prop
  contract, except for the existing markdown renderer boundary.
- Use stable keys from domain identifiers when mapping repeated records. The
  `reviewArtifacts` and `citations` lists in `ChapterWorkspace` use `artifact.id`
  and `citation.id`.
- Disable commands from the owning mutation/status state. A child should not
  duplicate the server transition rules; it receives `disabled` or an explicit
  command callback from its feature container.

## Styling and Composition

- Use Tailwind utility classes and the local `cn` helper from `src/lib/utils.ts`
  to merge optional `className` values. Follow neighboring files' quote and
  formatting style; do not reformat copied UI primitives while changing a page.
- Reuse `components/ui` primitives such as `Button`, `Textarea`, `Dialog`,
  `Select`, and `Tabs`. Their Radix behavior, focus styles, and data attributes
  are already centralized.
- Use `class-variance-authority` in a reusable primitive when variants are part
  of its public contract, as in `components/ui/button.tsx`. Do not reproduce
  the same variant matrix in individual feature components.
- Keep visual state readable from the markup: loading, error, empty, disabled,
  and selected states should have explicit classes or content.
- Use `lucide-react` icons through the existing UI primitives when an icon is
  needed. Decorative or brand-specific markup must stay in its existing shared
  component rather than becoming a new icon system.

## Accessibility

- Prefer semantic `main`, `header`, `nav`, `section`, `article`, and `form`
  elements. Give a region an `aria-label` when its purpose is not conveyed by a
  visible heading.
- Pair every form control with `Label` and a matching `htmlFor`/`id`, and expose
  operation failures with `role="alert"` when the user must act on them.
- Use real buttons for actions, preserve disabled behavior during mutations, and
  keep Radix dialog/select focus and keyboard behavior instead of replacing it
  with a div-based popup.
- Preserve visible focus styles supplied by the UI primitives. Do not hide a
  close or submit control just because the normal path is mouse-driven.

## Real Examples and Tests

- `tianming-web/frontend/src/features/workflow/chapter-workspace.tsx` shows
  typed callbacks, early loading/empty states, stable list keys, and a controlled
  text edit draft.
- `tianming-web/frontend/src/features/workflow/goal-workbench.tsx` shows a
  feature container passing query-owned data and commands to children.
- `tianming-web/frontend/src/components/shared/page-header.tsx` shows a typed
  `ReactNode` slot and `cn` class composition.
- `tianming-web/frontend/src/components/ui/dialog.tsx` shows the local Radix
  wrapper and accessible close/title/description composition.
- Add a component test only when it protects a user-visible state, accessible
  interaction, or command boundary that could otherwise regress silently.

## Wrong vs Correct

Wrong: let a card call `fetch`, mutate a copied `production.status`, and render
an unlabelled clickable `div` for accept.

Correct: the feature hook owns the mutation, the component receives `onAccept`
and `disabled`, the button is a real `Button`, and the next authoritative
workflow snapshot is read through the existing query owner.
