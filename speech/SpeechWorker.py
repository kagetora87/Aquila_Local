"""Owned offline Qwen3 CustomVoice worker. JSON lines, fixed local assets, no network."""
import os, sys, json, pathlib, argparse, contextlib, socket, gc, time, wave, ctypes
os.environ.update(HF_HUB_OFFLINE='1', TRANSFORMERS_OFFLINE='1', HF_HUB_DISABLE_TELEMETRY='1', DO_NOT_TRACK='1', ORT_DISABLE_TELEMETRY='1', TOKENIZERS_PARALLELISM='false', OMP_NUM_THREADS='2', MKL_NUM_THREADS='2', PYTHONNOUSERSITE='1')
parser = argparse.ArgumentParser()
parser.add_argument('--assets', required=True)
parser.add_argument('--output', required=True)
args = parser.parse_args()
root = pathlib.Path(args.assets).resolve()
output = pathlib.Path(args.output).resolve()
output.mkdir(parents=True, exist_ok=True)
protocol = sys.stdout
def emit(kind, **fields):
    protocol.write(json.dumps(dict(type=kind, **fields), ensure_ascii=False) + '\n'); protocol.flush()
def blocked(*args, **kwargs):
    raise RuntimeError('Network access is disabled in the speech worker.')
socket.socket.connect = blocked
socket.socket.connect_ex = blocked
socket.create_connection = blocked
with contextlib.redirect_stdout(sys.stderr):
    import numpy as np
    import torch
    from qwen_tts import Qwen3TTSModel
torch.set_num_threads(2)
torch.set_num_interop_threads(1)
model = None
backend = None
def available_ram():
    class Memory(ctypes.Structure):
        _fields_ = [('length', ctypes.c_ulong), ('load', ctypes.c_ulong)] + [(name, ctypes.c_ulonglong) for name in ('total', 'available', 'total_page', 'available_page', 'total_virtual', 'available_virtual', 'extended')]
    value = Memory(); value.length = ctypes.sizeof(value)
    return value.available if ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(value)) else 0
def load(device):
    global model, backend
    if device not in ('auto', 'cpu', 'gpu'): raise ValueError('Unsupported speech device.')
    selected = 'cpu'
    if device != 'cpu' and torch.cuda.is_available():
        free, _ = torch.cuda.mem_get_info()
        if free >= 6 * 1024**3: selected = 'cuda:0'
        elif device == 'gpu': raise RuntimeError('Expressive speech needs about 6 GB of free VRAM, including a working margin. Unload the chat model, choose CPU/Windows voice, or use the larger GPU.')
    elif device == 'gpu': raise RuntimeError('CUDA speech is unavailable on this machine. Choose CPU or an installed Windows voice.')
    if model is not None and backend == selected: return
    model = None; gc.collect()
    if torch.cuda.is_available(): torch.cuda.empty_cache()
    if selected == 'cpu' and available_ram() < 6 * 1024**3: raise RuntimeError('Expressive CPU speech needs about 6 GB of free RAM. Free memory or select an installed Windows voice; your text answer is saved.')
    emit('progress', message='Loading expressive voice on ' + ('GPU' if selected.startswith('cuda') else 'CPU') + '…')
    with contextlib.redirect_stdout(sys.stderr):
        model = Qwen3TTSModel.from_pretrained(str(root / 'models' / 'custom'), device_map=selected, dtype=torch.bfloat16, attn_implementation='sdpa', local_files_only=True, low_cpu_mem_usage=True)
    backend = selected
emit('ready')
for line in sys.stdin:
    request = {}; path = None
    try:
        if len(line) > 16000: raise ValueError('Speech request is too large.')
        request = json.loads(line)
        if request.get('command') == 'quit': break
        identity = request.get('id', '')
        if len(identity) != 32 or any(c not in '0123456789abcdef' for c in identity): raise ValueError('Invalid speech request identity.')
        text = request.get('text', '').strip(); direction = request.get('direction', '')
        if not text or len(text) > 1000 or not isinstance(direction, str) or len(direction) > 1600: raise ValueError('Speech text/direction is outside its bounds.')
        speaker = request.get('speaker', '').lower()
        if speaker not in ('ryan', 'aiden', 'vivian', 'serena', 'uncle_fu', 'dylan', 'eric', 'ono_anna', 'sohee'): raise ValueError('Unknown speaker.')
        started = time.monotonic(); load(request.get('device', 'auto'))
        emit('progress', message='Generating expressive speech on ' + ('GPU' if backend.startswith('cuda') else 'CPU') + '…')
        torch.manual_seed(42)
        with contextlib.redirect_stdout(sys.stderr), torch.inference_mode():
            wavs, rate = model.generate_custom_voice(text=text, language='English', speaker=speaker, instruct=direction, non_streaming_mode=True, max_new_tokens=min(768, max(96, len(text) * 3)), do_sample=False, subtalker_dosample=False)
        samples = np.asarray(wavs[0], dtype=np.float32).reshape(-1)
        if rate != 24000 or len(samples) < 2400 or len(samples) > 24000 * 90 or not np.isfinite(samples).all(): raise RuntimeError('The speech model returned invalid or oversized audio.')
        peak = float(np.max(np.abs(samples)))
        if peak < 0.0001: raise RuntimeError('The speech model returned silence.')
        if peak > .95: samples *= .95 / peak
        path = output / (identity + '.wav')
        with wave.open(str(path), 'wb') as stream:
            stream.setnchannels(1); stream.setsampwidth(2); stream.setframerate(rate); stream.writeframes((np.clip(samples, -1, 1) * 32767).astype('<i2').tobytes())
        emit('audio', id=identity, path=str(path), rate=rate, frames=len(samples), seconds=len(samples)/rate, elapsed=time.monotonic()-started, backend=backend)
    except Exception as error:
        if path and path.exists(): path.unlink()
        emit('error', id=request.get('id', ''), message=str(error)[:1000])
        if 'out of memory' in str(error).lower():
            model = None; backend = None; gc.collect()
            if torch.cuda.is_available(): torch.cuda.empty_cache()
