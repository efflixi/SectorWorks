# Windows desktop testing

The owner verified the pre–Phase 6 UI and packaging changes on a Windows 11
Pro work PC and later reported that the CD metadata review build also worked
as intended there. See the [platform status record](platform-status.md#ui-and-package-review-before-phase-6)
for the scope of these smoke tests.

## Superseding scope/acceptance decision — November 17, 2026

[The accepted 1.0 scope and exit audit](1.0-scope-closeout.md) closes revised
Phase2/2B/2C/2D/3 gates on existing evidence and explicit owner acceptance.
Remaining modern NTFS qualification is DEFERRED POST-1.0 to P1.NTFS.MODERN.1–.6;
physical ARM64 is NOT VERIFIED — DEFERRED POST-1.0 BY OWNER, separately
P1.ARM64.LINUX/P1.ARM64.WINDOWS. Four-target builds remain; owner has hardware
for later testing. Prior dated blocked/pending descriptions below retain their
historical scope, not current exit requirements. No Phase4 or new probes are
authorized by this documentation task. Safety/media/manual-tunnel rules remain.


The local Linux development machine remains the primary development and default
test host. Local automated tests and Linux UI checks continue to run there.
This document governs testing of the Windows version only.

The owner verified the `windows_laptop` MCP connection and selected it as the
default Windows test interface on **2026-11-16**. Use it for both background
commands and graphical SectorWorks tests. Continue an authorized test through
execution, inspection, diagnosis and cleanup without routine permission stops.
Request help only for a specific blocking action, credential/UAC prompt,
unavailable hardware or decision outside the authorized scope.

## Resumed R4b closeout, November16 evening / November17 UTC

The owner restored the manual tunnel after update/reboot. Current standard-user
R4b application workflows and exact Save As/commit outputs now pass;56independent
Linux NTFS commands verify the returned bytes. Real restarted recovery cleans
only matching-source artifacts and refuses the stale changed-source case.
Ten explicit private-store checks and separate reload pass; these do not claim
a second account or whole new OS profile. Screenshots correspond to current R4b.

Both deferred old desktop roots were already cleaned before the crash. Current
resumed root/stale test registration now also have verified hash/archive cleanup;
normal171-byte preferences/layout persist. No task app/test remains; control is
released. Manual tunnel retained. Later dated connection-loss/UAC-unavailable
statements below are historical; they do not override restored owner authority.
Native writer-safety and physicalARM64 gates remain incomplete.

## Connection and authorization

The server runs in the signed-in user's interactive Windows session. Its
capabilities are those of that session; tool availability does not prove an
administrator token or grant permission to change privileges. The existing
standard SSH account remains standard. Its enrolled key now enables forwarding
for the owner-authorized loopback MCP destination while retaining the source-IP,
PTY, agent and X11 restrictions. A backup of the previous key entry was verified.
Fresh key login, the permitted TCP channel and rejection of another local-forward
destination were tested. No SSH service restart or account elevation was needed.

Keep server/client bearer tokens, connection addresses, private keys and local
access inventory outside the repository and synchronized storage. Read current
configuration locally; never print secrets, place them in artifacts or ask the
owner to paste them in chat. If authentication fails, report the response and
which configuration needs correction without exposing the value. The client and
server must use the same actual token, not the setup placeholder.

## Ordinary testing workflow

1. Discover the `windows_laptop` tools and check the current desktop with
   `Snapshot` before any graphical action. Reuse the owner's existing
   authorization to focus windows or enter test data.
2. Prefer background `PowerShell` for builds, finite tests, transfer preparation,
   hashes and output inspection. Use exact paths and bounded commands. Preserve
   useful reports, errors and source/output identities. Do not claim UI behavior
   from a command-line pass.
3. For UI workflows, launch or switch to the task's application, inspect the
   observed controls, act, and inspect the resulting screenshot/accessibility
   state. Scope screenshots to the relevant application when practical. Capture
   and actually inspect rendered output when visual behavior matters.
4. Use fresh disposable fixtures and task-owned windows/documents. Preserve
   existing user documents, owner processes and unrelated desktop state. Avoid
   shared clipboard replacement when `Type` or another supported API suffices.
5. When finished, or before waiting for the owner, call `ControlStatus` with
   `release=true`. Clean only task-owned disposable files/processes, retaining
   evidence. The owner's takeover mechanism remains available.

Read-only preflight still precedes destructive/native disk tests. The continuous
[owner assignment](sectorworks_phase_2d_and_phase_3_autonomous_sol_prompt.txt) supersedes
the earlier owner-launch-only restriction for generated media; actual token/UAC
availability still governs execution. On **2026-11-16**, the owner is unavailable for further UAC
approvals for the rest of the day. Real-disk exclusions, protected-media rules, TxF/CLFS/LFS safety
gates and the assignment's phase/publication boundaries remain in force.
If a needed privilege is absent, state the exact action and blocker rather
than changing account membership or installing an elevated worker implicitly.

## Verified infrastructure test, 2026-11-16

Actual MCP `Snapshot` captured the visible desktop. `App` launched Notepad,
which restored an existing document; that tab was left unmodified. A fresh tab
was created, `Type` entered `SectorWorks desktop connection verified`, and a
second window-scoped `Snapshot` showed that exact editor value and a modified
unsaved tab. No Save operation ran. The document was left open and unsaved, and
`ControlStatus(release=true)` returned control to the owner.

This verifies desktop capture, application launch, text entry and inspection.
It is infrastructure evidence, not a SectorWorks GUI workflow pass or full
Phase 2D completion. SectorWorks native picker, keyboard, clipboard, drag/drop,
layout and accessibility checks must still run against an identified build and
named workflows before their respective checklist items can close.

## Published SectorWorks evidence and current connection disposition

The November16 R3 published Windows application now has actual picker,
navigation, staging/history, Save As, confirmed commit and restarted recovery
cleanup/refusal evidence; returned saved/committed bytes pass independent native
checks. App/control were closed/released. This closes named workflows, not
blanket clipboard/drag/drop/accessibility or modern Windows writer support.

The visible tunnel subsequently closed; do not recreate it. Corrected R4
first-profile-path sources pass Windows standard-user CLI checks and the runnable
package is deployed, but final R4 desktop revalidation is unavailable. The
restricted SSH account cannot access the desktop profile's two task test roots
and intentionally stale recovery registration; retain them until ordinary
desktop control returns. No elevation/ACL workaround or extra UAC prompt is
requested. Exact source/package/GUI identities and cleanup receipts are in the
[final review](phase2d-phase3-review.md) and its evidence index.
