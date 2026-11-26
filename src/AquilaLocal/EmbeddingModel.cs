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
		string text = Path.Combine(directory, "model.onnx");
		string path = Path.Combine(directory, "vocab.txt");
		Verify(text, "B941BF19F1F1283680F449FA6A7336BB5600BDCD5F84D10DDC5CD72218A0FD21");
		Verify(path, "07ECED375CEC144D27C900241F3E339478DEC958F92FDDBC551F295C992038A3");
		Tokenizer = new MiniTokenizer(path);
		OrtEnv.Instance().DisableTelemetryEvents();
		using SessionOptions sessionOptions = new SessionOptions
		{
			IntraOpNumThreads = 1,
			InterOpNumThreads = 1,
			ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
			GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
			EnableMemoryPattern = false,
			EnableCpuMemArena = false,
			LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR
		};
		sessionOptions.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
		sessionOptions.AddSessionConfigEntry("session.inter_op.allow_spinning", "0");
		session = new InferenceSession(text, sessionOptions);
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
			List<NamedOnnxValue> inputs = new List<NamedOnnxValue>
			{
				NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(array2, array3)),
				NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(Enumerable.Repeat(1L, array2.Length).ToArray(), array3)),
				NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>(new long[array2.Length], array3))
			};
			using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> source2 = session.Run(inputs);
			Runs++;
			Tensor<float> tensor = source2.First().AsTensor<float>();
			if (tensor.Rank != 3 || tensor.Dimensions[2] != 384)
			{
				throw new InvalidDataException("Unexpected MiniLM output dimensions.");
			}
			for (int i = 0; i < array2.Length; i++)
			{
				for (int j = 0; j < 384; j++)
				{
					array[j] += tensor[new int[3] { 0, i, j }];
				}
			}
			num += array2.Length;
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
		for (int num3 = 0; num3 < 384; num3++)
		{
			array[num3] = (float)((double)array[num3] / num2);
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
