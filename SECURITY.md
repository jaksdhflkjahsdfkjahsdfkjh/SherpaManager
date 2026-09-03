# Security policy

## Supported versions

Sherpa Manager is currently an early-stage project. Security fixes are applied to the latest version on the default branch; older builds are not supported.

## Reporting a vulnerability

Please do not disclose a suspected vulnerability in a public issue. Use the repository's **Security → Report a vulnerability** feature to submit a private GitHub Security Advisory.

Include the affected version, reproduction steps, expected impact, and any suggested mitigation. Please avoid accessing data that is not yours while investigating.

## Scope

Sherpa Manager runs and optionally terminates local applications, calls the Windows display configuration API, and can validate and restore a captured NVIDIA Surround display grid at the user's request. **Close on switch** is enabled by default for newly added applications. When that option is enabled, Sherpa first requests a normal exit and automatically force-terminates the matching process tree if it remains running; users can disable the option for applications that may contain unsaved work. Implicit executable matching uses canonical paths; an explicitly entered process-name override is intentionally broader. Profiles and automatic recovery snapshots are stored locally in `%APPDATA%\SherpaManager`. The application does not include telemetry, accounts, or network services.
