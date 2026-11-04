# Edda — a level editor for Ragnarock VR

<img src="https://img.shields.io/github/downloads/watsu78/Edda/total">

**Latest releases**  
| **Stable** | <img src="https://img.shields.io/github/v/release/watsu78/Edda"> | <img src="https://img.shields.io/github/downloads/watsu78/Edda/latest/total"> |
|---|:---|---|
| **Beta** | <img src="https://img.shields.io/github/v/release/watsu78/Edda?include_prereleases"> | <img src="https://img.shields.io/github/downloads-pre/watsu78/Edda/latest/total"> |

<hr/>

Edda is an editor that lets you map custom songs to levels for the VR rhythm game [Ragnarock](https://www.ragnarock-vr.com/home).

This is a fork of the original EDDA projet created by PKBeam right [here](https://github.com/PKBeam/Edda).

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

## RagnaCustoms upload

Open **Tools > Settings > RagnaCustoms > Manage API key** to save, replace, or remove your RagnaCustoms API key. The dialog links to your RagnaCustoms account, where you can obtain the key. Edda stores it in Windows Credential Manager for the current Windows user, separately from settings and map files.

Edda checks the saved account using `GET /api/account/me` and displays its username, Premium tier and expiration date. You can refresh this information with **Check account**. Before preparing an upload, Edda checks the account again and requires `isPremium: true` and `premiumTier` of at least `2`. A missing or unknown tier does not grant upload eligibility; the upload API also checks access on the server.

Open a map and choose **File > Upload to RagnaCustoms**. Clicking **Upload map** saves the current map and sends a ZIP with all difficulties, audio, cover and optional preview. Each upload creates a **new WIP map**; review it on RagnaCustoms before publishing. Upload requires **Premium Tier 2 or higher**.

The client uses the production API at `https://api.ragnacustoms.com/api`: `GET /account/me` checks the account, and `POST /upload/song` uploads the ZIP in the multipart `file` field. Both requests authenticate through `X-API-Key`. Uploads can be cancelled. Edda does not automatically retry uploads; after a timeout or interrupted connection, check your account before trying again to avoid creating duplicate maps.

Run the integration checks on Windows with:

```powershell
dotnet run --project Tests/RagnaCustomsChecks/RagnaCustomsChecks.csproj
```

These checks verify ZIP contents, audio offsets, account membership, multipart requests, API errors and cancellation using simulated HTTP responses. They also create and remove a uniquely named test credential; they do not access your saved RagnaCustoms key or call the RagnaCustoms API.
