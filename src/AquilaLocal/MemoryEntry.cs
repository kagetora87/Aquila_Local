using System;
using System.Collections.Generic;
using System.IO;

namespace AquilaLocal;

public sealed class MemoryEntry
{
	public string Id { get; set; } = Guid.NewGuid().ToString("N");

	public string Scope { get; set; } = "personal";

	public string Kind { get; set; } = "fact";

	public string Topic { get; set; } = "";

	public string Value { get; set; } = "";

	public string State { get; set; } = "active";

	public bool Pinned { get; set; }

	public string Origin { get; set; } = "automatic";

	public List<MemorySource> Sources { get; set; } = new List<MemorySource>();

	public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;

	public DateTimeOffset Updated { get; set; } = DateTimeOffset.Now;

	public DateTimeOffset? LastUsed { get; set; }

	public int Confirmations { get; set; } = 1;

	public string ReplacedBy { get; set; } = "";

	public string ScopeLabel
	{
		get
		{
			if (!(Scope == "personal"))
			{
				string scope = Scope;
				return "Project · " + Path.GetFileName(scope.Substring(8, scope.Length - 8));
			}
			return "Personal";
		}
	}

	public string Display => $"{(Pinned ? "★ " : "")}{Topic} · {ScopeLabel} · {State}";
}
