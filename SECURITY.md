# Security policy

## Supported versions

Sherpa Manager is currently an early-stage project. Security fixes are applied to the latest version on the default branch; older builds are not supported.

## Reporting a vulnerability

Please do not disclose a suspected vulnerability in a public issue. Use the repository's **Security → Report a vulnerability** feature to submit a private GitHub Security Advisory.

Include the affected version, reproduction steps, expected impact, and any suggested mitigation. Please avoid accessing data that is not yours while investigating.

## Scope

Sherpa Manager runs local applications and changes Windows display configuration at the user's request. Profiles are stored locally in `%APPDATA%\SherpaManager`. The application does not include telemetry, accounts, or network services.
