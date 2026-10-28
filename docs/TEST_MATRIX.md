# Validation matrix for preview 0.1.0

The CLI must compile on `windows-2025` with .NET 10 before testing.

| Scenario | Expected outcome | Checked |
|---|---|---|
| Windows workstation without IIS/Apache | `install` makes no changes, does not delete cache, does not create log | [ ] |
| IIS only, W3SVC stopped | Adds rule without starting W3SVC | [ ] |
| IIS already has Babylon rule v3/v10 | Idempotent, no duplicate, no unneeded changes | [ ] |
| IIS only, local port 80 website | Normal `/` works; Babylon interface translates | [ ] |
| XAMPP Apache 2.4 running on port 80, not service | Adds Apache snippet, `httpd -t`, restarts using XAMPP batch files | [ ] |
| Apache only, Babylon actually translates | Confirm HTTP 403 compatibility empirically | [ ] |
| Apache stopped before `install` | Remains stopped | [ ] |
| IIS and Apache installed, one active | Configures only the intended rules, avoids port changes | [ ] |
| Multiple Apache installations | Selects actual unique port-80 instance or refuses ambiguous choice | [ ] |
| XAMPP paths differing by case | Recognized as the same executable | [ ] |
| Student Babylon process was running | Restarted in same session and non-elevated token | [ ] |
| Student Babylon was not running | Remains stopped | [ ] |
| SYSTEM / Session 0 invocation | No XAMPP batch restart; no unintended interactive sessions | [ ] |
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

**Do not distribute until items relevant to the deployment are tested.** The IIS rewrite was previously observed working in PowerShell; the C# port requires new validation. Apache handling remains experimental.
