# Validation matrix for stable 1.0.0 / v1.0.3 development

The CLI must compile on `windows-2025` with .NET 10 before testing.

| Scenario | Expected outcome | Checked |
|---|---|---|
| Windows workstation without IIS/Apache | `install` makes no changes, does not delete cache, does not create log | [ ] |
| IIS only, W3SVC stopped | Adds rule without starting W3SVC | [ ] |
| IIS native mode, URL Rewrite absent | `install --iis-mode native` adds named Request Filtering rule without MSI | [x] 2026-10-28 BASE-IMAGE: IIS Started, named native filter installed with no MSI download |
| IIS native mode, Babylon translation | Babylon translates after native filtering was installed with IIS running | [x] 2026-10-28 BASE-IMAGE: user confirmed translation works. Brief initial error page with red text appeared before normal Babylon window |
| Default IIS mode with no MSI | Plain `install` chooses native filtering without a URL Rewrite download | [x] BASE-IMAGE 2026-10-28 09:30 and 09:34: no-argument EXE installed native rule; no MSI download; IIS remained Started on TCP/80 |
| Migration native -> rewrite | Explicit `--iis-mode rewrite` installs Microsoft URL Rewrite and the Babylon rule, then removes the managed native IIS rule | [x] BASE-IMAGE 2026-10-28: first run user confirmed translation; repeated 09:33 test MSI exit=0, Rewrite rule installed, native rule removed, IIS Started, Babylon restarted. Failure rollback untested |
| Migration rewrite -> native | Plain `install` installs native Request Filtering, then removes only Babylon's managed URL Rewrite rule | [x] BASE-IMAGE 2026-10-28 09:34: native rule installed, old Rewrite rule removed, migration INFO logged, IIS remained Started, cache failures=0, Babylon restarted; translation after this particular run not independently shown |
| Repeated native install | No IIS backup/changes when the native Babylon rule already matches | [ ] Needs workstation retest |
| IIS native mode, website | Ordinary `http://localhost/` works; Babylon query denied with 404.19 | [ ] Needs live HTTP test |
| `remove-iis` | Removes only managed IIS rules, not Apache/cache | [ ] Needs integration test; parser covered by smoke tests |
| IIS missing RewriteModule, MSI stored beside the EXE | Use signed local MSI without contacting Microsoft URL | [ ] Source precedence covered by offline smoke test; target-machine test pending |
| IIS missing RewriteModule, custom official URL | Use `--msi-url` with Microsoft HTTPS direct MSI | [ ] URL validation unit-tested; network installation with new URL not yet verified |
| IIS missing RewriteModule, default Microsoft URL unavailable | Fail with offline UNC instructions, never silently proceed | [ ] Error path logic reviewed; target-machine network failure not reproduced |
| IIS already has newer RewriteModule | Skip download and upgrade; apply compatible rule | [ ] Future version compatibility not yet tested |
| Rewrite dry-run with native rule present | Detect registered RewriteModule, preview Rewrite rule creation and native rule deletion without changing IIS | [x] 2026-10-28 BASE-IMAGE v1.0.4: IIS Started, TCP/80, showed "Would install IIS rule" and "Would remove IIS native Babylon rule", DONE. No configuration changes made |
| Previously installed RewriteModule reused | Existing registered RewriteModule is detected with managed IIS API or read-only appcmd fallback, avoiding repeated MSI download | [x] 2026-10-28 BASE-IMAGE v1.0.4 `install --iis-mode rewrite --dry-run`: log confirmed "IIS RewriteModule is already registered; reusing existing module without MSI download." Only read-only dry-run verified; appcmd fallback mismatch and real install paths remain untested |
| IIS missing URL Rewrite, IIS/W3SVC running | Validate the official Microsoft MSI via native WinTrust, install URL Rewrite 2.1 and create the Babylon rule | [x] 2026-10-28 BASE-IMAGE: W3SVC Started, MSI exit=0, rule installed, DONE. EXE SHA-256 matched published v0.1.3-preview.1 Release asset |
| Downloaded URL Rewrite MSI cleanup | Automatically delete only the downloaded temporary MSI after installation | [ ] Code attempts cleanup in finally; file absence not confirmed in test log |
| Unsigned or spoofed MSI | Reject without installation | [x] Windows CI negative signature test; spoofed subject covered by unit tests |
| IIS already has Babylon rule v3/v10 | Idempotent, no duplicate, no unneeded changes | [ ] |
| IIS running with local port 80 binding | Apply Babylon rewrite rule without stopping IIS or altering its binding | [x] 2026-10-28 BASE-IMAGE log: IIS state Started, http *:80:, rule installed |
| Babylon translates with IIS active | Babylon Pro 12 translation works after URL Rewrite setup with IIS running on port 80 | [x] 2026-10-28 BASE-IMAGE: user confirmed translation works; exact release EXE verified by SHA-256 |
| IIS only, local port 80 website | Normal `/` responds correctly while Babylon also translates | [ ] Translation confirmed, ordinary `http://localhost/` response with IIS active not separately verified |
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

**Stable release scope:** the verified workflow covers Babylon Pro 12 with an active IIS site or XAMPP console Apache on a Windows test workstation. Test other scenarios locally before mass deployment. **Do not distribute broadly until items relevant to the deployment are tested.** A 2026-10-28 BASE-IMAGE run reported success for the C# IIS URL Rewrite installation path while W3SVC was started, including MSI exit code 0, rule installation, Babylon process restart and WinINet cache cleanup without errors. Translation with IIS active was subsequently confirmed by the user; the deployed EXE's SHA-256 exactly matches the public v0.1.3 Preview release. Both directions of switching between the two Babylon IIS rule types succeeded on BASE-IMAGE at 09:33 and 09:34; the translated output after the latest 09:34 run was not independently shown. A normal IIS `http://localhost/` response, downloaded MSI file deletion, and the cause of the original 09:33 MSI redownload remain unverified. A later v1.0.4 dry-run on BASE-IMAGE correctly detected the already-registered RewriteModule and skipped the installer without changing IIS. Apache and Babylon translation were observed together on another workstation test; Apache HTTP 403 behavior and repeat-install idempotence still require targeted workstation validation.
