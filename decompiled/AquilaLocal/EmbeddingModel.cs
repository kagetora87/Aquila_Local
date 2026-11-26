using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace AquilaLocal;

public sealed class EmbeddingModel : IDisposable
{
	public const string Revision = "MiniLM-1110a243-quint8-avx2-tokenizer-v1";

	public const int Dimensions = 384;

	private readonly InferenceSession session;

	public MiniTokenizer Tokenizer { get; }

	public int Runs { get; private set; }

	public EmbeddingModel(string directory)
	{
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		//IL_005d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0064: Unknown result type (might be due to invalid IL or missing references)
		//IL_006c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_007a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0082: Expected O, but got Unknown
		//IL_00a5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00af: Expected O, but got Unknown
		string text = Path.Combine(directory, "model.onnx");
		string path = Path.Combine(directory, "vocab.txt");
		Verify(text, "B941BF19F1F1283680F449FA6A7336BB5600BDCD5F84D10DDC5CD72218A0FD21");
		Verify(path, "07ECED375CEC144D27C900241F3E339478DEC958F92FDDBC551F295C992038A3");
		Tokenizer = new MiniTokenizer(path);
		OrtEnv.Instance().DisableTelemetryEvents();
		SessionOptions val = new SessionOptions
		{
			IntraOpNumThreads = 1,
			InterOpNumThreads = 1,
			ExecutionMode = (ExecutionMode)0,
			GraphOptimizationLevel = (GraphOptimizationLevel)99,
			EnableMemoryPattern = false,
			EnableCpuMemArena = false,
			LogSeverityLevel = (OrtLoggingLevel)3
		};
		try
		{
			val.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
			val.AddSessionConfigEntry("session.inter_op.allow_spinning", "0");
			session = new InferenceSession(text, val);
		}
		finally
		{
			((IDisposable)val)?.Dispose();
		}
	}

	private static void Verify(string path, string expected)
	{
		using FileStream source = File.OpenRead(path);
		if (Convert.ToHexString(SHA256.HashData(source)) != expected)
		{
			throw new InvalidDataException("Bundled MiniLM file failed integrity verification: " + Path.GetFileName(path));
		}
	}

	public float[] Encode(string text, CancellationToken ct)
	{
		List<long> source = Tokenizer.Pieces(text);
		float[] array = new float[384];
		int num = 0;
		foreach (long[] item in source.Chunk(254).DefaultIfEmpty(Array.Empty<long>()))
		{
			ct.ThrowIfCancellationRequested();
			long[] array2 = new long[1] { 101L }.Concat(item).Append(102L).ToArray();
			int[] array3 = new int[2] { 1, array2.Length };
			List<NamedOnnxValue> list = new List<NamedOnnxValue>
			{
				NamedOnnxValue.CreateFromTensor<long>("input_ids", (Tensor<long>)(object)new DenseTensor<long>((Memory<long>)array2, (ReadOnlySpan<int>)array3, false)),
				NamedOnnxValue.CreateFromTensor<long>("attention_mask", (Tensor<long>)(object)new DenseTensor<long>((Memory<long>)Enumerable.Repeat(1L, array2.Length).ToArray(), (ReadOnlySpan<int>)array3, false)),
				NamedOnnxValue.CreateFromTensor<long>("token_type_ids", (Tensor<long>)(object)new DenseTensor<long>((Memory<long>)new long[array2.Length], (ReadOnlySpan<int>)array3, false))
			};
			IDisposableReadOnlyCollection<DisposableNamedOnnxValue> val = session.Run((IReadOnlyCollection<NamedOnnxValue>)list);
			try
			{
				Runs++;
				Tensor<float> val2 = ((NamedOnnxValue)((IEnumerable<DisposableNamedOnnxValue>)val).First()).AsTensor<float>();
				if (val2.Rank != 3 || val2.Dimensions[2] != 384)
				{
					throw new InvalidDataException("Unexpected MiniLM output dimensions.");
				}
				for (int i = 0; i < array2.Length; i++)
				{
					for (int j = 0; j < 384; j++)
					{
						array[j] += val2[new int[3] { 0, i, j }];
					}
				}
				num += array2.Length;
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}
		for (int k = 0; k < 384; k++)
		{
			array[k] /= Math.Max(1, num);
		}
		double num2 = Math.Sqrt(array.Sum((float f) => (double)f * (double)f));
		if (num2 < 1E-12 || !double.IsFinite(num2))
		{
			throw new InvalidDataException("MiniLM returned an invalid embedding.");
		}
		for (int l = 0; l < 384; l++)
		{
			array[l] = (float)((double)array[l] / num2);
		}
		return array;
	}

	public static float Similarity(float[] a, float[] b)
	{
		double num = 0.0;
		for (int i = 0; i < 384; i++)
		{
			num += (double)a[i] * (double)b[i];
		}
		return (float)Math.Clamp(num, -1.0, 1.0);
	}

	public void Dispose()
	{
		session.Dispose();
	}
}
