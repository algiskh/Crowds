using UnityEngine;

namespace ECS
{
	public enum FormationType : byte
	{
		Column, // колонна — друг за другом
		Wedge,  // клин «▼» — ведущий впереди, ведомые веером назад
		Line    // шеренга — все в одну линию
	}

	/// <summary>
	/// Один слот строя: индекс родительского слота, локальный офсет в «единицах строя»
	/// (x — вправо, z — вперёд), и угол доводки лицом (в v1 не используется — мобы
	/// смотрят по движению/на ведущего). Слот 0 — всегда ведущий в начале координат.
	/// </summary>
	public readonly struct FormationSlot
	{
		public readonly int Base;     // индекс родительского слота, -1 = корень/ведущий
		public readonly float X, Z;   // единицы строя (умножаются на spacing)
		public readonly float Angle;  // радианы, офсет доводки лицом

		public FormationSlot(int b, float x, float z, float a)
		{
			Base = b; X = x; Z = z; Angle = a;
		}
	}

	/// <summary>
	/// Таблицы строёв (минимальный набор §2 инструкции) + сборка локальных офсетов слотов (§4).
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
		/// Считает локальные офсеты слотов (§4). spacingX/Z — метров на единицу строя.
		/// Возвращает массив длиной min(count, размер таблицы); слот 0 (ведущий) = (0,0,0).
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
