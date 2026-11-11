# Documentation brief: making the request “deeper,” and its execution

[↑ Documentation map](../README.md) | [日本語](../../../users-guide/documentation-brief.md) | **English** | [简体中文](../../zh-CN/users-guide/documentation-brief.md) | [한국어](../../ko/users-guide/documentation-brief.md)


[App README](../README.md) / [Guide index](README.md) / [Verification](verification.md)

This records how the guide request was made “deeper,” not merely “bigger”: (1) additional expert work, (2) raw starting materials, (3) methods/tools, (4) independent verification; the request was rewritten and executed. The final table maps rewritten requirements to deliverables/evidence.

## Original request (abridged)

- Create beginner quickstart, screen-component explanations, screenshots under `/users-guide`.
- Create technical reference/component diagrams for customizing software engineers.
- Add illustrated first-user preparation/actions/deliverables to `/README.md`.
- All diagrams SVG.

## (1) What leading experts additionally do

Technical writers/developer-experience experts do the following before increasing page count:

1. **Narrow audience and outcome:** define first success as exporting one video; choose one source + P4 + Standard, put other features after first success.
2. **State prerequisites/failures first:** Azure resources, permissions, destinations, charges, rights, non-offline scope before steps; include likely blockers and diagnosis, not only happy paths.
3. **Reverse-map UI to implementation:** verify labels/defaults/visibility/disabled conditions/API in source, not appearances; explain what controls change.
4. **Make screenshots reproducible:** launch isolated data, capture automatically, mask personal data, document recreation instead of manual capture.
5. **Give engineers change entry points and invariant contracts:** boundaries, persistence, timeline conversion, QA/approval, extension tests.
6. **Separate claims from checks:** distinguish execution, source-only evidence, and unverified real-cloud/independent usability scope.
7. **Make diagrams verifiable:** SVG title/desc, mechanical overflow/external-reference checks; distinguish screen captures from schematics.

## (2) Raw primary materials

Not summarized READMEs/historical prose:

| Material | What it establishes |
|---|---|
| Running app in a real browser | Actual labels, initial/post-operation state |
| UI [app.js](../../../src/VideoCreator.App/wwwroot/js/app.js), [index.html](../../../src/VideoCreator.App/wwwroot/index.html) | Visibility/disabled conditions/input destinations |
| API/settings [Api.cs](../../../src/VideoCreator.App/Api.cs), [Program.cs](../../../src/VideoCreator.App/Program.cs), [AppOptions.cs](../../../src/VideoCreator.App/AppOptions.cs) | Routes, headers, JSON, startup configuration |
| Core [Models.cs](../../../src/VideoCreator.Core/Models/Models.cs), [PipelineRunner.cs](../../../src/VideoCreator.Core/Pipeline/PipelineRunner.cs), [QaEngine.cs](../../../src/VideoCreator.Core/Qa/QaEngine.cs) | Structures, processing order, QA |
| [run.cmd](../../../run.cmd), [setup.ps1](../../../scripts/setup.ps1), [get-tools.ps1](../../../scripts/get-tools.ps1) | User-supplied vs automatically supplied prerequisites |
| [Presets](../../../presets) | Pattern duration/caption/audio policies |
| [Tests](../../../tests), [exports.http](../../../src/VideoCreator.App/exports.http) | Contract behavior |
| Actual MP4/SRT/ASS/JSON/PNG | Artifact formats, checked with ffprobe etc. |
| Microsoft Learn (Speech Fast Transcription) | Cloud prerequisites/limitations |

## (3) Methods and tools

| Method | Tools | Application |
|---|---|---|
| Task analysis/shortest path | Source reading/real UI | [Quickstart](quickstart.md): prepare → readiness → assets → generate → review → export |
| Extract UI contract | grep/source reading | [Screens](screens.md): meaning/default/use cases |
| Isolated execution | `VideoCreator__DataDir`, `__ProjectsDir`, `__Port`, synthetic [seed.cs](../../../users-guide/tools/seed.cs) | Reproduce screens/outputs without user data/cloud |
| Automatic screenshots | Playwright + Edge [capture.mjs](../../../users-guide/tools/capture.mjs) | Embed real PNGs in SVG, mask paths |
| Diagram production | Handwritten SVG with title/desc | Five: overview/components/pipeline/timelines/QA |
| Output inspection | ffprobe/API retrieval | Codec/resolution/audio/duration/output keys |
| Mechanical documentation checks | [validate.mjs](../../../users-guide/tools/validate.mjs), `git diff --check` | Links/anchors/XML/embedded PNG/text bounds/rendering |

