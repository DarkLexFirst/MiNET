using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using MiNET.Blocks;
using MiNET.Worlds;

namespace MiNET.Utils
{
	public class RayTracing
	{
		public Level Level { get; set; }

		public Vector3 Position { get; set; }
		public Vector3 Destination { get; set; }
		public Vector3 Aura { get; set; }

		public RayTracing(Level level, Vector3 position, Vector3 destination, Vector3 aura)
		{
			Level = level;
			Position = position;
			Destination = destination;
			Aura = aura;
		}

		private int _stepsCount = 5;
		public bool Next()
		{
			var maxDir = Math.Max(Math.Max(Destination.X, Destination.Y), Destination.Z);
			var step = Destination / maxDir;

			Position = Next(step, _stepsCount, out bool collided);
			return collided;
		}

		public bool ToDestination()
		{
			var maxDir = Math.Max(Math.Max(Destination.X, Destination.Y), Destination.Z);
			var step = Destination / maxDir;

			int stepsCount = (int)(Destination.Length() / step.Length()) + 1;

			Position = Next(step, stepsCount, out bool collided);
			return collided;
		}

		private Vector3 Next(Vector3 step, int stepsCount, out bool collided)
		{
			var newPosition = GetAdjustedLengthFromCollision(step, stepsCount);

			collided = newPosition != Vector3.Zero;

			if (!collided)
			{
				newPosition = step * stepsCount;
			}

			return newPosition;
		}

		private Vector3 GetAdjustedLengthFromCollision(Vector3 step, int stepsCount)
		{
			var position = Position;
			for (int i = 0; i < stepsCount; i++)
			{
				var distVec = step * i;

				var minCollision = Destination;

				BlockCoordinates xBlockPos = position + new Vector3(distVec.X, 0, 0);
				Block xBlockX = Level.GetBlock(xBlockPos);
				var xCollision = GetAdjustedLengthFromBBCollision(xBlockX, position, step);
				if (minCollision != Vector3.Zero && minCollision.Length() > xCollision.Length())
					minCollision = xCollision;

				BlockCoordinates yBlockPos = position + new Vector3(0, distVec.Y, 0);
				Block yBlockX = Level.GetBlock(yBlockPos);
				var yCollision = GetAdjustedLengthFromBBCollision(yBlockX, position, step);
				if (minCollision != Vector3.Zero && minCollision.Length() > yCollision.Length())
					minCollision = yCollision;

				BlockCoordinates zBlockPos = position + new Vector3(0, 0, distVec.Z);
				Block zBlockX = Level.GetBlock(zBlockPos);
				var zCollision = GetAdjustedLengthFromBBCollision(zBlockX, position, step);
				if (minCollision != Vector3.Zero && minCollision.Length() > zCollision.Length())
					minCollision = zCollision;

				if (minCollision != Vector3.Zero)
				{
					return minCollision;
				}
			}

			return Vector3.Zero;
		}

		private Vector3 GetAdjustedLengthFromBBCollision(Block block, Vector3 rayPosition, Vector3 rayDirection)
		{
			if (block.IsSolid)
			{
				Ray ray = new Ray(rayPosition, rayDirection);
				var distance = ray.Intersects(block.GetBoundingBox());
				if (distance.HasValue)
				{
					return ray.Direction * (new Vector3((float)distance) - Aura);
				}
			}

			return Vector3.Zero;
		}
	}
}
