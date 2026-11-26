"""Owned offline neural speech worker. JSON lines and fixed local assets."""
import os, sys, json, pathlib, argparse, contextlib, socket, gc, time, wave, ctypes
sys.stdout.reconfigure(encoding='utf-8'); sys.stderr.reconfigure(encoding='utf-8')
os.environ.update(HF_HUB_OFFLINE='1', TRANSFORMERS_OFFLINE='1', HF_HUB_DISABLE_TELEMETRY='1', DO_NOT_TRACK='1', ORT_DISABLE_TELEMETRY='1', TOKENIZERS_PARALLELISM='false', OMP_NUM_THREADS='2', MKL_NUM_THREADS='2', PYTHONNOUSERSITE='1')
parser = argparse.ArgumentParser()
parser.add_argument('--assets', required=True)
parser.add_argument('--output', required=True)
parser.add_argument('--references', required=True)
args = parser.parse_args()
root = pathlib.Path(args.assets).resolve()
output = pathlib.Path(args.output).resolve()
references = pathlib.Path(args.references).resolve()
os.environ['HF_HUB_CACHE'] = str(root / 'kitten-hub')
os.environ['HF_HUB_DISABLE_XET'] = '1'
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
torch.set_num_threads(2)
torch.set_num_interop_threads(1)
model = None
backend = None
engine_loaded = None
def available_ram():
    class Memory(ctypes.Structure):
        _fields_ = [('length', ctypes.c_ulong), ('load', ctypes.c_ulong)] + [(name, ctypes.c_ulonglong) for name in ('total', 'available', 'total_page', 'available_page', 'total_virtual', 'available_virtual', 'extended')]
    value = Memory(); value.length = ctypes.sizeof(value)
    return value.available if ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(value)) else 0
def load(engine, device):
    global model, backend, engine_loaded
    if device not in ('auto', 'cpu', 'gpu'): raise ValueError('Unsupported speech device.')
    if device == 'auto' and model is not None and engine_loaded == engine: return
    selected = 'cpu'
    prefer_gpu = device == 'gpu' or device == 'auto' and (engine == 'qwen' or available_ram() < 6 * 1024**3)
    if prefer_gpu and torch.cuda.is_available():
        free, _ = torch.cuda.mem_get_info()
        margin = 6
        # Do not count our own warm model as a competing allocation.
        if free >= margin * 1024**3 or model is not None and backend == 'cuda:0' and engine_loaded == engine:
            selected = 'cuda:0'
        elif device == 'gpu':
            raise RuntimeError(f'This speech engine needs about {margin} GB of free VRAM. Choose CPU or unload the chat model.')
    elif device == 'gpu': raise RuntimeError('CUDA speech is unavailable on this machine. Choose CPU or an installed Windows voice.')
    if model is not None and backend == selected and engine_loaded == engine: return
    model = None; gc.collect()
    if backend == 'cuda:0': torch.cuda.empty_cache()
    if selected == 'cpu' and available_ram() < 6 * 1024**3:
        raise RuntimeError('Neural CPU speech needs about 6 GB of free RAM. Free memory, try GPU if available, or select a Windows voice.')
    emit('progress', message='Loading ' + ('Kitten TTS 2' if engine == 'kitten' else 'Qwen3 speech') + ' on ' + ('GPU' if selected.startswith('cuda') else 'CPU') + '…')
    with contextlib.redirect_stdout(sys.stderr):
        if engine == 'kitten':
            local_model = root / 'models' / 'kitten'
            local_packages = root / 'kitten-packages'
            if not local_packages.is_dir() or not (local_model / 'config.json').is_file():
                raise RuntimeError('The local Kitten TTS 2 assets are missing.')
            package_path = str(local_packages)
            if package_path not in sys.path: sys.path.append(package_path)
            from kittenml import KittenTTS
            model = KittenTTS(str(local_model), device=selected, cache_dir=str(root / 'kitten-hub'),
                             weights='full', decoder='student_w4')
        else:
            from qwen_tts import Qwen3TTSModel
            local_model = root / 'models' / 'custom'
            if not (local_model / 'model.safetensors').is_file():
                raise RuntimeError('The local Qwen3 speech assets are missing.')
            model = Qwen3TTSModel.from_pretrained(str(local_model), device_map=selected, dtype=torch.bfloat16, attn_implementation='sdpa', local_files_only=True, low_cpu_mem_usage=True)
    backend = selected
    engine_loaded = engine

