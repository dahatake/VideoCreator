# VideoCreator

<!-- language-nav -->
[日本語](../../README.md) | [English](start.md) | [한국어](../ko/start.md) | [简体中文](../zh-CN/start.md)
<!-- /language-nav -->

[Start](start.md) / [Guide index](README.md) / [Detailed overview](overview.md) / [For engineers](technical-reference.md)

**Turn an internal video into a short subtitled video, even if you have never edited video.**
Choose footage on your PC, select an editing pattern, review and revise candidates, then save. The app does not automatically post to social media or internal sites.

## Prepare first

- **One video you may use:** a few minutes of speech in an MP4 or another supported format. Check permission for people, slides and music, and approval to send data to external services.
- **A Windows 10/11 PC, Internet and free storage:** first launch installs required tools. Follow your company's installation rules.
- **An administrator handoff:** confirm the account, Speech endpoint and permissions, prepared settings file, billing owner and support contact. Do not guess missing endpoints; ask your administrator.

> **Local does not mean fully offline.** Normally audio goes to Azure Speech (default model: MAI-Transcribe-2); AI features also send transcripts to the configured service. Disabling the LLM does not disable Speech traffic. Never paste keys or tokens into documentation or support messages.

## Your first video

1. Follow the [quickstart](quickstart.md) to prepare with your administrator and launch the approved package/settings. For the source edition, double-click `run.cmd` in the extracted folder.
2. Create a project in the browser, add one video and confirm usage rights.
3. Choose **P4 (long video → 30-second clip)**, series off and standard filtering. Check destinations/costs, then generate.
4. Play a candidate; check subtitles, meaning, framing and sound. Approve and export.
5. Play the entire finished MP4. **Opening the saved MP4 and subtitles from their output folder is your first success.** Post manually under company policy.

![Preparation, operation and deliverables](images/overview.svg)

If something fails, do not repeatedly regenerate: open [troubleshooting](troubleshooting.md). `FAIL` indicates a local prerequisite failure; `WARN` is a warning. A working browser page does not prove transcription is ready. Give support the time, operation, error code and app version, without attaching footage or confidential subtitles.

## Find the right help

| Goal | Read |
|---|---|
| Follow preparation through saving | [Quickstart](quickstart.md) |
| Find terms, buttons and operations | [Guide index](README.md) / [Screens](screens.md) |
| Errors, warnings and backup | [Troubleshooting](troubleshooting.md) |
| Detailed features, packaged edition and original setup | [Detailed overview](overview.md) |
| Customize safely as an engineer | [Technical reference](technical-reference.md) / [Configuration/API](configuration-api.md) |
| Azure setup, shared users, key / Entra ID authentication | [Azure access](azure-access.md) |
| Verification, builds and release details | [Verification](verification.md) / [Detailed overview](overview.md) |

Display language and video language are independent. Subtitles are not automatically translated. Generate new candidates after changing video language. The [detailed overview](overview.md) retains the full feature, distribution, versioning, testing, architecture and security information.

[Requirements](docs/PRD.md) / [Implementation record](docs/IMPLEMENTATION.md) / [Research](docs/VideoCreator-AnalyticsReport.md) / [Documentation brief](documentation-brief.md) / [MIT License](../../LICENSE) / [Third-party notices](../../THIRD-PARTY-NOTICES.txt)
