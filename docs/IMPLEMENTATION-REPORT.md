# Implementation report

An honest account of what was built, how it was verified, and what is missing.

---

## 1. Verification actually performed

Claims below are backed by commands whose output was observed. Nothing here is inferred.

### Build

```
[00:44:56] Keyward build - Debug
[00:44:57]   Keyward.Core.dll   OK     71,680 bytes  (12 files)
[00:44:58]   Keyward.exe        OK    139,264 bytes  (9 files)
[00:44:59]   Keyward.Tests.exe  OK     62,464 bytes  (5 files)
[00:44:59] Build complete.
```

### Tests — 86 passed, 0 failed

```
==================================================================
  86 passed, 0 failed, 0 skipped
==================================================================
```

Coverage by suite:

| Suite | Cases | What it proves |
|---|---|---|
| SecureRandom | 7 | Bounds, no modulo bias (60k draws across 12 buckets), alphabet containment, shuffle preserves the multiset, 256 unique pass-phrase words |
| SecretBytes | 3 | Buffers are zeroed on dispose; use-after-dispose throws; dispose is idempotent |
| Key derivation | 7 | Determinism, distinct passwords → distinct keys, sub-keys differ, unique salts, iteration floor enforced, unknown algorithm refused |
| Authenticated cipher | 12 | Round-trip, ciphertext opacity, fresh IV per seal, tamper detection (ciphertext / IV / MAC / truncation / wrong key), constant-time compare, AAD binding |
| Password generator | 16 | Length, class inclusion, class exclusion, ambiguous exclusion, uniqueness, alphabet coverage, invalid configurations, pass-phrase structure, strength estimator |
| Vault file | 17 | Round-trip, no plaintext at rest, notes/folders/tags, wrong password rejected, tamper detection, KDF downgrade resistance, future-version refusal, malformed/empty/truncated input, atomicity |
| JSON | 12 | Parsing, round-trip, escaping, unicode, member order, 6 malformed-input rejections including depth limiting |
| Atomic storage | 5 | Write/read/replace, no leftover temp files, nested directory creation, oversize refusal |
| Vault session | 7 | **Immediate persistence on creation**, refused unlock leaves no file, save/reopen, delete persistence, lock destroys keys, auto-lock arming, master password rotation |

### Application inspected on a real desktop

The application was built, launched, and driven with synthetic mouse and keyboard input at
150% display scaling. Screenshots in `artifacts/`:

| File | Shows |
|---|---|
| `v10.png` | First run: brand, strength guidance, recovery warning, create action |
| `drive-1-filled.png` | Live strength meter reporting "Excellent · about 267 bits" |
| `drive.png` | Unlocked workspace: navigation rail, list, empty state, detail pane, auto-lock countdown |
| `vault-item.png` | Search placeholder and corrected "Locks in 4m" countdown |

A crash log (`%APPDATA%\Keyward\crash.log`) was added and used throughout; the final runs produced
no entries.

---

## 2. Defects found and fixed during development

These are the substantive findings, not typos.

### Critical — vault encrypted with an all-zero key

`KeyDerivation.Derive` copied the derived material into two buffers, wrapped them in `SecretBytes`,
and then zeroed those same buffers in a `finally` block. `SecretBytes` takes ownership of the
array, so the "cleanup" wiped the keys to zero the instant they were created.

Every vault would have been encrypted under a key of all zeros — the master password would have had
no effect at all, and the wrong password would have opened the vault.

Found by a focused reproduction after the test suite flagged a parameter error that had masked it.
The reproduction printed `keys differ: False`. Fixed by transferring ownership and clearing only
the intermediate PBKDF2 buffer. Regression tests assert that different passwords produce different
keys and that the two sub-keys are never equal.

### High — new vault never written to disk

`VaultSession.Unlock` created a brand-new vault in memory and returned success without persisting
it. A user who created a vault and closed the app before adding an entry would find no file on the
next launch, and would be asked to create a vault again — silently abandoning the master password
they had just chosen.

Found by driving the real UI and then checking the data directory. Fixed by writing immediately on
creation, with a named regression test.

### Medium — KDF default was ~3× the cost users would accept

The initial default was 600,000 iterations. Measured on the target machine that is **9.8 s per
unlock**, which pushes users toward leaving the vault unlocked. Changed to 210,000, the OWASP
figure for PBKDF2-HMAC-SHA512, which measures at ~3.6 s.

### Medium — window overflowed the screen on a scaled display

The window was sized with fixed DIP dimensions. On this 1920×1080 display running at 150% scaling
that produced a window wider than the screen, pushing the right-hand pane off-screen entirely.
Fixed by sizing against `SystemParameters.WorkArea` in DIPs and adding a per-monitor DPI-aware
application manifest.

### Medium — truncated warning text

The first-run recovery warning placed text inside a horizontal `StackPanel`. A horizontal stack
panel offers its children infinite width, so the text rendered on one line and was silently
clipped. Replaced with a two-column grid. This class of bug is easy to miss because the text still
"exists" in the visual tree.

### Low — duplicated auto-lock label

`"Locks in " + FormatRemaining(...)` where `FormatRemaining` also prefixed `"Locks in "`, giving
"Locks in Locks in 4m".

---

## 3. Known limitations and unfinished work

Stated plainly. None of these are hidden behind a disabled button or a "coming soon" row.

### Not implemented

- **Import / export.** The brief asked for CSV/JSON import and export with validation and duplicate
  detection. Not built. There is no import or export UI at all — rather than ship a button that
  produces a half-working file.
- **TOTP / authenticator codes, passkeys, encrypted attachments, browser integration, breach
  monitoring, encrypted sync.** Treated as separate projects; none are stubbed or faked.
- **Cross-platform builds.** Windows only. See `ENVIRONMENT.md` for why.
- **Tray icon / minimise-to-close behaviour.** The setting exists in the model but has no UI.
- **Folder and tag creation UI.** Folders and tags are modelled, persisted, filterable and shown in
  the rail, but there is no "create folder" affordance yet, so the rail reads "No folders yet"
  unless folders arrive by import.
- **Tag editing.** Tags persist and filter, but cannot be typed in the UI.
- **Reduced-motion preference.** No animations are long enough to be a hazard, so the
  `SystemParameters.ClientAreaAnimation` check was not wired up; nothing in the current build
  ignores it in a harmful way, but the claim is not made.

### Verified only at the service layer

- **Create / edit / delete a credential through the interface.** `VaultSession` create, commit,
  delete and reopen are covered by tests. Driving the "+" button with synthetic clicks did not
  produce a reliable hit during the session, so the click path itself is unconfirmed on screen even
  though the code path is short and directly calls a tested method.

### Not verified

- Automated UI / accessibility testing. Accessibility was implemented deliberately — visible focus
  rings drawn per-control, `AutomationProperties` names on interactive controls, contrast checked
  against WCAG AA, full keyboard operation — but **not** verified with a screen reader, and **not**
  tested with a screen-reader user.
- Behaviour across multiple monitors with mixed DPI.
- Behaviour on a display at 100% scaling (this machine runs 150%).
- Large-vault performance. Tested with hundreds of items at most, not thousands.
- Real clipboard contention under other applications.

### Known technical debt

- The list row template embeds geometry via `Icons.Get("star").ToString()`. That works but couples
  the template to one icon; a resource dictionary would be cleaner.
- The JSON reader is hand-written because .NET Framework 4.8 has no `System.Text.Json` and no
  package feed was available. It is strict and tested, but it is more code to own than a dependency.
- Search is an unindexed linear scan with per-character string comparison. Fine at this scale; a
  vault of tens of thousands of entries would want an index built after unlock.
