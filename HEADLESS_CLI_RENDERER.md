# Headless CLI renderer extraction notes

Target command:

```sh
openutau-cli render input.ustx --out output.wav
```

This document maps the current `.ustx` load and audio render/export paths and proposes the smallest safe first step toward a headless CLI renderer. The guiding constraint is to preserve the Avalonia GUI behavior and avoid a broad render refactor in the first pass.

## 1. Project and class map for loading `.ustx` files

- **Solution layout**
  - `OpenUtau.Core` is the reusable non-Avalonia project that contains the project model, file formats, singers, phonemizers, renderers, signal chain, preferences, and managers.
  - `OpenUtau` is the Avalonia desktop application and references both `OpenUtau.Core` and `OpenUtau.Plugin.Builtin`.
  - `OpenUtau.Plugin.Builtin` contains built-in phonemizer plugins loaded by `DocManager.SearchAllPlugins()`.
  - `OpenUtau.Test` contains tests.

- **Format detection and dispatch**
  - `OpenUtau.Core.Format.Formats.DetectProjectFormat(string file)` reads the first few lines and identifies `.ustx` by either JSON (`"ustxVersion":`) or YAML (`ustx_version:`) markers.
  - `OpenUtau.Core.Format.Formats.ReadProject(string[] files)` is the reusable loader entry point. For `ProjectFormats.Ustx`, it delegates to `OpenUtau.Core.Format.Ustx.Load(files[0])` and returns a `UProject` without publishing a UI command.
  - `OpenUtau.Core.Format.Formats.LoadProject(string[] files)` wraps `ReadProject()` and then publishes `LoadProjectNotification` through `DocManager`. That is the GUI/stateful load path and should not be the first CLI entry point.

- **`.ustx` serializer**
  - `OpenUtau.Core.Format.Ustx.Load(string filePath)` deserializes YAML into `UProject`, assigns `FilePath`, calls `UProject.AfterLoad()`, validates the project, marks it saved, and returns it.
  - `OpenUtau.Core.Format.Ustx.Save()` and `AutoSave()` call `UProject.BeforeSave()`/`AfterSave()` and are not needed for the initial render-only CLI path.

- **Project model post-load work**
  - `OpenUtau.Core.Ustx.UProject.AfterLoad()` calls `AfterLoad()` on tracks, merges serialized voice/wave part lists into `parts`, and calls `AfterLoad()` on every part.
  - `OpenUtau.Core.Ustx.UTrack.AfterLoad()` restores the phonemizer type, resolves the serialized singer id through `SingerManager.Inst.GetSinger()`, creates a missing-singer placeholder if needed, and initializes renderer settings.
  - `OpenUtau.Core.Ustx.UVoicePart.AfterLoad()` restores note/curve runtime links.
  - `OpenUtau.Core.Ustx.UWavePart.AfterLoad()` resolves wave-part paths relative to the project file and calls `Load(project)`.
  - `OpenUtau.Core.Ustx.UProject.ValidateFull()` builds timing, validates tracks, pushes phonemizer work, validates phonemes, and builds render phrases once phonemizer responses are available.

## 2. Project and class map for rendering/exporting audio

- **Desktop export call sites**
  - The GUI mixdown export menu asks for an output path via Avalonia file picker and then calls `PlaybackManager.Inst.RenderMixdown(project, file)`.
  - The GUI per-track WAV export path calls `PlaybackManager.Inst.RenderToFiles(project, file)`.

- **Current export service**
  - `OpenUtau.Core.PlaybackManager.RenderMixdown(UProject project, string exportPath)` runs a background task, constructs `RenderEngine`, calls `RenderEngine.RenderMixdown(..., wait: true)`, checks output writability, and writes 16-bit WAV using `WaveFileWriter.CreateWaveFile16(exportPath, new ExportAdapter(projectMix))`.
  - `OpenUtau.Core.PlaybackManager.RenderToFiles(UProject project, string exportPath)` calls `RenderEngine.RenderTracks()`, derives per-track output names with `PathManager.GetExportPath()`, and writes mono track files.

- **Render engine and signal chain**
  - `OpenUtau.Core.Render.RenderEngine` is currently internal to `OpenUtau.Core`. It prepares voice-part render requests, asks each phrase renderer for layout and rendered samples, builds `WaveSource`/`WaveMix` instances, applies `Fader` volume and pan, and returns an `ISignalSource` graph for export/playback.
  - `RenderEngine.PrepareRequests()` filters `UVoicePart` instances, respects `Preferences.Default.SkipRenderingMutedTracks`, creates render phrases via `UVoicePart.GetRenderRequest()`, creates placeholder `WaveSource` objects from phrase layouts, and stores a `WaveMix` on each request.
  - `RenderEngine.RenderRequests()` calls each phrase renderer (`IRenderer.Render()`), sets rendered samples onto the corresponding `WaveSource`, stores completed part mixes with `UVoicePart.SetMix()`, and publishes progress/part-rendered notifications through `DocManager`.
  - `OpenUtau.Core.SignalChain.ExportAdapter` is currently internal to `OpenUtau.Core`. It adapts an `ISignalSource` to NAudio's `ISampleProvider` at 44.1 kHz stereo float for WAV writing.

- **Renderer selection and backends**
  - `OpenUtau.Core.Ustx.URenderSettings.Validate(UTrack track)` chooses or creates an `IRenderer` using `Renderers.GetDefaultRenderer()`/`Renderers.CreateRenderer()`.
  - `OpenUtau.Core.Render.Renderers.CreateRenderer()` supports Classic, WORLDLINE-R, ENUNU, VOGEN, DiffSinger, and Voicevox renderer implementations.
  - `OpenUtau.Core.Classic.ToolsManager`, `ClassicRenderer`, `WorldlineRenderer`, and the neural/external renderer classes must remain intact; the CLI should reuse these rather than simplifying singer, resampler, wavtool, phonemizer, or plugin support.

