using System.Collections.Generic;

namespace AquilaLocal;

public sealed class PersonalityCatalog
{
	public int Version { get; set; } = 1;


	public List<PersonalityProfile> Profiles { get; set; } = new List<PersonalityProfile>();

}