KITTEN_VOICES = ('Bella', 'Jasper', 'Luna', 'Bruno', 'Rosie', 'Hugo', 'Kiki', 'Leo')
QWEN_VOICES = ('ryan', 'aiden', 'vivian', 'serena', 'uncle_fu', 'dylan', 'eric', 'ono_anna', 'sohee')
KITTEN_MOODS = {'warm': 'tender', 'cheerful': 'joyful', 'gentle': 'tender',
                'serious': 'contemplative', 'concerned': 'tender'}
KITTEN_MOODS.update((mood, mood) for mood in ('angry', 'contemplative', 'excited', 'joyful',
    'mundane', 'nervous', 'sad', 'stern', 'surprised', 'tender'))

def kitten_voice(request, speaker):
    if speaker.startswith('Kitten: ') and speaker[8:] in KITTEN_VOICES:
        return {'voice': speaker[8:]}
    if not speaker.startswith('Clone: '):
        raise ValueError('Unknown Kitten voice.')
    key = speaker[7:]
    if len(key) != 32 or any(c not in '0123456789abcdef' for c in key):
        raise ValueError('Invalid local voice ID.')
    sample = pathlib.Path(request.get('reference', '')).resolve()
    allowed = (references / key / 'reference.wav', references / ('.draft-' + key) / 'reference.wav')
    if sample not in allowed or not sample.is_file() or sample.stat().st_size > 1500000:
        raise ValueError('The reference must be an imported local voice sample.')
    transcript = request.get('reference_text', '').strip()
    if not transcript or len(transcript) > 1000:
        raise ValueError('Enter the transcript of the voice sample.')
    with wave.open(str(sample), 'rb') as clip:
        if (clip.getnchannels(), clip.getsampwidth(), clip.getframerate()) != (1, 2, 24000) or not 120000 <= clip.getnframes() <= 720000:
            raise ValueError('Invalid voice sample audio.')
    return {'reference': str(sample), 'reference_text': transcript}

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
        engine = request.get('engine', 'qwen')
        speaker = request.get('speaker', '')
        if engine not in ('qwen', 'kitten'): raise ValueError('Unknown speech engine.')
        if engine == 'kitten':
            voice = kitten_voice(request, speaker)
        elif speaker.lower() not in QWEN_VOICES:
            raise ValueError('Unknown Qwen3 speaker.')
        started = time.monotonic(); load(engine, request.get('device', 'auto'))
        emit('progress', message='Generating expressive speech on ' + ('GPU' if backend.startswith('cuda') else 'CPU') + '…')
        torch.manual_seed(42)
        with contextlib.redirect_stdout(sys.stderr), torch.inference_mode():
            if engine == 'kitten':
                intensity = request.get('intensity', .5)
                mood = KITTEN_MOODS.get(request.get('mood'))
                if mood: text = f'[{mood}] {text}'
                samples = model.generate(text, **voice, preset='expressive' if intensity > .7 else 'stable',
                                         max_new_tokens=min(768, max(160, len(text) * 5)))
                rate = model.sample_rate
            else:
                wavs, rate = model.generate_custom_voice(text=text, language='English', speaker=speaker.lower(), instruct=direction, non_streaming_mode=True, max_new_tokens=min(768, max(96, len(text) * 3)), do_sample=False, subtalker_dosample=False)
                samples = wavs[0]
        samples = np.asarray(samples, dtype=np.float32).reshape(-1)
        if rate != 24000 or len(samples) < 2400 or len(samples) > 24000 * 90 or not np.isfinite(samples).all(): raise RuntimeError('The speech model returned invalid or oversized audio.')
        pace = {'Slow': .88, 'Lively': 1.12}.get(request.get('pace'))
        if engine == 'kitten' and pace is not None:
            from librosa.effects import time_stretch
            samples = time_stretch(samples, rate=pace)
            if len(samples) > rate * 90 or not np.isfinite(samples).all():
                raise RuntimeError('Pace adjustment returned invalid or oversized audio.')
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