## (4) What beyond you or me demonstrates correctness?

| Independent judge | Demonstrates | Status |
|---|---|---|
| Actual app | Screens are real, not imagined | Done: automatic Edge capture |
| Outputs/ffprobe | Promised artifacts really exist: H.264/AAC, 1080×1920, eight outputs | Done |
| Automatic app QA | Hard export conditions | Done: all pass on synthetic footage |
| Browser SVG/XML parser | Diagrams intact, text in bounds | Done: all 20 |
| Link validator | Internal/source links resolve | Done |
| Build/existing tests | Described contracts match code | Partial: build passed; full tests not rerun |
| Independent novice | Can export with guide alone | **Not done**; step 6 of [checklist](verification.md#update-checklist) |
| Real Azure transmission | Real speech recognition/cost | **Not done**; distinguished from synthetic results |

## Rewritten request

> Create documentation for first-time VideoCreator users and customizing software engineers, using the current working tree and actual screens as primary evidence. Verify against UI source, APIs, models, startup scripts, presets, tests, and actual exports, not summarized READMEs/prior prose.
>
> 1. **README (`/README.md`):** one table and one SVG showing preparation (PC, authorized footage, own Azure resource/permissions), actions (launch, assets/rights, pattern, generation, review/edit, export), outputs (MP4/SRT/ASS/plan/thumbnails/metadata proposals). Warn about destinations, not-offline behavior, and QA not guaranteeing rights/fidelity.
> 2. **Quickstart:** success = first export; restrict to one video + P4 + Standard. Give a “verified, proceed” criterion at each stage.
> 3. **Screen/components:** isolated actual app, automatic Playwright/Edge capture embedded in SVG. Verify each button/field/switch meaning/default/visibility/disabled condition/use case against source. State synthetic footage and personal-data masking.
> 4. **Troubleshooting:** diagnosis order, common symptoms, QA interpretation, storage/backups.
> 5. **Technical reference:** SVG components, generation/export, models/persistence, confusing timelines, approval/QA/reproduction contracts, extension points/tests/build. Separate configuration/API list.
> 6. **Diagrams:** all SVG, schematic title/desc, distinct from actual screens.
> 7. **Verification:** provide/run automation for links/anchors/SVG syntax/embedded images/overflow/browser rendering. Export one video; inspect with ffprobe/APIs. Record executed, source-only, and unverified scope (real cloud/independent users), and screenshot recreation.
> 8. Preserve existing application changes.

## Mapping to execution

| Requirement | Deliverables | Evidence |
|---|---|---|
| 1. README preparation/actions/outputs | [README](../README.md#first-time-users-preparation--operation--deliverables), [overview](images/overview.svg) | Eight actual keys, ffprobe formats ([record](verification.md#execution-checks-performed)) |
| 2. Quickstart | [quickstart.md](quickstart.md) | Actual synthetic rights → P4 → review → export UI |
| 3. Screens/captures | [screens.md](screens.md), 15 screen SVGs ([original images](../../../users-guide/images)) | [capture.mjs](../../../users-guide/tools/capture.mjs); embedded-PNG/render validation |
| 4. Troubleshooting | [troubleshooting.md](troubleshooting.md), [QA](images/qa-gates.svg) | Conditions checked against [QaEngine.cs](../../../src/VideoCreator.Core/Qa/QaEngine.cs); executed QA passed |
| 5. Technical reference | [reference](technical-reference.md#1-components), [components](images/components.svg), [pipeline](images/pipeline.svg), [timelines](images/timelines.svg), [configuration/API](configuration-api.md) | Zero-warning/error build; routes/settings source-checked |
| 6. SVG | Five schematics + fifteen screens = twenty | [validate.mjs](../../../users-guide/tools/validate.mjs): SVG-only, XML/title/desc/external refs/overflow/rendering |
| 7. Verification records | [verification.md](verification.md) | Validator PASS; unverified scope stated |
| 8. Existing changes preserved | — | Application changes not reverted |

Unperformed independent-novice evaluation, real Azure speech tests, and full test reruns are not treated as correctness evidence.
---

[↑ Documentation map](../README.md) | [日本語](../../../users-guide/documentation-brief.md) | **English** | [简体中文](../../zh-CN/users-guide/documentation-brief.md) | [한국어](../../ko/users-guide/documentation-brief.md)
