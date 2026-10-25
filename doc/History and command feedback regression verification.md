# History and command feedback regression verification

## Implementation scope

- Mode: `satisfy-current-intent`.
- Specs used as intent: IDD-0004 (history/graph), IDD-0009 (responsiveness),
  IDD-0019 (continuous graph surfaces), IDD-0045 (command feedback lifecycle).
- Engineering rules applied: none; `.idd/engineering/` is absent.
- Verification policy: `.idd/verification.yaml` is absent; use the repository CI
  Release restore/build/test commands as the verification fallback.
- Base: `8990135` from `origin/fix/git-command-async-completion-20261025`.
  The five commits following `d5f2719` already isolate stream pumps from the UI
  synchronization context and isolate activity observers. Those changes are
  included in the branch ancestry, not reimplemented here.

## Causes and changes

`HistorySubjectReferencesPanel` measures its children at natural and allocated
widths. `HistoryReferencesPresenter.MeasureOverride` previously applied each
intermediate badge allocation to attached children. Alternating those constraints
changed child Width/Visibility repeatedly and invalidated layout again. Live
stack samples during the stall showed the UI thread in reference badge
measurement/arrangement. Git had already exited.

The presenter now measures reusable detached probes, caches natural metrics for
the current references/theme, and applies only the final allocation during
arrangement. On the same 3DRoomLayoutEditor repository, the graph is now visibly
painted and commit details are available. Skia itself was not established as the
cause; the earlier diagnosis attributing the problem to Skia was too strong.

Successful command feedback previously started its close clock in a background
completion callback. If the UI was delayed longer than that interval after
showing Running, its next capture could dismiss success without ever displaying
it. The first visible success capture now starts that clock. Lifecycle delivery
also requests feedback rendering immediately on the UI thread; streamed output
retains its existing batching. Time hidden behind the manually opened Git Console
does not consume the success interval.

- Behavior changed: reference layout settles; a delayed UI receives the full
  configured success interval.
- Behavior preserved: Git topology, adaptive graph width/metrics/cache, reference
  order and annotations, output sanitization/bounds, per-activity routing,
  cancellation, user dismissal, failure priority and hover/focus pause.
- Public contracts preserved: no application/Git interfaces or settings changed.
- Compatibility/data constraints: no migration, legacy wrapper or new fallback.
- Code areas changed: reference presenter, feedback coordinator/lifecycle host,
  graph paint verification and the existing viewport check.
- Tests changed: add delayed-success regression; extend the existing viewport
  scenario to both reference-bearing production templates and require an actual
  Skia paint callback for the current geometry.

## Verification

- Debug build passed.
- Focused Desktop checks: 18 passed, including real viewport resize/recycling.
- Focused Application checks: 22 passed, including the incoming observer fix.
- Git streaming checks: 6 passed, including the incoming non-pumping UI-context
  regression.
- Release build passed without warnings after Release-specific restore.
- Final Debug regression checks: 16 passed, including constrained 1000 px windows
  with long reference names and a success card hidden behind the Console.
- Full Release Application checks: 512 passed. Full Release Desktop checks:
  581 passed. Full Release Git checks: 507 passed. Total: 1600 passed, no failures
  or skips. These preceded the final constrained-window/hidden-card additions,
  which were then verified by the Debug regression checks above.
- Final Release rebuild passed with zero warnings/errors; all 15 feedback
  coordinator checks passed again against that final build.

## Focused intent drift audit

Scope: changed reference layout, graph painting evidence, and success-feedback
lifecycle against IDD-0004/0009/0019/0045; no product-wide audit.

- Product Intent conformance: the scoped application fixes match current intent;
  no new product behavior or missing intent was introduced.
- Remaining implementation drift against IDD-0009: the Windows native
  accessibility crash below still violates responsiveness/reliability. Its
  ownership is in the Uno dependency, but its effect on this application remains.
- Engineering conformance: no Engineering layer is configured.
- Verification evidence: automated lifecycle and desktop rendering evidence is
  present. macOS interactive verification is unavailable on this Windows host.
- Remaining risk: interactive verification exposed the separate Windows runtime
  crash below. Full interactive Pull completion cannot be approved while that
  dependency issue remains.

## Separate Uno Windows accessibility crash (unresolved)

An isolated repository with a local bare remote reproduced a native crash after
`git pull --progress --prune --ff-only` reported `Already up to date.` Windows
Application events 1026 and 1000 on 2026-10-25 identify an AccessViolation
(`0xc0000005`) in `Win32UIAutomationInterop.UiaRaiseStructureChangedEvent`, called
from `Win32Accessibility.FlushStructureChanges`. This also reproduced during
ordinary initial repository presentation after UI Automation had been used.

The installed `Uno.WinUI.Runtime.Skia.Win32` 6.7.135 declares that native function
with three arguments and a SAFEARRAY runtime ID. The native
[Windows contract](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationcoreapi/nf-uiautomationcoreapi-uiaraisestructurechangedevent)
requires four arguments: provider, change type, integer-array pointer, array
length. The same declaration was found in the cached 6.7.103 runtime and the
upstream [source](https://github.com/unoplatform/uno/blob/master/src/Uno.UI.Runtime.Win32/Accessibility/Win32UIAutomationInterop.cs)
inspected during this investigation. No stable package with a
verified fix was identified.

No runtime monkey patch, accessibility suppression, binary rewriting or SDK
downgrade has been introduced to hide this defect. Fixing/distributing the Uno
interop correction is separate dependency work still needed for this scenario.
