# Security architecture and limitations

This document describes what Keyward does to protect your vault, and — at least as importantly —
what it does not protect against. Claims here are backed by the test suite in `tests/`; where
something is unverified it is said so.

---

## 1. Threat model

### What Keyward is designed to defend against

| Threat | Defence |
|---|---|
| Someone reads the vault file from a stolen laptop or backup | AES-256-CBC encryption at rest; the file is opaque without the master password |
| Another local user reads the vault file | Owner-only NTFS ACL applied on create and after every write; inheritance broken so later parent-directory changes cannot grant access |
| Someone tampers with the vault file | Encrypt-then-MAC with HMAC-SHA-256; any modification is detected and rejected |
| An attacker downgrades the KDF to make guessing cheap | Iteration count, salt, format version and cipher name are all bound into the authentication tag |
| A weak master password | Live strength guidance on first run; a minimum of 10 characters is enforced |
| Predictable generated passwords | `RandomNumberGenerator` only, with rejection sampling so there is no modulo bias |
| Casual shoulder-surfing after copying | Clipboard auto-clear, configurable 10 s – 5 min |
| Forgetting to lock | Idle auto-lock (configurable 1 min – 1 hour), lock-on-sleep, lock-on-exit |
| An attacker timing password verification | Constant-time comparison (`FixedTimeEquals`) |

### What Keyward explicitly does NOT defend against

These are limitations of any local password manager, and are listed so nobody is misled:

- **Malware or a compromised operating system.** Keyward holds decrypted data in process memory
  while unlocked. Anything that can read the process — a keylogger, an injector, a debugger, or
  simply control of the machine while the vault is open — can read your passwords. No local
  design avoids this; it is why auto-lock exists.
- **A full-memory dump, hibernation file, or crash report.** `SecretBytes` pins and zeroes key
  buffers, which removes garbage-collector risks, but it cannot evict data that has already been
  paged to disk. Swapping is not disabled (that would require system-level privileges).
- **A weak master password.** Encryption strength is entirely the user's password. A 4-character
  password is brute-forceable regardless of how well the file is encrypted.
- **GPU/ASIC attacks on PBKDF2.** See §3.
- **Clipboard history.** Windows keeps its own clipboard history and other applications may cache
  it. Keyward clears the live clipboard; it cannot reach those.
- **Screen capture, keyloggers, or a camera pointed at your screen.**
- **A determined attacker with your password and your vault file.**

---

## 2. Cryptographic design

### Primitives

| Purpose | Choice | Notes |
|---|---|---|
| KDF | PBKDF2-HMAC-SHA512 | `System.Security.Cryptography`, 210,000 iterations |
| Salt | 32 random bytes per vault | `RandomNumberGenerator` |
| Encryption | AES-256-CBC, PKCS#7 | 128-bit random IV per save |
| Integrity | HMAC-SHA-256 | Encrypt-then-MAC over (associated data ‖ IV ‖ ciphertext) |
| Randomness | `RandomNumberGenerator` | Never `System.Random` |

No cryptographic primitive is hand-rolled. Everything comes from the .NET cryptography stack.

### Why encrypt-then-MAC rather than AES-GCM

`System.Security.Cryptography.AesGcm` exists only from .NET Core 3.0 / .NET 5. This application
targets .NET Framework 4.8, where it does not exist. Rather than implement GCM by hand — which
would be reckless in a security product — Keyward uses the classic encrypt-then-MAC composition,
which is a standard, well-understood AEAD-equivalent. The MAC is verified **before** any
decryption is attempted, so unauthenticated plaintext is never processed and padding-oracle
behaviour is not reachable.

### Key separation

PBKDF2 output (64 bytes) is split into two independent keys:

```
bytes  0..31  ->  AES-256 encryption key
bytes 32..63  ->  HMAC-SHA-256 authentication key
```

Using one key for two primitives is never safe. `MasterKey` validates both lengths at construction
and a test asserts the two sub-keys are never equal.

### Authenticated data

The MAC covers a canonical, length-prefixed encoding of:

```
format marker ‖ version ‖ cipher name ‖ KDF algorithm ‖ iterations ‖ salt
```

