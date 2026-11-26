# Inference profiles and creative writing

Aquila starts the bundled llama.cpp build **12110** over an authenticated local connection. Windows DXGI reports dedicated GPU memory and Windows reports system RAM. Aquila matches those adapters to the server's Vulkan device listing so integrated sharing does not masquerade as dedicated VRAM. Auto selection uses installed capacities. Free RAM and VRAM remain live estimates and do not change the selected profile.

| Profile | Auto selection | Context default and maximum | GPU and cache |
| --- | --- | --- | --- |
| A · High VRAM | At least 20 GiB of dedicated VRAM | 32,768 and 65,536 | All eligible layers on the GPU; Flash Attention on; q8_0 K and V |
| B · Constrained laptop | Up to 10 GiB VRAM and up to 20 GiB RAM | 16,384 and 24,576 | Backend chooses layer placement by default; Flash Attention on; q8_0 K and V, with optional q4_0 |
| Custom | Hardware outside those ranges or no supported discrete GPU | Previous allowance on upgrade, subject to the model's native limit | Manual GPU layers and f16, q8_0, or q4_0 cache |

**Settings → Inference profile** overrides Auto. Each profile remembers its selected allowance. The loaded GGUF's native limit still applies. The blue **Profile target** marks the profile's default for the selected model. The separate **Full GPU estimate** uses current free VRAM and assumes the whole model is on the GPU. With automatic GPU fit, an amber advisory means the current allowance may need some layers in system RAM. Fixed full offload and CPU-only placement show a red warning when the allowance exceeds the estimate for their memory tier. The selected allowance stays readable in its normal text color. Startup hides the estimates until model and hardware information are available. The 4-bit option is experimental. The Deckard 4B test followed a short instruction at 24K with either cache. The 0.6B Qwen fixture repeatedly ran off-topic with q4_0 on the same backend and responded correctly with q8_0.

On a GGUF that declares MoE experts, set **MoE expert layers on CPU** above zero. Aquila passes `--n-cpu-moe N` and requests all remaining layers on the selected GPU so it does not move whole attention layers to the CPU. Profile A reserves full GPU offload; select Laptop or Custom to try expert placement. These settings require enough memory on both devices. The laptop profile passes `--load-mode mmap --lazy-mode off` and leaves out mlock. Windows can still page mapped data under pressure.

**Generation settings → Use creative writing samplers** is enabled on upgrade. Defaults are temperature **0.95**, min-p **0.06**, top-p **1.0**, and DRY **0.8 / 1.75 / 2** with a bounded 4,096-token scan. Top-k is disabled in this mode. When the server lacks DRY and DRY is requested, Aquila uses `repeat_penalty=1.05`. Turning creative sampling off restores the previous sampler behavior and your standard temperature.

Aquila counts tokens through the actual chat template before sending a request. It reserves the output budget and trims whole oldest user/assistant pairs until the newer history fits. The system prompt, personality, context summary, and current request and tool exchange stay in place. The full saved chat remains available. An oversized current message, attachment, or tool exchange can still require a smaller input or a larger allowance.

Use `Aquila Local.exe --verify-inference <new-output-folder> <small-model.gguf> [Deckard-model.gguf]` to reproduce the settings, launch and live cache tests. Tests for the high-VRAM selector and MoE flags use fixtures when that hardware and a sparse model are unavailable.