## 3. Headless blockers and desktop assumptions

- **`DocManager` is both application state and event bus.**
  - `DocManager.Initialize(Thread mainThread, TaskScheduler mainScheduler)` loads plugins and creates the `PhonemizerRunner`.
  - `DocManager.ExecuteCmd()` requires calls on `mainThread`; if called from another thread, it calls `PostOnUIThread(() => ExecuteCmd(cmd))`. A CLI must provide a non-Avalonia `PostOnUIThread` strategy or avoid paths that issue commands from worker threads.
  - Many load/render/phonemizer paths publish notifications (`ProgressBarNotification`, `ValidateProjectNotification`, `PreRenderNotification`, `PartRenderedNotification`, `ErrorMessageNotification`) even when the caller only wants a WAV file.

- **Phonemization is asynchronous and scheduler-dependent.**
  - `UVoicePart.Validate()` pushes `PhonemizerRequest` into `DocManager.Inst.PhonemizerRunner`.
  - `PhonemizerRunner` processes requests on a background thread, then posts its response back onto `mainScheduler`; the response triggers another validation pass for that part.
  - A CLI render path needs a deterministic way to wait until all voice parts have `PhonemesUpToDate` and their `renderPhrases` have been generated before invoking `RenderEngine`.

- **Current render/export API surface is not externally consumable by a separate CLI assembly.**
  - `RenderEngine`, `RenderPartRequest`, `UVoicePart.GetRenderRequest()`, `UVoicePart.SetMix()`, and `ExportAdapter` are internal. A CLI project outside `OpenUtau.Core` cannot call the cleanest export primitives directly.
  - `PlaybackManager.RenderMixdown()` is public, but it is a singleton that also owns audio playback state, subscribes to `DocManager`, releases source temp files in its constructor, and reports errors/progress via `DocManager` notifications.

- **Preferences and paths are initialized through singletons.**
  - `PathManager` derives data/cache/plugin/singer paths from the entry assembly and OS conventions.
  - `Preferences` loads or creates `prefs.json` at `PathManager.Inst.PrefsFilePath`. A CLI should be explicit about whether it uses the same user data directory as the GUI, because that controls singer/plugin discovery.

- **Singer and plugin discovery must happen before project validation.**
  - `SingerManager.Initialize()` searches installed singers under `PathManager` paths.
  - `DocManager.Initialize()` loads built-in/external phonemizer plugins before `UTrack.AfterLoad()` resolves phonemizers.
  - If the built-in plugin DLL is not copied next to the CLI executable, `DocManager.SearchAllPlugins()` will not find `OpenUtau.Plugin.Builtin.dll` at `AppContext.BaseDirectory`.

- **Wave parts are file-system dependent.**
  - `UWavePart.AfterLoad()` resolves paths relative to `project.FilePath` and loads wave metadata/samples asynchronously. Headless export must preserve this behavior and surface missing-file failures cleanly.

- **GUI-only dialogs are not required for the first CLI path.**
  - The GUI export commands use Avalonia file pickers and message boxes, but those are outside `OpenUtau.Core`. The core render/export path itself does not require Avalonia controls if the CLI supplies paths and converts errors to process exit codes.

## 4. Smallest safe headless architecture

Add a new console project, tentatively `OpenUtau.Cli`, but keep the first real rendering service inside `OpenUtau.Core` so it can use the current internal render types without widening many internals.

Recommended layering:

1. **`OpenUtau.Cli` project**
   - `OutputType=Exe`, `TargetFramework=net8.0`.
   - References `OpenUtau.Core` and `OpenUtau.Plugin.Builtin`.
   - Parses only the initial command shape: `render <input.ustx> --out <output.wav>`.
   - Performs console logging, returns non-zero exit codes on failure, and does not reference Avalonia.

2. **`OpenUtau.Core.Headless` or `OpenUtau.Core.Rendering` service**
   - Public API example: `Task RenderMixdownAsync(string inputPath, string outputPath, CancellationToken cancellationToken = default)`.
   - Internally initializes `DocManager` with the current CLI thread and a CLI-compatible scheduler/poster, initializes `SingerManager`, loads the project using `Formats.ReadProject()` or `Ustx.Load()` instead of `Formats.LoadProject()`, validates/waits for phonemization, then calls existing `RenderEngine`/`ExportAdapter` to write WAV.
   - Converts notifications into no-op or console progress events instead of dialogs.
   - Does not modify desktop menu commands or desktop export behavior.

3. **No first-pass renderer simplification**
   - Reuse `Renderers.CreateRenderer()` and existing singer/phonemizer/resampler/plugin paths.
   - Document unsupported environment/runtime dependencies as errors instead of silently choosing a different renderer.

## 5. First implementation step that should compile

The smallest code-producing first step should be:

1. Add `OpenUtau.Cli/OpenUtau.Cli.csproj` as a console project referencing `OpenUtau.Core` and `OpenUtau.Plugin.Builtin`.
2. Add a minimal `Program.cs` that parses `openutau-cli render input.ustx --out output.wav`, validates arguments, and returns a clear "render not wired yet" exit code.
3. Add the CLI project to `OpenUtau.sln`.
4. Do **not** call `PlaybackManager.RenderMixdown()` from the CLI yet, because that would couple the first pass to `DocManager` UI scheduling, asynchronous phonemizer completion, and playback singleton behavior before those assumptions are isolated.
5. In the next small change, add the `OpenUtau.Core` headless render service described above and make the CLI call that service.

This first step compiles, introduces the command surface, and preserves all existing GUI behavior because it adds a new project only.
