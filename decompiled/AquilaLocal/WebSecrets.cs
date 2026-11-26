using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace AquilaLocal;

public static class WebSecrets
{
	private struct Blob
	{
		public int Length;

		public nint Data;
	}

	[DllImport("crypt32.dll", SetLastError = true)]
	private static extern bool CryptProtectData(ref Blob input, nint description, nint entropy, nint reserved, nint prompt, int flags, out Blob output);

	[DllImport("crypt32.dll", SetLastError = true)]
	private static extern bool CryptUnprotectData(ref Blob input, nint description, nint entropy, nint reserved, nint prompt, int flags, out Blob output);

	[DllImport("kernel32.dll")]
	private static extern nint LocalFree(nint value);

	private static byte[] Transform(byte[] bytes, bool protect)
	{
		Blob blob = default(Blob);
		blob.Length = bytes.Length;
		blob.Data = Marshal.AllocHGlobal(bytes.Length);
		Blob input = blob;
		Blob output = default(Blob);
		try
		{
			Marshal.Copy(bytes, 0, input.Data, bytes.Length);
			if (!(protect ? CryptProtectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output) : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)))
			{
				throw new CryptographicException("Windows could not access the saved search credential. Re-enter it in Internet settings.");
			}
			byte[] array = new byte[output.Length];
			Marshal.Copy(output.Data, array, 0, array.Length);
			return array;
		}
		finally
		{
			Marshal.FreeHGlobal(input.Data);
			if (output.Data != IntPtr.Zero)
			{
				LocalFree(output.Data);
			}
		}
	}

	public static string Protect(string key)
	{
		if (key.Length != 0)
		{
			return Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(key), protect: true));
		}
		return "";
	}

	public static string Unprotect(string encrypted)
	{
		if (encrypted.Length != 0)
		{
			return Encoding.UTF8.GetString(Transform(Convert.FromBase64String(encrypted), protect: false));
		}
		return "";
	}
}
