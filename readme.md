<div align="center">

# <img src="JCMU.ConsoleBed/Icons/jinn.ico" width="32" align="center" /> Jinn Context Menu Utility (JCMU)

**Take back absolute control of your Windows Right-Click menu.**

[![Platform Windows](https://img.shields.io/badge/Platform-Windows_10%20|%2011-blue?logo=windows)](#)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](#)
[![License MIT](https://img.shields.io/badge/License-MIT-green.svg)](#)

</div>

---

## 🛑 The Problem
Ever wanted to right-click an empty folder and just click **"Initialize Git Repository"**? Or **"Scaffold .NET Web API"**? Or **"Convert to MP4"**?

Historically, adding things to the Windows Context Menu meant carefully hacking the `regedit` (and hoping you didn't break your OS), or writing complex C++ COM Shell Extensions. It was a miserable experience.

## ✨ The Solution
**JCMU** is a lightweight, blazing-fast engine that injects a beautiful, organized `JinnCM` menu into your Windows Right-Click menu. 

But it's not just a menu—it's a **Decentralized App Store**. Using the built-in JCMU Command Line, you can search GitHub for community-made addons, install them with a single keystroke, and have them instantly appear in your right-click menu. 

### Why you'll love it:
* 📦 **Built-in App Store:** Just type `search` to find new tools directly from GitHub.
* ⚡ **Instant Integration:** Install an addon and it immediately appears in your right-click menu. No reboots required.
* 🛡️ **Zero "DLL Hell":** Addons run in strictly isolated memory spaces. They can't break each other, and they can't break the Core.
* 🧹 **Clean Removal:** Typing `uninstall` completely eradicates the addon and scrubs the Windows Registry clean. No orphaned garbage left behind.
* 🔒 **Secure by Default:** Features a strict `Trust` system. Code doesn't compile or install on your machine unless you explicitly whitelist the author.
* 📝 **Full Traceability:** Integrated persistent logging ensures that even background tasks are never a "black box" if something goes wrong.

---

## 🚀 Getting Started

1. Download the latest `JCMU_Installer.exe` from the [Releases](#) page.
2. Run the installer to unpack the core engine.  This also initializes the system (see init and teardown)

*(That's it! If you right-click your desktop right now, you'll see a brand new **JinnCM** menu at the bottom with a built-in search button!)*

---

## 🎮 Using the Package Manager

JCMU comes with an interactive REPL (Command Line) to manage your tools. You can launch it by right-clicking any folder and selecting **"Search for Addons..."**, or just by typing `jcmu` in your terminal.

#### 1. Find cool tools
Search the global GitHub ecosystem for any repository tagged with `jcmu-addon`:
```shell
JCMU> search log
```
```text
--- Searching GitHub for 'blamo' (Page 1) ---
[1] FakeGitUser/Blamo.NeighborsDog [TRUSTED]
    https://github.com/FakeGitUser/Blamo.NeighborsDog
    It rolls down stairs, alone or in pairs, it's Log, Log, Log!
```

#### 2. Trust the Author
To protect your machine from total chaos, you must trust a developer before installing their code:
```shell
JCMU> trust SomeGitUserName
```

#### 3. Install it
Just type `install` and the number from your search results:
```shell
JCMU> install 1
```
*(JCMU downloads the raw source, securely compiles it on your machine, locks the binaries in ProgramData, and automatically stitches the registry keys together. **Blamo!** The tool is now sitting in your right-click menu.)*

#### 4. Clean up
Don't need it anymore? See exactly what you have installed and purge the weak:
```shell
JCMU> list
```
```text
--- Installed JCMU Addons ---
[1] FakeGitUser/Blamo.NeighborsDog
    https://github.com/FakeGitUser/Blamo.NeighborsDog

[2] SomeOtherUser/JCMU.OldTool
    https://github.com/SomeOtherUser/JCMU.OldTool
```
```shell
JCMU> uninstall 1
```
*(This cleans up the files, releases the locks, and scrubs the Windows Registry. It's like it was never there.)*

```shell
JCMU> dev link [path]          # Create a local junction for active development.
JCMU> dev unlink <AddonId>     # Remove a developer link and its registry hooks.
```

---

## 🛠️ For Developers: Build Your Own Addon!

Can't find the exact tool you need? Building a JCMU Addon is incredibly easy. You don't need to know anything about the Windows Registry or OS APIs. 

If you know basic C# and can write a simple `manifest.json`, you can have a custom right-click tool running in under 5 minutes.

> **Pro Tip:** Use `dev link` to map your project's folder directly into the JCMU engine so you can test code changes instantly without re-installing.

👉 **[Check out the JCMU SDK Documentation to get started!](https://github.com/JinnFletch/JCMU.SDK/blob/main/readme.md)**
