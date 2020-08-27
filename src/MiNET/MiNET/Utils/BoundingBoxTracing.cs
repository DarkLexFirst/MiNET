using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using MiNET.Blocks;
using MiNET.Worlds;

namespace MiNET.Utils
{
	public class BoundingBoxTracing
	{
		public Level Level { get; set; }

		public BoundingBox BoundingBox { get; set; }

		public Vector3 Position { get; set; }
		public Vector3 Offset { get; set; }

		public BoundingBoxTracing(Level level, BoundingBox boundingBox, Vector3 position, Vector3 offset)
		{
			Level = level;
			BoundingBox = boundingBox;
			Position = position;
			Offset = offset;
		}

		private int _stepsCount = 5;
		public bool Next(out BlockFace collidedFace)
		{
			var absOffset = Vector3.Abs(Offset);
			var maxDir = Math.Max(Math.Max(absOffset.X, absOffset.Y), absOffset.Z);
			var step = Offset / maxDir;

			Position += Next(step, _stepsCount, false, out bool collided, out collidedFace);
			return collided;
		}

		public bool ToDestination(out BlockFace collidedFace)
		{
			var step = Vector3.Normalize(Offset);

			step *= BoundingBox.Max.X - BoundingBox.Min.X;

			int stepsCount = (int) (Offset.Length() / step.Length());

			Position += Next(step, stepsCount, true, out bool collided, out collidedFace);

			return collided;
		}

		private Vector3 Next(Vector3 step, int stepsCount, bool limited, out bool collided, out BlockFace collidedFace)
		{
			var offset = GetAdjustedLengthFromCollision(step, stepsCount, out collided, out collidedFace);

			if (limited && Offset.Length() < offset.Length())
				return Offset;

			return offset;
		}

		private Vector3 GetAdjustedLengthFromCollision(Vector3 step, int stepsCount, out bool collided, out BlockFace collidedFace)
		{
			collided = false;
			collidedFace = BlockFace.None;

			var fullOffset = Vector3.Zero;

			var collidedVector = Vector3.Zero;
			for (int i = 1; i <= stepsCount + 1 && !collided; i++)
			{
				var distVec = step * i;

				var boundingBox = new BoundingBox(BoundingBox.Min, BoundingBox.Max).OffsetBy(distVec);
				var coordinates = GetIntersectedBlocks(boundingBox);

				if (coordinates.Count == 0) continue;

				var ownCorner = GetCorner(boundingBox, Offset);

				var maxOffset = Vector3.Zero;

				foreach (var coords in coordinates)
				{
					var block = Level.GetBlock(coords);

					if (!block.IsSolid) continue;

					var blockBoundingBox = block.GetBoundingBox();

					if (!boundingBox.Intersects(blockBoundingBox)) continue;

					var targetCorner = GetCorner(blockBoundingBox, -Offset);

					var diff = (ownCorner - targetCorner) / step;

					var baseVector = Vector3.UnitX;
					var minVal = diff.X;

					if(diff.Y < minVal)
					{
						baseVector = Vector3.UnitY;
						minVal = diff.Y;
					}
					if (diff.Z < minVal)
					{
						baseVector = Vector3.UnitZ;
						minVal = diff.Z;
					}

					var offset = step * minVal;
					if (offset.Length() > maxOffset.Length())
					{
						maxOffset = offset;
						collidedVector = step / Vector3.Abs(step) * baseVector;
						collided = true;
					}
				}

				fullOffset += step - maxOffset;
			}

			if(collided) collidedFace = GetFaceFromVector(collidedVector);

			return fullOffset;
		}

		public static BlockFace GetFaceFromVector(BlockCoordinates vector)
		{
			if (vector == Level.Down)
				return BlockFace.Down;
			else if (vector == Level.Up)
				return BlockFace.Up;
			else if (vector == Level.North)
				return BlockFace.North;
			else if (vector == Level.South)
				return BlockFace.South;
			else if (vector == Level.West)
				return BlockFace.West;
			else if (vector == Level.East)
				return BlockFace.East;

			return BlockFace.None;
		}

		public static Vector3 GetVectorFromFace(BlockFace face)
		{
			switch (face)
			{
				case BlockFace.Down:
					return Level.Down;
				case BlockFace.Up:
					return Level.Up;
				case BlockFace.North:
					return Level.North;
				case BlockFace.South:
					return Level.South;
				case BlockFace.West:
					return Level.West;
				case BlockFace.East:
					return Level.East;
				default:
					return Vector3.Zero;
			}
		}

		private Vector3 GetCorner(BoundingBox boundingBox, Vector3 direction)
		{
			var result = new Vector3(
				direction.X < 0 ? boundingBox.Min.X : boundingBox.Max.X,
				direction.Y < 0 ? boundingBox.Min.Y : boundingBox.Max.Y,
				direction.Z < 0 ? boundingBox.Min.Z : boundingBox.Max.Z);

			return result;
		}

		private List<BlockCoordinates> GetIntersectedBlocks(BoundingBox boundingBox)
		{
			var coordinates = new List<BlockCoordinates>();

			for (var x = boundingBox.Min.X; x <= boundingBox.Max.X; x = Math.Min(x + 1, boundingBox.Max.X))
			{
				for (var y = boundingBox.Min.Y; y <= boundingBox.Max.Y; y = Math.Min(y + 1, boundingBox.Max.Y))
				{
					for (var z = boundingBox.Min.Z; z <= boundingBox.Max.Z; z = Math.Min(z + 1, boundingBox.Max.Z))
					{
						var coords = new BlockCoordinates(new Vector3(x, y, z));
						if (!coordinates.Contains(coords))
						{
							coordinates.Add(coords);
						}

						if (z == boundingBox.Max.Z) break;
					}
					if (y == boundingBox.Max.Y) break;
				}
				if (x == boundingBox.Max.X) break;
			}
			return coordinates;
		}
	}
}
