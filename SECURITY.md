# Security Policy

## Privacy & Security Model

Claupet is designed to be **private by default**:
- It processes session logs and state metrics directly from local system files created by your AI CLI tools.
- It does not store passwords, API keys, or your conversation histories.
- Local voice models (Whisper, Supertonic) and diagnostics run on your computer.

### 🔮 Future Security Roadmap
- **Secure Key Vault (In Development):** A planned local, encrypted secret manager to securely store credentials. Instead of exposing raw keys/passwords to the AI models, Claupet will manage, copy, and paste credentials securely when needed.

## Supported Versions

Only the latest release of Claupet receives active security updates and patches.

| Version | Supported |
| ------- | ------------------ |
| Latest release (main branch) | :white_check_mark: |
| Older releases | :x: |

## Reporting a Vulnerability

If you discover a potential security vulnerability within Claupet, please report it responsibly rather than opening a public GitHub issue.

### How to report
Send an email to **darren.arauz@gmail.com** with:
- A description of the vulnerability and its potential impact.
- Steps to reproduce the issue (or proof-of-concept code/configuration).
- Any relevant context regarding your OS or CLI environment.

### What to expect
- **Acknowledgment:** You will receive a response acknowledging receipt of your report within 48 hours.
- **Assessment & Status:** Updates regarding the investigation and fix progress will be provided as work proceeds.
- **Disclosure:** Once a fix is released, public credit will be given in the release notes (unless you prefer to remain anonymous).
