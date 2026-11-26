using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace AquilaAvatarProtocol;

public static class AvatarModelFile
{
	public const long MaxBytes = 134217728L;

	public static AvatarModelInfo Inspect(string path)
	{
		if (!Path.GetExtension(path).Equals(".vrm", StringComparison.OrdinalIgnoreCase))
		{
			throw new ArgumentException("Choose a VRM 1.0 file (.vrm).");
		}
		using (FileStream fileStream = File.OpenRead(path))
		{
			using BinaryReader binaryReader = new BinaryReader(fileStream, Encoding.UTF8, leaveOpen: true);
			if (fileStream.Length < 24 || fileStream.Length > 134217728)
			{
				throw new ArgumentException("Avatar files must be valid VRM files no larger than 128 MB.");
			}
			if (binaryReader.ReadUInt32() != 1179937895 || binaryReader.ReadUInt32() != 2 || binaryReader.ReadUInt32() != fileStream.Length)
			{
				throw new ArgumentException("This file is not a valid binary VRM/glTF model.");
			}
			uint num = binaryReader.ReadUInt32();
			if (binaryReader.ReadUInt32() != 1313821514 || num == 0 || num > 4194304 || num > fileStream.Length - 20)
			{
				throw new ArgumentException("Avatar metadata is invalid or exceeds the 4 MB limit.");
			}
			using JsonDocument jsonDocument = JsonDocument.Parse(binaryReader.ReadBytes((int)num));
			JsonElement rootElement = jsonDocument.RootElement;
			if (!rootElement.TryGetProperty("extensions", out var value) || !value.TryGetProperty("VRMC_vrm", out var value2) || !value2.TryGetProperty("specVersion", out var value3) || value3.GetString() != "1.0")
			{
				throw new ArgumentException("This avatar needs to be VRM 1.0. Re-export VRM 0.x models as VRM 1.0 before loading.");
			}
			if (!value2.TryGetProperty("humanoid", out var value4) || !value4.TryGetProperty("humanBones", out var value5) || !value5.TryGetProperty("hips", out var value6) || !value5.TryGetProperty("head", out value6))
			{
				throw new ArgumentException("This VRM is missing its humanoid hips or head mapping.");
			}
			string[] array = new string[2] { "buffers", "images" };
			foreach (string propertyName in array)
			{
				if (!rootElement.TryGetProperty(propertyName, out var value7))
				{
					continue;
				}
				foreach (JsonElement item in value7.EnumerateArray())
				{
					if (item.TryGetProperty("uri", out var value8))
					{
						string @string = value8.GetString();
						if (@string != null && @string.Length > 0 && !@string.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
						{
							throw new ArgumentException("Use a self-contained VRM with embedded textures and buffers; external asset paths are not supported.");
						}
					}
				}
			}
			value2.TryGetProperty("meta", out var value9);
			string authors = "Unknown creator";
			if (value9.ValueKind == JsonValueKind.Object && value9.TryGetProperty("authors", out var value10) && value10.ValueKind == JsonValueKind.Array)
			{
				authors = string.Join(", ", from n in (from n in value10.EnumerateArray()
						where n.ValueKind == JsonValueKind.String
						select n).Take(8)
					select (n.GetString() ?? "").TruncateModel(100));
			}
			bool blink = false;
			bool mouth = false;
			if (value2.TryGetProperty("expressions", out var value11) && value11.TryGetProperty("preset", out var value12))
			{
				blink = value12.TryGetProperty("blink", out value6);
				mouth = value12.TryGetProperty("aa", out value6);
			}
			JsonElement value13;
			string usage = "Avatar use: " + Text(value9, "avatarPermission", "unspecified") + " · Redistribution: " + ((value9.ValueKind == JsonValueKind.Object && value9.TryGetProperty("allowRedistribution", out value13) && value13.ValueKind == JsonValueKind.True) ? "allowed" : "check licence");
			return new AvatarModelInfo(Text(value9, "name", Path.GetFileNameWithoutExtension(path)), authors, Text(value9, "licenseUrl"), usage, blink, mouth, fileStream.Length);
		}
		static string Text(JsonElement e, string key, string fallback = "")
		{
			if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(key, out var value14) || value14.ValueKind != JsonValueKind.String)
			{
				return fallback;
			}
			return (value14.GetString() ?? fallback).Substring(0, Math.Min(value14.GetString()?.Length ?? 0, 500));
		}
	}

	private static string TruncateModel(this string text, int length)
	{
		return text.Substring(0, Math.Min(length, text.Length));
	}
}
