namespace AquilaAvatarProtocol;

public sealed class AvatarPresentation
{
	public int Fps { get; set; } = 30;

	public float Distance { get; set; } = 4.2f;

	public float Height { get; set; } = 1f;

	public float Fov { get; set; } = 40f;

	public bool Valid()
	{
		int fps = Fps;
		if (fps >= 15 && fps <= 60 && float.IsFinite(Distance))
		{
			float distance = Distance;
			if (distance >= 2f && distance <= 5f && float.IsFinite(Height))
			{
				distance = Height;
				if (distance >= 0.6f && distance <= 2f && float.IsFinite(Fov))
				{
					distance = Fov;
					if (distance >= 25f)
					{
						return distance <= 55f;
					}
					return false;
				}
			}
		}
		return false;
	}
}
