# Skype-style pop-up notifications

Written plan only. This document does not add a pop-up window, a timer, or a call to `SHQueryUserNotificationState`. The shipping presenter is the OS toast in `OsToast.cs`.

## Goal

A small pop-up near the notification area, in the style of older Skype, for a message that has already been normalized by the notification pipeline. It would be an extra presenter beside the OS toast, off unless a later setting turns it on.

## Architecture

The capture path stays as it is. A non-persistent `NotificationReceived` and the page shim for `showNotification` each become one `NotificationRecord` (title, body, icon, tag, url, service). `NotificationPipeline` still decides whether that record is a new message or a debounced "New activity".

A later `PopupPresenter` would accept the same record the toast presenter accepts:

1. The pipeline says the record should be shown.
2. The presenter asks Windows whether the user is accepting notifications.
3. Only then does it create a borderless, topmost WinForms window that does not activate.
4. The window shows the title and body, then closes itself after a few seconds.
5. A click uses `NotificationNavigation.OpenTarget` and the existing restore path. Esc or the close control dismisses the pop-up without opening a chat.
6. A second record with the same tag replaces the visible pop-up instead of stacking another one. That is the same dedupe rule as the toast.

The pop-up must not be a second WebView. It is a native window fed by the record, so a Facebook page cannot draw outside the card.

Position is the corner of the monitor that contains the cursor, a short gap above the taskbar. High DPI uses the monitor's `DpiX` / `DpiY` rather than a hard-coded pixel size. Two monitors do not each get a copy.

## Libraries

No new library is required for the pop-up itself. WinForms can host the window. The DND check is `SHQueryUserNotificationState` in `shell32`, which is already on Windows.

The OS toast path stays on `Microsoft.Toolkit.Uwp.Notifications` 7.1.3. That library is what honors Focus Assist today. A custom pop-up does not, unless the app checks the shell state first and refuses to show.

Do not add a third-party toast skin. It would be another way to draw the same card and another package to ship.

## Do not disturb

Call `SHQueryUserNotificationState` immediately before the pop-up is shown. Show the pop-up only when the result is `QUNS_ACCEPTS_NOTIFICATIONS` (5).

| State | Value | Pop-up |
| --- | --- | --- |
| `QUNS_NOT_PRESENT` | 1 | No. Nobody is logged in to the console session |
| `QUNS_BUSY` | 2 | No |
| `QUNS_RUNNING_D3D_FULL_SCREEN` | 3 | No. A game or exclusive full-screen app is running |
| `QUNS_PRESENTATION_MODE` | 4 | No |
| `QUNS_ACCEPTS_NOTIFICATIONS` | 5 | Yes |
| `QUNS_QUIET_TIME` | 6 | No. Quiet hours |
| `QUNS_APP` | 7 | No. Focus Assist, or an app has asked for quiet |

The check is a snapshot. The user can turn Focus Assist on while the pop-up is already visible. The pop-up should still close on its own timer. It should not re-check in a loop.

OS toasts stay the default because Windows already applies this policy, including Quiet Hours and priority lists the shell API does not fully describe. The custom pop-up is opt-in so a missed check cannot bypass Focus Assist for every message. If the shell call fails, do not show the pop-up. Fall back to the OS toast, not to an unchecked window.

The tray balloon in the current presenter is the failure path for OS toasts. It does not honor Focus Assist. A Skype-style pop-up must not become a second unchecked path.

## Risks

- A missing or wrong `SHQueryUserNotificationState` check shows a card during Focus Assist, a full-screen game, or a presentation. That is the main reason this stays a plan.
- The pop-up can steal focus if it is an ordinary form. It has to be shown with `ShowWithoutActivation`, or the chat WebView will blur on every message.
- Click-through and hit testing: a topmost window over a full-screen app is hard to dismiss. The full-screen state above is why that case is suppressed rather than styled.
- Two presenters (OS toast and pop-up) will double-notify unless the setting selects one.
- The pop-up is not in the Action Center. Dismissing it does not match the OS notification history. A user who misses it has no record unless the OS toast is also posted.
- Remote icons are http URLs. Loading one into the card can hang the UI or pull an image from Facebook's CDN. Reuse the record's icon string only after a short timeout, and show initials when it fails.
- Narrator and high contrast need a real window title and system colors, not a painted bitmap with no accessible name.
- The window must not read or copy the WebView2 profile. It only receives the already-normalized record.
- Calls are untested in the pipeline. A pop-up must not guess that a title change is a call.

## Not in this change

No pop-up type, no `SHQueryUserNotificationState` P/Invoke, and no setting. The follow-up list in `docs/ROADMAP.md` is where the implementation stays until it is scheduled.
