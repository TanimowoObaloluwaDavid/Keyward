# Environment, stack selection, and competitor notes

## The constraint that decided the stack

Before choosing a framework I measured what this machine can actually do. The results ruled out
the two obvious answers:

| Capability | Measured | Consequence |
|---|---|---|
| Rust / Cargo | **absent** | Tauri would need rustup, a toolchain and MSVC build tools — gigabytes |
| Electron | **infeasible** | ~110 MB binary from GitHub measured at **1.8 KB/s** ≈ 17 hours |
| npm registry | reachable but **2–50 KB/s** | Even a modest dependency tree is impractical |
| `dotnet` SDK | **absent** | No `dotnet build`, no NuGet restore |
| MSBuild | **absent** | No `.xaml` → `.baml` compilation step |
| `csc.exe` (.NET Framework) | **present**, C# 5 language level | Compiles C# directly |
| WPF assemblies | **present** and rendering correctly | Real native desktop UI |
| JDK 17 | present, but no JavaFX | Swing would be a poor fit |
| WebView2 runtime | present | Could host HTML, at the cost of a webview boundary |

**Decision: native Windows, C# on .NET Framework 4.8, WPF for the interface, zero external
dependencies.**

This was not a preference. Tauri and Electron were rejected on measured evidence, Flutter was
excluded by the brief, and WPF was the only option that produced a real, native, offline-buildable
desktop application here. It also happens to be a good fit for the domain: a password manager needs
precise keyboard handling, a real clipboard, file ACLs, DPAPI-class OS integration and a
non-negotiable trust boundary — all of which are native-first.

### Consequences of the older framework

The WPF reference assemblies on this machine are older than .NET 4, which shaped several
implementation choices. Each was a deliberate adaptation, not an oversight:

| Missing | Consequence | How it was handled |
|---|---|---|
| `AesGcm` | No AES-GCM | Encrypt-then-MAC with AES-256-CBC (§2 of SECURITY.md) |
| `ToggleSwitch` | No WPF switch | `CheckBox` with a replacement slide template |
| `FocusVisualStyle` | No focus adornments | Focus ring drawn by each control's own template |
| `FrameworkElementFactory.SetBinding` | No bound templates | Row templates authored as XAML and parsed at runtime |
| `TextBlock.TextCharacterSpacing` | No tracking | Letter-spacing dropped from the type scale |
| `TextBox.PasswordChar` | No masked `TextBox` | `SecretInput`: a `PasswordBox` beside a hidden `TextBox`, swapped to reveal |
| MSBuild / XAML | No `.baml` | Templates parsed from XAML strings at startup |

The build pins `/langversion:5` so this constraint cannot be broken silently by a future toolchain.

### Cross-platform

This build is **Windows-only**. The brief asked for a cross-platform product; that goal was not
met, and the honest reason is the one above: Tauri and Electron were not buildable here within any
reasonable time. The domain layer (`Keyward.Core`) has no UI or Windows dependency beyond the file
paths and ACLs in `AppPaths`, so a macOS or Linux shell is a realistic follow-up, but it has not
been built or tested.

---

## Competitor research

Reviewed for pattern and interaction reference. No competitor was copied, and no claims are made
about their internals beyond their publicly documented behaviour.

| Product | Pattern worth learning | Where Keyward differs |
|---|---|---|
| **Bitwarden** | Clear three-region vault layout; browser/desktop parity | Bitwarden is cloud-first with an account; Keyward is local-only with no account at all |
| **1Password** | Excellent use of a single accent colour and generous whitespace; strong detail-pane hierarchy | 1Password uses a subscription and sync; Keyward has neither |
| **Proton Pass** | Privacy-first positioning is stated plainly, not implied | Proton Pass is account-based; Keyward's equivalent claim is that the file never leaves the disk |
| **KeePassXC** | Genuine offline-first model, plugin KDF agility | KeePassXC's breadth (plugins, drivers, databases) is far larger; Keyward is a focused single-file tool |
| **Dashlane** | Useful reuse/safety warnings surfaced inline | Keyward warns on password reuse and weak passwords but does not have a breach-monitoring service |

**Opportunities that shaped the design:**

1. *Every* competitor asks you to create an account. A password manager that stores your passwords
   is the last place an account is warranted. Keyward has no account, and the first-run screen
   says so plainly.
2. Competitors rarely state recovery limits at the moment they matter. Keyward states them on first
   run, before a password is chosen, because after that it is too late to inform the decision.
3. Blue and indigo are overused as the "security" accent. Keyward uses a brass accent — the
   association of a vault door, without the shield-and-padlock cliché.

**Common usability problems avoided:** secrets visible by default, copy actions with no feedback,
destructive actions behind a single click, auto-lock with no visible state, and errors that report
failure vaguely. Keyward's auto-lock shows a live countdown in the title bar, every destructive
action is confirmed, and clipboard operations report what actually happened.

---

## Sources

Verified during development, not recalled:

- Electron security checklist — <https://electronjs.org/docs/latest/tutorial/security>
- Electron process sandboxing — <https://electronjs.org/docs/latest/tutorial/sandbox>
- Electron context isolation — <https://electronjs.org/docs/latest/tutorial/context-isolation>
- OWASP Password Storage Cheat Sheet — <https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html>
- NIST SP 800-63B (digital identity guidelines) — <https://pages.nist.gov/800-63-3/sp800-63b.html>

PBKDF2 timing figures in `SECURITY.md` were measured on this machine with `tools/Bench.cs`, not
quoted from a reference.
