# Aquila Local 2.0.1 — GPU detection diagnosis (2026-11-25)

## What is on this machine

`C:\Aquila\Aquila Local` (and an identical copy at `OneDrive\Desktop\Aquila Local`, same exe SHA-256
`8215744E…B629`) is a **compiled release only**. There is no C# source on the machine: the README points at
`AquilaLocal-2.0.1-Source.zip`, and the PDB points at
`C:\Users\kaget\Documents\Codex\2026-11-11\if\work\source-2.0.1\`. Neither exists any more.

- App: WPF, .NET 8.0.31, self-contained single-file `Aquila Local.exe` (188 MB). Main assembly
  `Aquila Local.dll` was extracted from the bundle to `recovered/AquilaLocal.dll`, with its portable PDB at `recovered/AquilaLocal.pdb`.
  Decompile those with ILSpy to get the C# back. `recovered/il_full.txt` holds the IL of every method, with
  file:line annotations taken from the PDB. `recovered/documents.txt` lists the 66 original source files.
- Engine: bundled **llama.cpp b12110 `llama-server.exe`, Vulkan backend** (`ggml-vulkan.dll`) with CPU fallback.
  The app starts it on authenticated localhost.
- Speech: separate Python 3.12 worker (`SpeechWorker.py`, Kitten TTS / Chatterbox) in `speech/python`.
- Embeddings: MiniLM ONNX on CPU. Avatar: separate Godot 4 / .NET executable.

## Hardware (read-only checks)

| Source | Result |
|---|---|
| nvidia-smi | **NVIDIA GeForce RTX 5090 Laptop GPU**, driver 591.44, CUDA 13.1, 24463 MiB, WDDM |
| Win32_VideoController | AMD Radeon(TM) 610M (iGPU) + NVIDIA GeForce RTX 5090 Laptop GPU (AdapterRAM wraps at 4 GB and is not used by the app) |
| DXGI (what the app uses) | Radeon 610M: 0.45 GiB dedicated; **RTX 5090: 23.57 GiB dedicated**; Microsoft Basic Render (software, skipped) |
| `llama-server --list-devices` | `Vulkan0: AMD Radeon(TM) 610M (32446 MiB, 30824 MiB free)` / `Vulkan1: NVIDIA GeForce RTX 5090 Laptop GPU (24137 MiB, 23369 MiB free)` |
| vulkaninfo | GPU0 = AMD, INTEGRATED_GPU; GPU1 = NVIDIA, DISCRETE_GPU |

Machine: Eluktronics HYDROC-16 G2, Ryzen 9 9955HX3D, 47 GiB RAM.

## Where the message comes from

`"No supported dedicated GPU found"` is emitted by `MainWindow.RenderProfile`, at
`AquilaLocal\MainWindow.Inference.cs:109`, when `settings.Hardware.Gpu == null`.

`Hardware` is filled by `HardwareInventory.DetectAsync(Engine.Executable)` (`InferenceProfiles.cs:52-…`):

1. `GpuMonitor.GetAdapters()` (`GpuMonitor.cs:121`): `CreateDXGIFactory1` → `EnumAdapters1` (up to 16 adapters) → `GetDesc1`.
   Software adapters are skipped. It records `Description`, the LUID and `DedicatedVideoMemory`.
   COM, DllNotFound and EntryPointNotFound exceptions are swallowed, which leaves the list empty.
2. It runs `engine\llama-server.exe --list-devices` with a **6-second timeout**. Output is kept only if the exit code is 0.
   On a timeout, or an IOException, InvalidOperationException, Win32Exception or OperationCanceledException,
   the process is killed and the listing stays **empty**.
3. `SelectGpu` (`InferenceProfiles.cs:88-94`) applies the regex `^\s*(Vulkan\d+):\s+(.+?)\s+\((\d+) MiB, (\d+) MiB free\)\s*$`
   (Multiline). It joins each Vulkan line to a DXGI adapter whose `Name.Trim()` equals the Vulkan name
   (OrdinalIgnoreCase) **and** whose `DedicatedVideoMemory >= 1 GiB`. It then orders by dedicated memory, then free memory, and takes the first.

So `Gpu == null` happens when (a) DXGI does not return the dGPU, (b) the `--list-devices` listing is empty
(timeout over 6 s, non-zero exit, start failure), or (c) the DXGI and Vulkan names differ.

## Running the detection in isolation

`recovered/detect.ps1` re-implements those three steps from the IL and runs them read-only. Today it gives:

```
DXGI: [0] AMD Radeon(TM) 610M 0.45 GiB | [1] NVIDIA GeForce RTX 5090 Laptop GPU 23.57 GiB | [2] Basic Render (skipped)
--list-devices: exit 0 after ~205 ms
Vulkan0 AMD -> name match, but <1 GiB dedicated -> rejected (correct)
Vulkan1 NVIDIA -> name match, 23.6 GiB -> SELECTED
RESULT: Gpu = Vulkan1 'NVIDIA GeForce RTX 5090 Laptop GPU' 23.6 GiB dedicated → Auto would pick Profile A (High VRAM)
```

**The detection code works on this hardware when the dGPU is visible.** It did not reproduce in two runs.

## Diagnosis

1. **Why inference actually runs on the CPU (confirmed):** both copies of `Data/settings.json` have
   `"Inference.Profile": "Custom"` and `"GpuLayers": 0`. In `InferenceLaunch.Resolve` (`InferenceProfiles.cs:141-…`),
   the Custom profile uses the GPU only when `GpuLayers != 0`. Otherwise it sets device `none` and `-ngl 0`, so the engine is CPU-only
   whatever GPU was detected. The README says "Fresh settings use CPU-only". `engine.log` confirms it: no Vulkan
   device ever loads, and the 27B model generates at about **1.75 tok/s**.
2. **Why the UI sometimes says "No supported dedicated GPU found" (probable, not reproduced):** the detection is fragile.
   It runs once at startup and is never retried. A **6 s** `--list-devices` call that times out, or fails while the
   NVIDIA dGPU is waking from D3cold, powered off (hybrid/Eco/iGPU-only MUX mode on this Eluktronics laptop), or
   switching modes, silently produces `Gpu = null`. Nothing is logged.
3. **Latent bug, wrong default device:** with the Custom profile and `GpuLayers != 0`, `InferenceLaunch.Resolve` treats
   `Hardware == null` (detection not finished, or DetectAsync threw) as "GPU available" and uses the hard-coded device
   `"Vulkan0"`. The app was developed on a machine where Vulkan0 was an RTX 4060 (see README and BUILD-MANIFEST).
   **On this laptop Vulkan0 is the Radeon 610M iGPU**, which reports 32 GB of shared memory. A model loaded before
   detection completes would be offloaded to the integrated GPU.

## Suggested fixes (source-level)

- Pick the GPU by Vulkan `deviceType == DISCRETE_GPU` (or by DXGI LUID) instead of an exact name match. Never fall back to `Vulkan0`.
  Fall back to the largest-VRAM discrete device, or to CPU with a clear message.
- Retry detection with a longer timeout (≥ 20 s on the first call) and log the raw `--list-devices` output, the exit code and the DXGI adapter list.
  Add a "Re-detect hardware" button.
- On first run or upgrade, when a ≥ 8 GiB discrete GPU is detected, default to `Profile = Auto` / `GpuLayers = -1` instead of Custom/0.

## Workaround now (no code change)

In Settings → Inference profile, choose **Auto** (or **A · High VRAM**). If you stay on Custom, set **GPU layers to -1**.
With a 24 GB RTX 5090, the 17 GB Qwen3.8-27B IQ4_XS fits fully on the GPU at 32K context with a q8_0 cache.
