# Contributing to Claupet

Thank you for taking the time to contribute to **Claupet**! 🐾  
Whether you are fixing a bug, adding support for a new AI CLI tool, creating a new pixel-art pet, or improving documentation, all contributions are welcome.

---

## 🚀 Quick Start (Development Environment)

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Any supported AI CLI tool configured on your system (Claude Code, Gemini CLI, Codex CLI, GitHub Copilot CLI, OpenCode, etc.)
- Python 3 (optional, only required for building release packages)

### Setup & Run
```powershell
# Clone the repository
git clone [https://github.com/darrenarauz21/claupet.git](https://github.com/darrenarauz21/claupet.git)
cd claupet

# Run the app locally (Windows, Linux, macOS)
dotnet run --project Claupet.Avalonia

# Run tests (Windows)
dotnet test Claupet.Tests

# Build self-contained packages (optional)
python herramientas/empaquetar.py
