# MessengerDesktop roadmap

Base: .NET + WinForms + WebView2 (kept; see docs/architecture.md ADR-001). Every PR is a draft, stacked on the previous one, Windows CI green, README + docs/architecture.md updated, Windows checklist in PR body, skills/libraries used listed. Merge order = PR order. Cosmetic issues → follow-up list, never blockers.

Test method key: CI = windows-latest build + xUnit; PC = Zerkoto manual checklist.

## PR2 — split: PR2a foundations (.NET 10, CI tests, JS extraction, docs; no behaviour change) then PR2b tray/lifetime (T1–T4) on top; base main
Target updated to .NET 10 per CoS 2026-10-29.
T1 Settings store. One JSON settings service (%LOCALAPPDATA%\MessengerDesktop\settings.json), typed, atomic write, defaults, corrupt-file recovery. Accept: unit tests for load/save/defaults/corrupt. Test: CI.
T2 App lifetime module. Single instance (named mutex + activation signal), arg parsing (--minimized). Accept: second launch focuses first; tests for args. Test: CI + PC.
T3 Tray + close-to-tray. Tray icon, Open / Start with Windows ✓ / Exit; X hides; WebView kept running (throttling approach documented). Accept: Exit leaves no process; messages keep arriving hidden. Test: PC.
T4 Start with Windows. HKCU Run entry with --minimized, default ON, toggle reflects registry, path self-heal. Accept: tests for value build/compare; regedit check. Test: CI + PC.
T5 docs/architecture.md (module map, ADR-001 stack decision incl. 2026 trends review: WinUI3/WinAppSDK, Tauri/Rust, Avalonia considered) + docs/ROADMAP.md. Accept: docs present and consistent with code. Test: review.

## PR3 — Notification pipeline (depends PR2)
T6 Permission: auto-grant Notifications for facebook.com only. Test: PC.
T7 Capture (records carry tag for dedupe): WebView2 NotificationReceived if SDK supports it, else JS Notification shim; normalize to one Notification record (title, body, icon, tag, url, service). Accept: unit tests for normalization; shim tests where feasible. Test: CI + PC.
T8 Presenter (dedupe by notification tag, so the same message never toasts twice; tag-dedupe unit tests): native Windows toast (CommunityToolkit/WinAppSDK AppNotifications, needs AUMID + shortcut) so Focus Assist/DND is honored by the OS; fallback to tray balloon. Click → restore + open chat from url/tag, else inbox. Test: PC.
T9 Unread: parse page title "(N)" → tray tooltip + taskbar overlay badge. Accept: parser tests. Test: CI + PC.
T10 Reaction heuristic: unread/title increase with no message notification → generic "New activity" toast (debounced). Document coverage table (messages yes; reactions best-effort; calls untested). Test: PC.
T11 docs/popup-notifications-plan.md (Skype-style pop-up design: architecture, libs, DND via SHQueryUserNotificationState + OS toasts, risks). No pop-up code. Test: review.

## PR-X1 — Service-profile layer + X overlay (depends PR3)
T12 Service-profile layer: ServiceDefinition (name, WebView2 profile name, home URL, link rules, tab|overlay). Facebook migrated onto it without behaviour change. Accept: reel tests still green; link-rule tests. Test: CI.
T13 Overlay host (PR #1 reel behaviour must stay identical; PR checklist lists reel regression checks: open, loader, 8s timeout, close returns to same chat without reload, reopen): generalize PR #1 reel overlay (cover, spinner, 8s timeout, Esc/close) to take a service + URL; reel-specific close/history logic stays in reel service. Test: CI + PC (reels regression).
T14 X service: own profile (WebView2-X), login once in-app, capture x.com/twitter.com status links (unwrap l.facebook.com), open in overlay; Esc returns to same chat, no reload. Test: CI (rules) + PC.

## PR-X2 — Optional X preview cards (depends X1; only if X oEmbed works keyless)
T15 Verify publish.twitter.com/oembed keyless; if not, close task with note.
T16 Settings toggle (default OFF); inject card under x.com links via DOM, fail-silent; click → overlay. Test: CI (parser) + PC.

## PR-IG — Instagram tab service (depends X1; can follow X2)
T17 Tab strip in title bar (Neutron Designer option A per CoS), Ctrl+1/Ctrl+2, unread pills from title counts. Test: PC.
T18 Instagram service: own profile, instagram.com/direct, loaded at startup and kept alive (Designer option A; no side-by-side), link rules; notifications through pipeline (T7–T8). Test: CI (rules) + PC.

## Follow-ups (not scheduled)
Skype-style custom pop-up implementation; cosmetic polish (Codex later); calls/downloads testing.
Status: APPROVED by Zerkoto via Chief of Staff, 2026-10-29.
