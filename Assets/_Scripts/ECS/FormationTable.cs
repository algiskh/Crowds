using UnityEngine;

namespace ECS
{
	public enum FormationType : byte
	{
		Column, // column — one behind another
		Wedge,  // wedge "▼" — leader in front, followers fanning back
		Line    // line — all in one row
	}

	/// <summary>
	/// One formation slot: parent slot index, local offset in "formation units"
	/// (x — right, z — forward), and a facing trim angle (unused in v1 — mobs
	/// face their movement/the leader). Slot 0 is always the leader at the origin.
	/// </summary>
	public readonly struct FormationSlot
	{
		public readonly int Base;     // parent slot index, -1 = root/leader
		public readonly float X, Z;   // formation units (multiplied by spacing)
		public readonly float Angle;  // radians, facing trim offset

		public FormationSlot(int b, float x, float z, float a)
		{
			Base = b; X = x; Z = z; Angle = a;
		}
	}

	/// <summary>
	/// Formation tables (the minimal set from §2 of the instructions) + building the slot local offsets (§4).
	/// </summary>
	public static class FormationTable
	{
		private const float Q = Mathf.PI / 2f;

		private static readonly FormationSlot[] Column =
		{
			new(-1, 0,  0, 0),
			new( 0, 0, -1,  0.25f * Q),
			new( 1, 0, -1, -0.25f * Q),
			new( 2, 0, -1,  Q),
			new( 3, 0, -1, 0),
			new( 4, 0, -1,  0.25f * Q),
			new( 5, 0, -1, -0.25f * Q),
			new( 6, 0, -1,  Q),
		};

		private static readonly FormationSlot[] Wedge =
		{
			new(-1,  0,  0,     0),
			new( 0,  1, -1,      0.25f * Q),
			new( 0, -1, -1.33f, -0.25f * Q),
			new( 1,  1, -1,      0.5f * Q),
			new( 2, -1, -1.33f, -0.25f * Q),
			new( 3,  1, -1,      0.5f * Q),
			new( 4, -1, -1.33f, -0.25f * Q),
		};

		private static readonly FormationSlot[] Line =
		{
			new(-1,  0, 0, 0),
			new( 0,  1, 0, 0),
			new( 0, -1, 0, 0),
			new( 1,  1, 0, 0),
			new( 2, -1, 0, 0),
			new( 3,  1, 0, 0),
			new( 4, -1, 0, 0),
		};

		public static FormationSlot[] Get(FormationType type) => type switch
		{
			FormationType.Column => Column,
			FormationType.Line => Line,
			_ => Wedge,
		};

		/// <summary>
		/// Computes the slot local offsets (§4). spacingX/Z — meters per formation unit.
		/// Returns an array of length min(count, table size); slot 0 (the leader) = (0,0,0).
		/// </summary>
		public static Vector3[] ComputeOffsets(FormationType type, int count, float spacingX, float spacingZ)
		{
			var table = Get(type);
			count = Mathf.Clamp(count, 1, table.Length);
			var offsets = new Vector3[count];

			for (int j = 0; j < count; j++)
			{
				var slot = table[j];
				if (slot.Base >= 0 && slot.Base < j)
				{
					offsets[j] = new Vector3(
						offsets[slot.Base].x + spacingX * slot.X,
						0f,
						offsets[slot.Base].z + spacingZ * slot.Z);
				}
				else
				{
					offsets[j] = new Vector3(slot.X, 0f, slot.Z);
				}
			}

			return offsets;
		}
	}
}
