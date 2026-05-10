# Security Policy

## Supported Versions

Security and bug fixes are generally only provided for the latest version of QuickTickLib. Users are encouraged to upgrade to the latest release to receive all security-related updates.

## Reporting a Vulnerability

Please use **GitHub's private vulnerability reporting** feature to report security issues. Do not open a public issue.

To report:
1. Go to the **Security** tab of this repository
2. Click **Report a vulnerability**
3. Fill in the private disclosure form

This ensures the issue is handled confidentially and a fix can be coordinated before any public disclosure.

**Do not** open a public issue or disclose the vulnerability publicly before a fix has been released.

## Response

While specific response times cannot be guaranteed, confirmed security issues will be treated as a priority.

## Scope

The following are considered in scope:

- Vulnerabilities within the library's own code, such as unsafe resource management, incorrect platform-level API behavior, or improper handle/memory lifecycle

The following are **out of scope**:

- Vulnerabilities in the underlying OS or .NET runtime
- Issues caused entirely by external systems or platform limitations outside of this project's control
