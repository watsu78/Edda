# Edda — a level editor for Ragnarock VR

<img src="https://img.shields.io/github/downloads/PKBeam/Edda/total">

**Latest releases**  
| **Stable** | <img src="https://img.shields.io/github/v/release/PKBeam/Edda"> | <img src="https://img.shields.io/github/downloads/PKBeam/Edda/latest/total"> |
|---|:---|---|
| **Beta** | <img src="https://img.shields.io/github/v/release/PKBeam/Edda?include_prereleases"> | <img src="https://img.shields.io/github/downloads-pre/PKBeam/Edda/latest/total"> |

<hr/>

Edda is an editor that lets you map custom songs to levels for the VR rhythm game [Ragnarock](https://www.ragnarock-vr.com/home).

User documentation (such as installation and usage instructions) can be found at https://pkbeam.github.io/Edda/.

If you are interested in contributing to the source code, partial developer documentation can be found [here](https://github.com/PKBeam/Edda/wiki).

<br/>
<p align="left"><img height="auto" width="750px" src="https://i.imgur.com/6e8nAVo.png"></p>

## Development Quick Start

- Prerequisite: Install .NET SDK 8.0 for Windows (x64). Download from https://aka.ms/dotnet/download or via Winget:

```powershell
winget install Microsoft.DotNet.SDK.8
```

- Restore and build:

```powershell
cd i:\EddaTestCopilot
dotnet restore
dotnet build --configuration Debug
```

- Run the app:

```powershell
dotnet run --project RagnarockEditor.csproj --configuration Debug
```

In VS Code, you can also use the task "Run Edda (Debug)" to start the app once the .NET 8 SDK is installed.
