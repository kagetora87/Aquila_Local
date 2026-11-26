using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AquilaLocal;

public sealed record ModelInfo(string Name, string Architecture, int NativeContext, long FileBytes, double KvBytesPerToken, bool MetadataKnown)
{
	public int ExpertCount { get; init; }

	public int BlockCount { get; init; }

	public static ModelInfo Unknown(string path)
	{
		return new ModelInfo(Path.GetFileNameWithoutExtension(path), "unknown", 32768, File.Exists(path) ? new FileInfo(path).Length : 0, 262144.0, MetadataKnown: false);
	}

	public static string FindProjector(string path)
	{
		string directoryName = Path.GetDirectoryName(path);
		if (directoryName == null || !Directory.Exists(directoryName))
		{
			return "";
		}
		if (Path.GetFileName(path).Contains("Deckard", StringComparison.OrdinalIgnoreCase))
		{
			return Directory.EnumerateFiles(directoryName, "*.gguf").FirstOrDefault((string p) => Path.GetFileName(p).Contains("Deckard", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(p).Contains("mmproj", StringComparison.OrdinalIgnoreCase)) ?? "";
		}
		string stem = Path.GetFileNameWithoutExtension(path);
		return Directory.EnumerateFiles(directoryName, "*.gguf").FirstOrDefault((string p) => Path.GetFileName(p).StartsWith(stem + ".mmproj", StringComparison.OrdinalIgnoreCase)) ?? "";
	}

	public static ModelInfo Read(string path)
	{
		try
		{
			FileStream stream = File.OpenRead(path);
			BinaryReader reader;
			Dictionary<string, object> values;
			string architecture;
			try
			{
				reader = new BinaryReader(stream, Encoding.UTF8);
				try
				{
					if (reader.ReadUInt32() != 1179993927)
					{
						return Unknown(path);
					}
					uint num = reader.ReadUInt32();
					if ((num < 2 || num > 3) ? true : false)
					{
						return Unknown(path);
					}
					reader.ReadUInt64();
					ulong num2 = reader.ReadUInt64();
					if (num2 > 100000)
					{
						return Unknown(path);
					}
					values = new Dictionary<string, object>();
					for (ulong num3 = 0uL; num3 < num2; num3++)
					{
						string text = String(keep: true);
						uint type = reader.ReadUInt32();
						bool flag = text == "general.name" || text == "general.architecture" || text.EndsWith("context_length") || text.EndsWith("block_count") || text.EndsWith("expert_count") || text.EndsWith("embedding_length") || text.Contains("attention.") || text.EndsWith("full_attention_interval");
						object obj = Value(type, flag);
						if (flag && obj != null)
						{
							values[text] = obj;
						}
					}
					architecture = values.GetValueOrDefault("general.architecture")?.ToString() ?? "unknown";
					int nativeContext = (int)Math.Clamp(Number("context_length", 32768.0), 2048.0, 262144.0);
					double num4 = Number("block_count", 32.0);
					double num5 = Number("attention.head_count", 32.0);
					double num6 = Number("attention.head_count_kv", num5);
					double num7 = Number("attention.key_length", Number("embedding_length", 4096.0) / Math.Max(num5, 1.0));
					double num8 = Number("attention.value_length", num7);
					double val = Number("full_attention_interval", (!architecture.Contains("qwen35") && !architecture.Contains("qwen3_5")) ? 1 : 4);
					double num9 = Math.Ceiling(num4 / Math.Max(1.0, val));
					double kvBytesPerToken = Math.Max(1024.0, num9 * num6 * (num7 + num8) * 2.0);
					return new ModelInfo(values.GetValueOrDefault("general.name")?.ToString() ?? Path.GetFileNameWithoutExtension(path), architecture, nativeContext, stream.Length, kvBytesPerToken, values.ContainsKey(architecture + ".context_length"))
					{
						ExpertCount = (int)Math.Clamp(Number("expert_count", 0.0), 0.0, 65536.0),
						BlockCount = (int)Math.Clamp(num4, 0.0, 999.0)
					};
				}
				finally
				{
					if (reader != null)
					{
						((IDisposable)reader).Dispose();
					}
				}
			}
			finally
			{
				if (stream != null)
				{
					((IDisposable)stream).Dispose();
				}
			}
			double Number(string suffix, double fallback)
			{
				if (!values.TryGetValue(architecture + "." + suffix, out object value))
				{
					return fallback;
				}
				return Convert.ToDouble(value);
			}
			string String(bool keep)
			{
				ulong num10 = reader.ReadUInt64();
				if (num10 > 67108864 || num10 > (ulong)(stream.Length - stream.Position))
				{
					throw new InvalidDataException("Invalid GGUF string");
				}
				if (!keep)
				{
					stream.Seek((long)num10, SeekOrigin.Current);
					return "";
				}
				return Encoding.UTF8.GetString(reader.ReadBytes(checked((int)num10)));
			}
			object? Value(uint num10, bool keep, int depth = 0)
			{
				if (depth > 2)
				{
					throw new InvalidDataException("Nested GGUF arrays");
				}
				switch (num10)
				{
				case 0u:
					return reader.ReadByte();
				case 1u:
					return reader.ReadSByte();
				case 2u:
					return reader.ReadUInt16();
				case 3u:
					return reader.ReadInt16();
				case 4u:
					return reader.ReadUInt32();
				case 5u:
					return reader.ReadInt32();
				case 6u:
					return reader.ReadSingle();
				case 7u:
					return reader.ReadBoolean();
				case 8u:
					return String(keep);
				case 9u:
				{
					uint num11 = reader.ReadUInt32();
					ulong num12 = reader.ReadUInt64();
					if (num12 > 2000000)
					{
						throw new InvalidDataException("Oversized GGUF array");
					}
					int num13;
					switch (num11)
					{
					case 0u:
					case 1u:
					case 7u:
						num13 = 1;
						break;
					case 2u:
					case 3u:
						num13 = 2;
						break;
					case 4u:
					case 5u:
					case 6u:
						num13 = 4;
						break;
					case 10u:
					case 11u:
					case 12u:
						num13 = 8;
						break;
					default:
						num13 = 0;
						break;
					}
					int num14 = num13;
					if (num14 > 0)
					{
						long num15 = checked((long)num12 * num14);
						if (num15 > stream.Length - stream.Position)
						{
							throw new InvalidDataException();
						}
						stream.Seek(num15, SeekOrigin.Current);
					}
					else
					{
						for (ulong num16 = 0uL; num16 < num12; num16++)
						{
							Value(num11, keep: false, depth + 1);
						}
					}
					return null;
				}
				case 10u:
					return reader.ReadUInt64();
				case 11u:
					return reader.ReadInt64();
				case 12u:
					return reader.ReadDouble();
				default:
					throw new InvalidDataException("Unknown GGUF type");
				}
			}
		}
		catch (Exception ex) when (((ex is IOException || ex is ArgumentException || ex is OverflowException) ? 1 : 0) != 0)
		{
			return Unknown(path);
		}
	}

	public ContextAdvice Advice(long availableBytes, long projectorBytes, string cache = "f16")
	{
		double num = (double)(FileBytes + projectorBytes) + 1073741824.0;
		double num2 = ((cache == "q8_0") ? (17.0 / 32.0) : ((!(cache == "q4_0")) ? 1.0 : (9.0 / 32.0)));
		double num3 = num2;
		double num4 = ((double)availableBytes - num) / Math.Max(1.0, KvBytesPerToken * num3);
		int num5 = (int)Math.Clamp(num4, 2048.0, NativeContext);
		int num6 = Math.Max(2048, num5 / 1024 * 1024);
		return new ContextAdvice(Math.Max(2048, (int)(Math.Min(16384.0, (double)num6 * 0.5) / 1024.0) * 1024), num6, NativeContext, MetadataKnown ? "Estimate based on weights and KV cache." : "Model metadata unavailable; conservative estimates.")
		{
			FitsMinimum = (num4 >= 2048.0),
			FitsNativeMaximum = (num4 >= (double)NativeContext)
		};
	}
}
