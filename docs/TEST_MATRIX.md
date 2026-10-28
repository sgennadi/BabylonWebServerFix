# Validation matrix for preview 0.1.3

The CLI must compile on `windows-2025` with .NET 10 before testing.

| Scenario | Expected outcome | Checked |
|---|---|---|
| Windows workstation without IIS/Apache | `install` makes no changes, does not delete cache, does not create log | [ ] |
| IIS only, W3SVC stopped | Adds rule without starting W3SVC | [ ] |
| IIS missing URL Rewrite, IIS/W3SVC running | Download official Microsoft MSI, validate trusted publisher through native WinTrust, install URL Rewrite 2.1 and create Babylon rule | [x] 2026-10-28 BASE-IMAGE log: W3SVC Started, MSI exit=0, rule installed, DONE; exact executable build/hash not recorded |
| Downloaded URL Rewrite MSI cleanup | Automatically delete only the downloaded temporary MSI after installation | [ ] Code attempts cleanup in finally; file absence not confirmed in test log |
| Unsigned or spoofed MSI | Reject without installation | [x] Windows CI negative signature test; spoofed subject covered by unit tests |
| IIS already has Babylon rule v3/v10 | Idempotent, no duplicate, no unneeded changes | [ ] |
| IIS running with local port 80 binding | Apply Babylon rewrite rule without stopping IIS or altering its binding | [x] 2026-10-28 BASE-IMAGE log: IIS state Started, http *:80:, rule installed |
| IIS only, local port 80 website | Normal `/` works; Babylon interface translates | [ ] Babylon restart succeeded, but translation and normal HTTP response with IIS running are not shown in the console log |
| XAMPP Apache 2.4 running on port 80, not service | Adds Apache snippet, `httpd -t`, direct process restart, TCP/80 listening | [x] Test workstation log |
| Apache active on TCP/80, Babylon actually translates | Babylon Pro 12 translates while IIS is stopped and XAMPP Apache is running; HTTP 403 itself still needs verification | [x] Test workstation screenshot + log |
| Apache stopped before `install` | Remains stopped | [ ] |
| Repeated `install`, Apache rule unchanged | Apache is not restarted; explicit `restart-apache` still supported | [x] Policy smoke tests; workstation retest pending |
| IIS and Apache installed, one active | Configures only the intended rules, avoids port changes | [ ] |
| Multiple Apache installations | Selects actual unique port-80 instance or refuses ambiguous choice | [ ] |
| XAMPP paths differing by case | Recognized as the same executable | [x] Smoke tests and workstation log |
| Student Babylon process was running | Restarted in same session; non-elevated token not independently inspected | [x] Session/log; token unverified |
| Student Babylon was not running | Remains stopped | [ ] |
| SYSTEM / Session 0 invocation | No XAMPP console restart; no unintended interactive sessions | [ ] |
| User cache file locked | Warns, does not delete cookies/passwords; program continues | [ ] |
| Service accounts SQL Server present | Skips `Windows\\ServiceProfiles` | [ ] |
| Default logging | No file/directory created | [ ] |
| `--enable-log` | Creates appropriate log only with flag | [ ] |
| Default next-login cache cleanup | Not registered | [ ] |
| `--cleanup-next-logon` | Registers Active Setup; `remove` undoes it | [ ] |
| `--dry-run` | No file, registry, service, process or task changes | [ ] |
| `--skip-cache` | Skips cache cleanup and still handles optional Babylon restart | [ ] |
| `remove` | Only managed rules / Active Setup removed | [ ] |
| Unsigned build SmartScreen behavior | Confirm deployment policies allow the exe, or sign release | [ ] |

**Do not distribute until items relevant to the deployment are tested.** A 2026-10-28 BASE-IMAGE run reported success for the C# IIS URL Rewrite installation path while W3SVC was started, including MSI exit code 0, rule installation, Babylon process restart and WinINet cache cleanup without errors. This does **not** by itself confirm actual translation with IIS active, a normal IIS `http://localhost/` response, downloaded MSI file deletion, or the precise deployed EXE hash. Apache and Babylon translation were observed together on another workstation test; Apache HTTP 403 behavior and repeat-install idempotence still require targeted workstation validation.