This binds the envelope metadata into the tag. An attacker who edits `iterations` from 210,000 to
1,000, or swaps the salt, produces a tag that no longer verifies — so the vault is rejected rather
than silently derived with attacker-chosen parameters. Tests cover both the iteration and salt
downgrade attacks.

### Vault file format

```json
{
  "format": "keyward-vault",
  "version": 2,
  "updatedUtc": "...",
  "kdf":    { "algorithm": "PBKDF2-HMAC-SHA512", "iterations": 210000, "salt": "base64" },
  "cipher": { "algorithm": "AES-256-CBC+HMAC-SHA256",
              "iv": "base64", "ciphertext": "base64", "mac": "base64" }
}
```

Only `cipher.ciphertext` contains vault data. Everything else is metadata needed to decrypt it.
Versioning is in place so future formats can be migrated; `version` is authenticated, so it cannot
be forged downwards.

### Key lifecycle

1. The master password is converted to UTF-8 and that buffer is zeroed as soon as derivation ends.
2. The PBKDF2 output buffer is zeroed once the two sub-keys have been split out.
3. `SecretBytes` pins each sub-key and zeroes it on `Dispose`.
4. Locking disposes `MasterKey`, which zeroes both sub-keys, then scrubs every password and note
   body in the decrypted document before releasing it.
5. `MasterKey` throws if used after disposal, so a stale reference fails loudly rather than
   silently encrypting with zeroed keys.

### Failure behaviour

A wrong master password and a tampered file produce **the same** failure (`WrongPasswordOrTampered`)
with the same message. An attacker who could tell them apart would learn something useful, and
there is nothing the user could do about the second case anyway.

---

## 3. Choosing the iteration count

The default of 210,000 is the OWASP Password Storage Cheat Sheet figure for PBKDF2-HMAC-SHA512.
It was chosen from a measurement on the target machine, not copied blindly:

| Configuration | Measured cost |
|---|---|
| PBKDF2-HMAC-SHA512, 210,000 iterations | **~3.6 s** (chosen) |
| PBKDF2-HMAC-SHA512, 600,000 iterations | ~9.8 s |
| PBKDF2-HMAC-SHA256, 600,000 iterations | ~8.8 s |
| PBKDF2-HMAC-SHA512, 100,000 iterations | ~1.4 s |

A ten-second unlock was rejected: it pushes users toward leaving the vault unlocked, which is a
worse trade than the extra iterations buy. A floor of 100,000 iterations is enforced when opening
a vault so the work factor cannot be reduced by editing the file.

**Known weakness.** PBKDF2 is memory-light and therefore highly parallelisable; GPUs and ASICs
attack it far faster than a general-purpose CPU. Argon2id would be the better choice because its
memory hardness punishes exactly that. Argon2 was not used here because this machine has no
package feed and the machine-readable Argon2id implementation in `System.Security.Cryptography`
does not exist in .NET Framework 4.8. The KDF envelope is versioned and the algorithm name is
authenticated, so migrating to Argon2id is a contained change: add an algorithm identifier, read
the parameters, derive with Argon2id, re-encrypt.

---

## 4. In-memory handling during search

Search matches names, usernames, URLs, tags and note previews. Because the data is only decrypted
in memory, **the search necessarily runs over plaintext**. Consequences, all deliberate:

- Decrypted values are never written to the log file, a crash report, or any index.
- The process holds plaintext for as long as the vault is unlocked; auto-lock bounds that window.
- There is no OS-level search index or telemetry to leak into.
- The clipboard is the only channel out of the process, and it is cleared on a timer.

---

## 5. Clipboard

Copying a secret reports what actually happened:

- If the clipboard could not be written, the toast says so instead of claiming success.
- On clear, Keyward only clears the clipboard if it still holds the value it put there, so it
  cannot silently wipe something the user copied afterwards.
- If the clear fails, the user is told. Keyward does not claim a clipboard was cleared when it
  was not.

---

## 6. Audit status

**Keyward has never been independently audited, penetration tested, or certified.** No third party
has reviewed this code. The cryptography uses established algorithms from the .NET library and is
covered by 86 automated tests, including tamper detection, KDF downgrade resistance, nonce
freshness, and constant-time comparison — but a test suite is not an audit, and passing tests do
not prove the absence of defects.

Treat Keyward as carefully written software, not as a guarantee.
